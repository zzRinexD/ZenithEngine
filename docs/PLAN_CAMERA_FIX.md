# PLAN - Fix de OrbitFollowCamera y ThirdPersonCharacterMovement

## Contexto

Dos componentes escritos por IA (`OrbitFollowCamera.cs`, 180 lineas, y
`ThirdPersonCharacterMovement.cs`, 163 lineas) se sienten mal al jugarlos. La auditoria
previa los identifico asi: **5 bugs** (pitch invertido, lerp dependiente de framerate,
comentario mentiroso, lookAt inconsistente, Slerp sin clamp), **7 problemas de feel**
(teleport en colision, sensibilidad no normalizada, cursor lock, pivote/punto de mira
desacoplados, rotacion instantanea, robo de cursor) y **8 features faltantes** (zoom,
colision ON, gamepad, invertir Y, snap por teleport, look-ahead, FOV kick, shoulder offset).

Este plan **arregla los 5 bugs** y **anade las features como opciones configurables**,
sin reescribir la arquitectura.

**Lo que NO se hace aqui:** reescribir los componentes, cambiarles el nombre, tocar el
sistema de input, ni imponer nada al usuario. Nada de esto cambia de forma observable
salvo que el usuario lo active desde el Inspector.

---

## Filosofia

- **Bugs: obligatorios.** Se arreglan siempre, no son opcionales.
- **Features: opcionales.** Cada feature nueva arranca con un toggle
  `[SerializeField] private bool _xxxEnabled`, y sus parametros se ocultan detras de
  `[EnableIf("_xxxEnabled")]` para que el Inspector no se llene de campos muertos.
- **Defaults: sensatos.** Ningun default rompe el comportamiento actual. Para todo lo que
  cambia el feel, el default replica exactamente lo que hace hoy el codigo.
- **Tuning: expuesto, no hardcodeado.** Todo numero que se pueda tocar va a un campo.
- **El usuario manda.** El "se siente bien" no lo decide el codigo, lo decide el usuario.
  Por eso las features con feel se verifican a mano, no con asserts.

### Convencion de campos (importante)

Zenith serializa **por nombre de campo** y el Inspector dibuja tanto `public` como
`[SerializeField] private` (`RuntimeUtils.cs:341-345`). Por eso:

- `[SerializeField]` **no** es lo que hace que un campo aparezca en el Inspector. Los dos
  componentes auditados ya usan campos `public`, asi que **ya son configurables**.
- **Renombrar** `Sensitivity` a `_sensitivity` **rompe las escenas y prefabs ya
  guardados**: el valor serializado no se reasigna al nombre nuevo. No hay atributo tipo
  `FormerlySerializedAs` en el motor. **No se renombran los campos existentes.**
- Los campos **nuevos** si siguen el estilo de la casa
  (`[SerializeField] private _camelCase`), como en `AudioSource.cs:40-72`.

---

## Objetivos

- [ ] Arreglar los 5 bugs identificados
- [ ] Documentar en el Inspector los campos que ya son configurables pero no tienen
      tooltip (`Sensitivity`, dampings, `Distance`, `TargetHeight`, limites de pitch)
- [ ] Anadir las features nuevas como opciones (con toggle + `EnableIf`)
- [ ] Cubrir con tests automatizados lo que es determinista (matematicas de suavizado,
      clamp, normalizacion, zoom, gamepad)
- [ ] Dejar escrito el criterio manual para lo que no se puede testear (feel)

---

## No-objetivos (fuera de alcance)

- No se reescribe la arquitectura de los componentes ni se parte el `LateUpdate` en
  metodos mas pequenos (salvo lo que exija un bug).
- No se cambia el nombre ni la ruta del menu de los componentes
  (`[AddComponentMenu("Camera/Orbit Follow Camera")]`,
  `[AddComponentMenu("Character/Third Person Character Movement")]`) - las escenas y
  prefabs los referencian por tipo.
- No se tocan `Input`, `InputAction`, `DefaultInputHandler` ni ningun otro archivo de
  input **salvo los comentarios mentirosos del Bug 1.3**.
- No se anaden las features que la auditoria listo pero que aqui no se concretan
  (look-ahead, FOV kick, shoulder offset): requieren decidir valores de diseno y el
  usuario no los ha pedido. Quedan para un plan aparte.
- No se toca `ThirdPersonCharacterMovement` mas alla del Bug 1.2 / 1.5 (el jitter de
  movimiento y el `StrafeMode` no estan en la auditoria).
- No se normaliza la convencion de nombres de los campos existentes (ver "Convencion de
  campos" arriba).

---

## Estado actual

**Veredicto: tuning + arreglos puntuales. NO reescritura.**

El esqueleto esta bien. El pivote ya se suaviza (`OrbitFollowCamera.cs:106-119`), la
rotacion ya usa suavizado exponencial frame-rate independent (`:96-99`), la colision ya
existe (`:127-141`), el cursor ya se restaura en `OnDisable` (`:51-56`), el re-lock con
click izquierdo ya esta (`:67-73`) y el estado del cursor ya se re-evalua por si cambia
en runtime (`:63`, `:152-167`). El componente esta **mas maduro de lo que sugiere la
auditoria**: 2 de los 7 problemas de feel (cursor lock ausente, robo de cursor en
`OnEnable`) **ya estan resueltos** en el codigo actual - no se tocan.

Lo que queda son 5 bugs de una linea cada uno y features que son el mayor gap: el
`LateUpdate` no tiene zoom, no tiene gamepad, no normaliza la sensibilidad, y su colision
salta de golpe.

**Cobertura de tests: 0.** No existe ni un test para ninguno de los dos componentes
(`Zenith.Runtime.Test/` y `Zenith.Editor.Test/` no los mencionan). Esto es recuperable:
`FakeInputHandler` (`Zenith.Runtime.Test/InputTestHelpers/FakeInputHandler.cs`) permite
inyectar delta de raton, rueda y ejes de gamepad, y `scene.Update()` bombea
Start -> Update -> LateUpdate (`Scene.cs:936-952`). O sea, **casi todo es testeable
automaticamente**.

---

## Fases

### Fase 0 - Preparacion (~20 min)

- [ ] Grabar la linea base: `git rev-parse --short HEAD` (`aa52d597`) y
      `git status --short` debe salir vacio. **Verificado hoy: `main` esta limpio.**
- [ ] Grabar video de 30s del comportamiento actual (orbitar, pitch, correr, rotar
      personaje). Sin esto no hay forma de argumentar "se siente mejor".
- [ ] Anotar la linea base de tests: `dotnet test` y guardar el recuento exacto
      (referencia actual del repo: 1274 Runtime / 722 Editor - **confirmar, no asumir**).
- [ ] Crear rama `tweak/camera-feel` desde `main`.

**Esfuerzo:** 20 min (15 min de grabacion + 5 min de rama).

---

### Fase 1 - Bugs P0 (obligatorios, cambios pequenos)

#### Bug 1.1 - Pitch invertido **y su rango invertido con el**

- **Archivo:** `OrbitFollowCamera.cs:91` y `OrbitFollowCamera.cs:25-26`
- **Diagnostico:** el signo de la entrada *no* es el bug aislado; el bug es la
  **combinacion** entrada + convencion de `FromEuler`. En este motor el pitch positivo
  es mirar **abajo** (la camara del editor lo calcula asi,
  `EditorCamera.cs:638`: `-MathF.Sin(pitchRad)` en la Y del forward). La linea 91 resta
  el delta Y, asi que mover el raton **arriba** (delta negativo) **aumenta** el pitch ->
  la camara mira **abajo**. Invertido.
- **Cambio (dos lineas, inseparable):**
  1. `OrbitFollowCamera.cs:91` - de
     `_pitchTarget -= Input.MouseDelta.Y * Sensitivity * mult;`
     a `_pitchTarget += Input.MouseDelta.Y * Sensitivity * mult;`
  2. `OrbitFollowCamera.cs:25-26` - invertir tambien el clamp y sus defaults, si no el
     rango queda al reves:
     - `MinPitch = -18f` -> `MinPitch = -36f`
     - `MaxPitch = 36f` -> `MaxPitch = 18f`
- **Por que el punto 2 es obligatorio:** hoy el rango es "18 grados arriba / 36 abajo"
  (asimetrico, y la asimetria confirma que los defaults se escribieron bajo la convencion
  "positivo = abajo"). Al invertir el signo, ese mismo rango pasaria a ser "36 arriba /
  18 abajo": un cambio de feel no pedido, colado dentro de un fix de bug.
- **Verificacion:** raton arriba -> la camara **sube** y se para en 18 grados sobre el
  horizonte; raton abajo -> baja hasta 36. Antes: lo contrario en ambos extremos.
- **Commit:** `Fix: Invert pitch sign and pitch range in OrbitFollowCamera.`

#### Bug 1.2 - Rotacion del personaje dependiente de framerate

- **Archivo:** `ThirdPersonCharacterMovement.cs:147-148`
- **Cambio:**
  ```csharp
  // antes
  model.Rotation = Quaternion.Slerp(model.Rotation, targetRotation, TurnSpeed * Time.DeltaTime);
  // despues
  float tTurn = Maths.Clamp(1f - MathF.Exp(-TurnSpeed * Time.DeltaTime), 0f, 1f);
  model.Rotation = Quaternion.Slerp(model.Rotation, targetRotation, tTurn);
  ```
- **Nota:** `TurnSpeed * Time.DeltaTime` como interpolacion lineal solo es correcto a un
  framerate concreto. A 30 FPS vale 0.4; a 144 FPS vale 0.083 - el personaje gira
  visiblemente mas rapido a 30 FPS. La forma exponencial es la misma que ya usan la
  camara (`:97`) y el movimiento (`:96`): mismo criterio, mismo codigo.
- **Verificacion:** en VSync OFF, forzar 30 FPS y 144 FPS con el limitador de FPS del
  editor -> el angulo por frame es igual. **Automatizable** (ver Fase 4).
- **Commit:** `Fix: Make character turn framerate-independent.`

#### Bug 1.3 - Comentarios mentirosos en `DefaultInputHandler`

- **Archivo:** `Zenith.Runtime/InputManagement/DefaultInputHandler.cs:68` y `:382`
- **Cambio:** no hay inversion de Y en ninguno de los dos sitios.
  - `:68` - `return new Float2(delta.X, delta.Y); // Invert Y to match gamepad (up = positive)`
    -> borrar el comentario. El codigo devuelve el delta crudo.
  - `:382` - `return new Float2(thumbstick.X, thumbstick.Y); // We flip y to make UP on the stick positive`
    -> borrar el comentario. Tampoco hay flip.
- **Por que importa mas de lo que parece:** el Bug 1.1 existe precisamente porque alguien
  confio en la convencion "mouse up = positivo" que estos comentarios prometen. Un
  comentario que promete una normalizacion que no ocurre es la causa raiz de que el
  proximo agente vuelva a invertir el pitch.
- **Verificacion:** no queda ningun comentario en el archivo que describa una inversion
  que el codigo no hace.
- **Alternativa descartada:** implementar la normalizacion y que el comentario sea
  cierto. Eso es un cambio de comportamiento del input, fuera de alcance de este plan.
  Se elige borrar el comentario.
- **Commit:** `Docs: Remove comments claiming Y inversion that DefaultInputHandler does not do.`

#### Bug 1.4 - LookAt inconsistente con el pivote

- **Archivo:** `OrbitFollowCamera.cs:146-149`
- **Problema:** la posicion de la camara se deriva de `_smoothedTargetPos` (pivote
  suavizado, lineas 113-118), pero el punto de mira se recalcula desde
  `Target.Position` **crudo** (linea 147). Dos fuentes de verdad para la misma
  geometria -> el punto de mira "tira" del personaje mientras este se mueve.
- **Cambio:**
  ```csharp
  // antes
  Float3 lookAtPoint = Target.Position + new Float3(0, ChestOffset, 0);
  // despues
  Float3 lookAtPoint = _smoothedTargetPos + new Float3(0, ChestOffset - TargetHeight, 0);
  ```
- **Por que `ChestOffset - TargetHeight`:** `ChestOffset` (1.0) es hoy una altura medida
  desde los pies del target, mientras que `_smoothedTargetPos` ya lleva `TargetHeight`
  (1.5) sumado. Restarlo reproduce **exactamente** el mismo punto en el espacio del
  mundo (1.0 - 1.5 = -0.5) pero desde el pivote suavizado. No cambia el encuadre, solo
  quita el tiron.
- **Verificacion:** correr en linea recta y orbitar a la vez -> el personaje se queda
  quieto en el encuadre. Antes: la camara "pierde" al personaje al arrancar.
- **Commit:** `Fix: Use smoothed pivot for OrbitFollowCamera lookAt.`

#### Bug 1.5 - Slerp sin clamp

- **Archivo:** `ThirdPersonCharacterMovement.cs:148`
- **Cambio:** el `Maths.Clamp(tTurn, 0f, 1f)` del Bug 1.2 cubre esto (Slerp con `t` fuera
  de `[0,1]` extrapola). Si se prefiere el clamp en un paso propio y visible:
  ```csharp
  float tTurn = 1f - MathF.Exp(-TurnSpeed * Time.DeltaTime);
  tTurn = Maths.Clamp(tTurn, 0f, 1f);
  ```
- **Por que es defensivo y no necesario:** con `TurnSpeed >= 0` y `dt > 0`,
  `1 - Exp(-x)` ya cae en `(0, 1)`. El clamp protege contra `TurnSpeed` negativo
  puesto a mano desde el Inspector, que es exactamente el caso que el Inspector permite.
- **Verificacion:** poner `TurnSpeed = -5` en el Inspector -> el personaje no se
  teletransporta ni explota.
- **Commit:** `Fix: Clamp slerp t in ThirdPersonCharacterMovement.`

> **Nota de commits:** 1.2 y 1.5 tocan la misma expresion. Se pueden ir en dos commits
> (1.5 clamps, 1.2 cambia la formula) o en uno solo. **Recomendacion: uno solo**
> (`Fix: Make character turn framerate-independent and clamp t.`) - partir una linea
> en dos commits solo para cumplir una lista de la auditoria no aporta nada.

**Esfuerzo Fase 1: ~45 min** (5 bugs, 6 lineas de codigo, mas verificar en el editor).

---

### Fase 2 - Hacer descubribles los campos que ya son configurables

*(Clave de esta fase: los campos **ya estan en el Inspector** - son `public`. Lo que falta
es que el usuario sepa que son y que valores ponerles. Nada de esto cambia el
comportamiento.)*

#### Feature 2.1 - Tooltips en `OrbitFollowCamera`

- **Archivo:** `OrbitFollowCamera.cs:15-35`
- **Estado actual:** los 14 campos son `public` y por tanto editables, pero sin
  documentacion. `Range` y `Tooltip` ya estan soportados
  (`GameObject/Attributes/InspectorAttributes.cs:11,36`; `AudioSource.cs:40-72` es el
  ejemplo a imitar).
- **Cambio:** anadir `[Tooltip("...")]` a cada campo, sin tocar su nombre ni su default.
  Anadir `[Range]` donde el rango sea obvio:

  | Campo | Linea | Tooltip | Range |
  |---|---|---|---|
  | `Target` | 16 | "Transform al que sigue la camara. Sin el el componente no hace nada." | - |
  | `TargetHeight` | 17 | "Altura del pivote sobre los pies del target. Es el punto alrededor del que orbita la camara." | - |
  | `Mode` | 20 | "HoldRightClick: orbita solo con el boton derecho. LockedCursor: captura el cursor." | - |
  | `Distance` | 21 | "Distancia de la camara al pivote. El zoom con rueda modifica este valor en runtime." | - |
  | `Sensitivity` | 22 | "Sensibilidad del raton, en grados por pixel. Rango recomendado: 0.05 - 0.5." | - |
  | `HoldModeSensitivityMultiplier` | 23 | "Multiplicador de sensibilidad en modo HoldRightClick." | - |
  | `RotationSmoothing` | 24 | "Suavizado de la rotacion. Mayor = mas rigido e inmediato." | 5 - 30 |
  | `MinPitch` | 25 | "Limite inferior de inclinacion, en grados. Negativo = por encima del horizonte." | - |
  | `MaxPitch` | 26 | "Limite superior de inclinacion, en grados. Positivo = por debajo del horizonte." | - |
  | `FollowSmoothing` | 29 | "Suavizado de la posicion del pivote. Mayor = la camara pisa mas." | 5 - 30 |
  | `ChestOffset` | 30 | "Altura del punto de mira sobre los pies del target." | - |
  | `CollisionEnabled` | 33 | "Acerca la camara cuando un obstaculo se interpone." | - |
  | `CollisionRadius` | 34 | "Margen que deja la camara respecto al obstaculo." | - |
  | `MinDistance` | 35 | "Distancia minima a la que la colision puede acercar la camara." | - |

- **Tooltips de `ThirdPersonCharacterMovement`:** mismo tratamiento para `Camera`,
  `Controller`, `StrafeMode`, `Facing`, `MovementThreshold`, `TurnSpeed`.
- **Commit:** `Docs: Add inspector tooltips and ranges to camera and movement components.`

#### Feature 2.2 - Desambiguar los dos "min distance"

- **Archivo:** `OrbitFollowCamera.cs:32-35` (y el bloque nuevo de zoom en Fase 3)
- **Problema de nombres a resolver antes de escribir nada:** ya existe
  `public float MinDistance = 1f` en el bloque `[Header("Collision")]` (linea 35) - es
  el minimo **por colision**. La feature de zoom necesita sus propios min/max de
  distancia. Nombrarlos `_minDistance` / `_maxDistance` seria legal (distinto case) pero
  una trampa: dos campos con casi el mismo nombre visible en el mismo Inspector
  significando dos cosas distintas.
- **Cambio:** los nuevos campos de zoom se llaman **`_minZoomDistance`** /
  **`_maxZoomDistance`**, bajo `[Header("Zoom")]`, con tooltips que dicen explicitamente
  que son el rango de zoom y que `MinDistance` sigue siendo el minimo por colision.
- **Commit:** `Docs: Disambiguate collision and zoom distance fields in OrbitFollowCamera.`

**Esfuerzo Fase 2: ~1h 15 min** (es escribir tooltips, pero son 20 campos y hay que
revisar que cada uno describa lo que el codigo hace de verdad).

---

### Fase 3 - Features nuevas OPCIONALES (todas con toggle)

*(Cada feature nueva arranca con `[SerializeField] private bool _xxxEnabled`, y sus
parametros van con `[EnableIf("_xxxEnabled")]` para que se green cuando el toggle esta
OFF. Todas se escriben y se commitean **una por una**.)*

#### Feature 3.1 - Colision de camara con spring arm (suavizada)

- **Archivo:** `OrbitFollowCamera.cs:33` (toggle), `:127-141` (logica)
- **Estado actual:** `CollisionEnabled = false` hardcodeado, y cuando se activa
  **teleporta**: el raycast (`:135`) sobrescribe `desiredPos` de golpe (`:138`). No hay
  estado intermedio.
- **Sub-cambio 3.1.a - Activar por defecto:**
  - `[SerializeField] private bool _collisionEnabled = true;`
  - Default `true`: es lo que espera cualquier escena con geometria, y su ausencia es un
    agujero en el que la camara se mete dentro de las paredes. Es un default que arregla
    un problema, no uno que cambia el juego de alguien.
  - Tooltip: `"Evita que la camara atraviese paredes y geometria. Acerca la camara automaticamente cuando algo se interpone."`
- **Sub-cambio 3.1.b - Spring suave:**
  - `[SerializeField] private float _collisionPullInSpeed = 20f;`
    (`[EnableIf("_collisionEnabled")]`) - tooltip: `"Que tan rapido se acerca la camara al obstaculo. Alto = casi instantaneo."`
  - `[SerializeField] private float _collisionPushOutSpeed = 5f;`
    (`[EnableIf("_collisionEnabled")]`) - tooltip: `"Que tan rapido se aleja la camara cuando el obstaculo desaparece. Bajo = sale despacio."`
  - Nuevo estado privado: `private float _currentDistance;`
  - Reescritura del bloque `:127-141`:
    ```csharp
    // 5a. Resolver la distancia objetivo (colision)
    float targetDistance = Distance;
    if (_collisionEnabled)
    {
        Float3 rayDir = -offsetDir;
        PhysicsWorld? world = GameObject.IsValid() && GameObject.Scene.IsValid()
            ? GameObject.Scene.Physics
            : null;
        if (world != null && world.Raycast(_smoothedTargetPos, rayDir, targetDistance, out RaycastHit hitInfo))
            targetDistance = Maths.Max(hitInfo.Distance - CollisionRadius, MinDistance);
    }

    // 5b. Spring: rapido al acercarse, lento al alejarse
    float springSpeed = targetDistance < _currentDistance
        ? _collisionPullInSpeed
        : _collisionPushOutSpeed;
    float tSpring = 1f - MathF.Exp(-springSpeed * dt);
    _currentDistance = Maths.Lerp(_currentDistance, targetDistance, tSpring);

    // 5c. Reconstruir la posicion con la distancia real
    desiredPos = _smoothedTargetPos - offsetDir * _currentDistance;
    ```
  - **Inicializacion:** en la rama `!_hasTargetPos` (`:107-109`) anadir
    `_currentDistance = Distance;` - si no, el primer frame sale de 0 y la camara arranca
    pegada al personaje.
  - **Por que 20 / 5 y no uno solo:** el pull-in rapido es obligatorio - ver un frame de
    clipping dentro de una pared es peor que un tiron. El push-out lento es el que da el
    "feel": es lo que evita que la camara salga disparada al pasar por un hueco.
- **Verificacion:** empujar la camara contra una pared -> se acerca con suavidad, no
  atraviesa. Alejarse -> vuelve despacio. Sacudir el personaje contra una pared a alta
  velocidad -> no hay popping.
- **Commit:** `Feat: Add optional smoothed spring arm camera collision.`

#### Feature 3.2 - Zoom con rueda (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Campos nuevos** (despues del bloque de yaw/pitch, antes de `[Header("Follow")]`):
  - `[SerializeField, Tooltip("Zoom con la rueda del raton. Si esta desactivado, la distancia es fija.")] private bool _zoomEnabled = true;`
  - `[SerializeField, Tooltip("Distancia minima al hacer zoom in."), EnableIf("_zoomEnabled")] private float _minZoomDistance = 2f;`
  - `[SerializeField, Tooltip("Distancia maxima al hacer zoom out."), EnableIf("_zoomEnabled")] private float _maxZoomDistance = 15f;`
  - `[SerializeField, Tooltip("Velocidad del zoom. 1 = un tope de rueda recorre el rango completo."), EnableIf("_zoomEnabled")] private float _zoomSpeed = 1f;`
  - Nota: `EnableIf` va sobre los parametros; el toggle en si no lo necesita.
- **Logica** (en el bloque de input, junto a la linea 87, **fuera del bloque
  `if (shouldOrbit)`**: el zoom debe funcionar tambien con el cursor libre):
  ```csharp
  // 1b. Zoom (independiente del modo de orbitar)
  if (_zoomEnabled)
  {
      float wheel = Input.MouseWheelDelta;
      if (wheel != 0f)
          Distance = Maths.Clamp(
              Distance - wheel * _zoomSpeed * (Distance * 0.1f),
              _minZoomDistance, _maxZoomDistance);
  }
  ```
  - **Por que el escalado por `Distance`:** hace que el zoom sea relativo (un tope cerca
    mueve menos que uno lejos), que es lo que espera el jugador. Con escalado lineal, el
    zoom out se siente lentisimo.
  - **Guarda de seguridad:** si `_maxZoomDistance < _minZoomDistance`, el `Maths.Clamp`
    queda mal definido para valores fuera de rango. Normalizar los dos limites entre si
    antes, porque el Inspector deja escribir cualquier par.
  - **`Distance` se muta en runtime:** es intencional, para que el Inspector muestre el
    valor actual. Pero con esto **un proyecto guardado guarda la distancia con el zoom
    aplicado** - documentado en el tooltip de `Distance` (Feature 2.1).
- **Verificacion:** rueda arriba -> acerca, se para en 2; rueda abajo -> aleja, se para en
  15. Con `_zoomEnabled = false`, la distancia no cambia.
- **Automatizable:** si, con `FakeInputHandler.SetMouseWheel`.
- **Commit:** `Feat: Add optional mouse wheel zoom to OrbitFollowCamera.`

#### Feature 3.3 - Gamepad (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Correccion importante respecto a la auditoria:** la API real **no** es
  `Input.GetGamepadAxis(GamepadAxis.RightStickX/Y)`. No existe ningun enum `GamepadAxis`.
  Lo que hay es:
  - `Input.IsGamepadConnected(int gamepadIndex = 0)` - `Input.cs:212`
  - `Input.GetGamepadRightStick(int gamepadIndex = 0)` que devuelve `Float2` - `Input.cs:217`
  - `GetGamepadAxis` existe en el handler (`DefaultInputHandler.cs:372`) como
    `(int, int)`, pero **no** en la fachada publica `Input`.
- **Campos nuevos:**
  - `[SerializeField, Tooltip("Permite orbitar la camara con el stick derecho del mando.")] private bool _gamepadEnabled = true;`
  - `[SerializeField, Tooltip("Indice del mando. 0 es el primero."), Range(0, 15), EnableIf("_gamepadEnabled")] private int _gamepadIndex = 0;`
    - el rango 0-15 viene de los 16 slots fijos del backend GLFW
      (`DefaultInputHandler.cs:330-340`).
  - `[SerializeField, Tooltip("Sensibilidad del stick derecho."), EnableIf("_gamepadEnabled")] private float _gamepadSensitivity = 2f;`
  - `[SerializeField, Tooltip("Zona muerta del stick. Evita que la camara derive sola."), Range(0f, 0.5f), EnableIf("_gamepadEnabled")] private float _gamepadDeadzone = 0.15f;`
- **Logica** (despues del bloque de input del raton, `:87-92`):
  ```csharp
  // 1c. Gamepad: el stick no necesita un modo de "boton mantenido"
  if (_gamepadEnabled && Input.IsGamepadConnected(_gamepadIndex))
  {
      Float2 stick = Input.GetGamepadRightStick(_gamepadIndex);
      if (Maths.Abs(stick.X) > _gamepadDeadzone || Maths.Abs(stick.Y) > _gamepadDeadzone)
      {
          _yawTarget += stick.X * _gamepadSensitivity * dt;
          // Mismo signo que el raton tras el Bug 1.1: +Y del stick = arriba = pitch +
          _pitchTarget += stick.Y * _gamepadSensitivity * dt;
      }
  }
  ```
  - **Unidades:** el raton suma `MouseDelta * Sensitivity` (pixeles -> grados). El stick
    es un eje normalizado -1..1, asi que necesita `* dt` para ser una velocidad y no un
    salto. Sin el `dt` la camara se teletransporta al pulsar el stick.
  - **Deadzone:** sin ella, un stick con deriva (todos los tienen) hace que la camara
    gire sola para siempre. El valor 0.15 es el que ya usa el sample de FlyCamera
    (`Samples/FlyCamera/Program.cs:134`).
  - **Referencia de patron:** `Samples/FlyCamera/Program.cs:126-136` - pero ojo, ese
    sample usa `InputActionMap` con `DualAxisCompositeBinding`, un nivel de abstraccion
    que **no** se esta adoptando aqui. Este componente lee input de bajo nivel
    directamente, que es lo correcto para un componente de motor.
- **Verificacion:** stick derecho -> la camara orbita. Soltar -> se para (sin deriva).
  Sin mando conectado -> sin efecto ni excepcion.
- **Automatizable:** si, con `FakeInputHandler.SetGamepadAxis(0, 1, ...)` y
  `SetGamepadButton` para `IsGamepadConnected`.
- **Commit:** `Feat: Add optional gamepad orbit support to OrbitFollowCamera.`

#### Feature 3.4 - Invertir eje Y (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Campos:**
  - `[SerializeField, Tooltip("Invierte el eje vertical. Para quien juegue con control invertido.")] private bool _invertY = false;`
- **Logica** (una linea, en `:91` tras el fix del Bug 1.1):
  ```csharp
  float pitchSign = _invertY ? -1f : 1f;
  _pitchTarget += pitchSign * Input.MouseDelta.Y * Sensitivity * mult;
  ```
- **Default `false`:** el Bug 1.1 deja el comportamiento correcto; esta feature es solo
  para quien quiera lo contrario.
- **Verificacion:** con el toggle ON, raton arriba -> camara baja. OFF -> sube.
- **Commit:** `Feat: Add InvertY option to OrbitFollowCamera.`

#### Feature 3.5 - Sensibilidad normalizada por resolucion (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Renombrar respecto a la auditoria:** esto **no** es "DPI-aware". El motor no pide
  escalado por DPI al sistema operativo - GLFW no esta en modo `RawMouseMotion`
  (`DefaultInputHandler.ApplyCursorState:272-289` solo alterna
  `Disabled`/`Normal`/`Hidden`). Lo que si pasa es que **`Input.MouseDelta` esta en
  pixeles del framebuffer**, y un framebuffer 4K da el doble de deltas que uno 1080p
  para el mismo movimiento fisico del raton. Por eso a 4K la camara gira al doble de
  velocidad. El nombre correcto es **"sensibilidad normalizada por resolucion"**.
- **Campos:**
  - `[SerializeField, Tooltip("Normaliza la sensibilidad por la altura del framebuffer, para que a 4K no gire el doble que a 1080p.")] private bool _resolutionNormalizedSensitivity = true;`
  - `[SerializeField, Tooltip("Altura de referencia en pixeles. 1080 es el estandar."), EnableIf("_resolutionNormalizedSensitivity")] private float _referenceHeight = 1080f;`
- **Fuente de la resolucion:** **no existe `Screen.Height`**. Lo que hay:
  - `Window.InternalWindow.FramebufferSize.Y` - como hace `Camera.cs:284`.
  - O, si se quiere el tamano real del render target, `camTarget.Height`
    (`Camera.cs:284`). Mas correcto si el juego corre a otra resolucion, pero acopla
    esta camara a un `Camera` concreto.
- **Logica:**
  ```csharp
  float sens = Sensitivity;
  if (_resolutionNormalizedSensitivity)
  {
      int height = Window.InternalWindow.FramebufferSize.Y;
      if (height > 0)
          sens *= _referenceHeight / height;
  }
  ```
  - **Guarda `height > 0`: obligatoria.** En un test headless, o antes de que exista la
    ventana, `FramebufferSize` puede ser 0 -> division por cero -> `NaN` en
    `_pitchTarget` -> la camara se va y no vuelve. Con el `if`, cae a `Sensitivity` sin
    normalizar.
  - **Consecuencia a documentar:** al activar esto, `Sensitivity = 0.13` deja de ser
    "lo que feel bien a 1080p" y pasa a ser "lo que feel bien normalizado". El default
    `true` **cambia el feel actual en pantallas que no sean 1080p**, asi que hay que
    decidirlo con el usuario en la verificacion 4.4. Default propuesto `true` porque el
    problema (girar al doble de velocidad en 4K) es peor que el ajuste.
- **Verificacion:** misma distancia fisica de raton a 1920x1080 y a 3840x2160 -> el mismo
  angulo. Con el toggle OFF -> el angulo se dobla en 4K.
- **Automatizable:** parcialmente; el framebuffer es del `Window`, no del handler, asi
  que en un test hay que exponer la altura como campo o aceptar que solo se prueba la
  rama `height > 0`. **No bloquear la feature por esto.**
- **Commit:** `Feat: Add resolution-normalized sensitivity option to OrbitFollowCamera.`

#### Feature 3.6 - Snap por teleport (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Problema que resuelve:** si el target salta 20 metros (teleport, respawn, cambio de
  escena), la camara tiene que recorrerlos con el suavizado - un barrido visible a traves
  de las paredes.
- **Campos:**
  - `[SerializeField, Tooltip("Salta al pivote en vez de volar hasta el cuando el target se teletransporta.")] private bool _snapOnTeleport = true;`
  - `[SerializeField, Tooltip("Distancia en un solo frame a partir de la cual se considera teleport."), EnableIf("_snapOnTeleport")] private float _teleportThreshold = 5f;`
- **Logica** (justo antes del suavizado del pivote, `:106-119`). Necesita un campo
  privado extra, `private Float3 _lastRawPivot;`, para no comparar contra el pivote ya
  suavizado:
  ```csharp
  if (_hasTargetPos)
  {
      if (_snapOnTeleport &&
          Float3.Distance(targetPivot, _lastRawPivot) > _teleportThreshold)
      {
          // Teletransporte: saltar, no barrer.
          _smoothedTargetPos = targetPivot;
          _currentDistance = Distance;
      }
      else
      {
          float t = 1f - MathF.Exp(-FollowSmoothing * dt);
          _smoothedTargetPos = new Float3(
              Maths.Lerp(_smoothedTargetPos.X, targetPivot.X, t),
              Maths.Lerp(_smoothedTargetPos.Y, targetPivot.Y, t),
              Maths.Lerp(_smoothedTargetPos.Z, targetPivot.Z, t));
      }
  }
  _lastRawPivot = targetPivot;
  ```
- **Por que comparar contra `_lastRawPivot` y no contra `_smoothedTargetPos`:** el pivote
  suavizado va siempre atrasado; durante una carrera normal la distancia entre el raw y el
  suavizado se acerca a un valor estable proporcional a la velocidad. Si se comparara
  contra el suavizado, un sprint a `RunSpeed` podria disparar el snap sin que hubiera
  habido teleport. Contra el raw, la distancia solo crece si el target **de verdad** se
  movio.
- **Por que resetear tambien `_currentDistance`:** si ademas del pivote habia un
  obstaculo pegado a la camara en el punto viejo, la distancia colapsada se arrastraria al
  nuevo sitio.
- **Verificacion:** teleportear el target lejos -> la camara aparece ya en su sitio, sin
  barrido. Correr normal -> el snap no se dispara nunca.
- **Automatizable:** si.
- **Commit:** `Feat: Add snap-on-teleport option to OrbitFollowCamera.`

**Esfuerzo Fase 3: ~4h 30 min** (6 features, ~45 min cada una: escribir, probar el toggle
OFF antes del commit, probar el toggle ON, y el doble de verificacion manual).

---

### Fase 4 - Verificacion

#### Verificacion 4.1 - Bugs

- [ ] **Bug 1.1** - test: `MouseDelta = (0, -10)` (raton arriba) con el cursor bloqueado ->
      el pitch sube. Manual: la camara sube y para en 18 grados.
- [ ] **Bug 1.2** - test: rotacion con `TurnSpeed = 12`, `dt` de 1/30 vs 1/144, el mismo
      numero de frames -> el angulo final difiere < 1e-3. Manual: limitador de FPS a 30
      y a 144 -> el personaje gira igual.
- [ ] **Bug 1.3** - `Select-String` sobre el archivo: sin comentarios de inversion.
- [ ] **Bug 1.4** - test: mover el target, `_smoothedTargetPos` se retrasa -> el
      `LookRotation` resultante apunta al pivote suavizado, no a la posicion cruda.
- [ ] **Bug 1.5** - test: `TurnSpeed = -5` -> `t` en `[0,1]`, sin excepcion.

#### Verificacion 4.2 - Tests nuevos a escribir (cobertura hoy: 0)

Todos contra `FakeInputHandler` (`InputTestHelpers/FakeInputHandler.cs`) +
`scene.Update()` (que bombea `LateUpdate`, `Scene.cs:936-952`):

- [ ] `OrbitFollowCameraTests.PitchDelta_MouseUp_IncreasesPitch`
- [ ] `OrbitFollowCameraTests.Pitch_ClampedToMinMax`
- [ ] `OrbitFollowCameraTests.LookAt_UsesSmoothedPivotNotRawTarget`
- [ ] `OrbitFollowCameraTests.WheelZoom_ChangesDistance_AndClampsToRange`
- [ ] `OrbitFollowCameraTests.WheelZoom_DoesNothing_WhenDisabled`
- [ ] `OrbitFollowCameraTests.GamepadRightStick_Orbits_WhenConnected`
- [ ] `OrbitFollowCameraTests.GamepadIgnored_WhenNotConnected_NoException`
- [ ] `OrbitFollowCameraTests.InvertY_FlipsPitchSign`
- [ ] `OrbitFollowCameraTests.SnapOnTeleport_JumpsInsteadOfSweeping`
- [ ] `OrbitFollowCameraTests.NormalFollow_DoesNotSnap_AtRunSpeed`
- [ ] `OrbitFollowCameraTests.CollisionSpring_ApproachesGradually_NotTeleport`
- [ ] `ThirdPersonCharacterMovementTests.TurnRate_FrameRateIndependent`
- [ ] `ThirdPersonCharacterMovementTests.SlerpT_Clamped_WhenTurnSpeedNegative`

- [ ] Confirmar que `_resolutionNormalizedSensitivity` con altura 0 **no** produce `NaN`
      (o documentar que queda fuera de cobertura automatizada).

#### Verificacion 4.3 - Features, una por una, con el toggle OFF primero

El orden importa: **cada feature se prueba OFF (no debe cambiar nada respecto al video de
la Fase 0), luego ON.**

- [ ] Colision OFF -> camara identica a la linea base
- [ ] Colision ON -> contra una pared: se acerca suave, no atraviesa; al salir, se aleja
      despacio
- [ ] Zoom OFF -> la distancia no cambia con la rueda
- [ ] Zoom ON -> rueda arriba acerca (tope 2), rueda abajo aleja (tope 15)
- [ ] Gamepad OFF -> el stick no hace nada
- [ ] Gamepad ON -> el stick orbita; sin deriva al soltar; sin mando, sin crashear
- [ ] InvertY ON -> el pitch se invierte
- [ ] Normalizacion OFF -> la sensibilidad es la de siempre
- [ ] Normalizacion ON -> 1080p y 4K giran igual
- [ ] Snap OFF -> un teleport se ve como barrido (comportamiento actual)
- [ ] Snap ON -> el teleport salta

#### Verificacion 4.4 - Feel (subjetivo: lo decide el usuario)

Lo que no se puede testear, porque no hay assert posible para "esto se siente bien":

- [ ] Se siente mejor que la linea base? -> **Si / No**
- [ ] Que feature marco la diferencia? -> __________
- [ ] Que feature sigue sintiendose mal? -> __________
- [ ] El default de `Sensitivity = 0.13` sigue siendo el correcto con la normalizacion
      por resolucion activada? -> **Si / No** (decision del usuario, no del codigo)
- [ ] `_collisionPullInSpeed = 20` / `_collisionPushOutSpeed = 5`: el tiron al pegarse a
      una pared se nota demasiado? -> __________

**Esfuerzo Fase 4: ~2h** (tests nuevos ~1h, verificacion manual ~1h).

---

### Fase 5 - Cierre

- [ ] `dotnet build` sin warnings nuevos
- [ ] `dotnet test` verde y **el recuento no ha bajado** respecto a la linea base de la
      Fase 0 (los 13 tests nuevos suben el total; si baja, algo se rompio)
- [ ] Grabar el video "despues" y ponerlo lado a lado con el de la Fase 0
- [ ] Actualizar este documento: marcar las casillas, anotar los defaults finales que
      haya elegido el usuario
- [ ] Mergear a `main` y tag - **el tag lo confirma el usuario**
- [ ] Los defaults que el usuario haya cambiado durante el tuning quedan como nuevos
      defaults en el codigo, con un comentario de una linea explicando por que

**Esfuerzo Fase 5: ~40 min.**

---

## Estimacion

| Fase | Contenido | Esfuerzo |
|---|---|---|
| 0 | Preparacion (rama, video, linea base de tests) | 20 min |
| 1 | 5 bugs P0 | 45 min |
| 2 | Tooltips, rangos y desambiguacion de nombres | 1h 15 min |
| 3 | 6 features opcionales (con toggle) | 4h 30 min |
| 4 | Tests nuevos + verificacion manual | 2h |
| 5 | Build, tests, video, merge | 40 min |
| | **Total** | **~9h 30 min** |

El trabajo de codigo son ~2h. Las 7h 30 min restantes son verificacion, que es donde se
decide si esto ha servido de algo.

---

## Riesgos

| # | Riesgo | Mitigacion |
|---|---|---|
| R1 | **Renombrar campos rompe datos serializados.** La serializacion es por nombre de campo y no hay `FormerlySerializedAs`. Un `public Sensitivity` pasa a `_sensitivity` y deja todas las escenas y prefabs con el default. | No renombrar nada existente. Solo `[Tooltip]` / `[Range]`, que no afectan al nombre. Verificado en Fase 2. |
| R2 | **Fase 1.1 se aplica a medias**: se cambia el signo del pitch pero no el rango de clamp, y el resultado es un rango de pitch invertido (36 arriba / 18 abajo). | Los dos cambios van en el mismo commit, con un test que asserta los dos extremos. |
| R3 | **Defaults nuevos rompen proyectos existentes.** Poner `_collisionEnabled = true` cambia el comportamiento de cualquier escena que tuviera la camara atravesando paredes. | Es el default correcto (arregla un agujero), pero hay que tener el video de la linea base para poder mostrar la diferencia. |
| R4 | **Division por cero en la normalizacion** si el framebuffer mide 0 (headless, tests, antes de crear la ventana) -> `NaN` en `_pitchTarget` y la camara se va para siempre. | Guarda `if (height > 0)` obligatoria. Test explicito en 4.2. |
| R5 | **`EnableIf` necesita el nombre exacto del campo** (`"_xxxEnabled"`). Un typo no da error de compilacion: simplemente no greyea nada. | Revisar visualmente el Inspector con cada toggle en OFF despues de anadirlo, no confiar en el build. |
| R6 | **El usuario prefiere defaults distintos** a los propuestos (sobre todo `Sensitivity` tras la normalizacion, y el 20 / 5 del spring). | Cada default se escribe en el `Tooltip`, asi que se cambia desde el Inspector sin tocar codigo. Se anotan los finales en la Fase 5. |
| R7 | **El feel no se puede automatizar.** Es posible pasar todos los tests y que el componente siga sintiendose mal. | La verificacion 4.4 es obligatoria y la decide el usuario, no los asserts. El video antes/despues es la evidencia. |
| R8 | **El lookAt suavizado introduce retardo** que antes no habia (el raw era instantaneo). Puede notarse al arrancar a caminar. | Es el trade-off correcto (Bug 1.4), pero hay que probarlo en movimiento real, no parado. Si molesta, la salida es un smoothing aparte para el lookAt, no volver al raw. |
| R9 | **Feature 3.2 muta `Distance`**, asi que un proyecto guardado guarda la distancia con el zoom aplicado. | Documentado en el tooltip de `Distance`. Alternativa si molesta: campo `_zoomOffset` separado. Decidir en la revision de la Fase 4. |
| R10 | **`Float3.Distance` cada frame** solo para el snap (Feature 3.6). Coste despreciable, pero es codigo nuevo en el hot path. | Comparar por componentes si el perfil dice lo contrario. No hacerlo de entrada. |

---

## Criterios de "terminado"

- [ ] Los 5 bugs arreglados, cada uno con su commit.
- [ ] El Bug 1.1 incluye el cambio de rango de pitch, no solo el signo.
- [ ] Todas las features nuevas son **opcionales**: cada una con su
      `[SerializeField] private bool _xxxEnabled` y sus parametros con
      `[EnableIf("_xxxEnabled")]`.
- [ ] Con todos los toggles en OFF, el componente se comporta **exactamente** como la
      linea base grabada en la Fase 0 (salvo el Bug 1.1, que es un fix).
- [ ] Todos los campos tienen tooltip, y los tooltips dicen la verdad (si no se puede
      escribir un tooltip cierto sobre un campo, es senal de que el campo necesita un
      nombre mejor).
- [ ] Ningun campo existente se ha renombrado.
- [ ] Cobertura de tests: de 0 a 13 tests nuevos en verde, sobre el recuento de la Fase 0.
- [ ] `dotnet build` limpio; `dotnet test` sin regresiones.
- [ ] El feel es mejor, segun el usuario (verificacion 4.4 respondida, no asumida).
- [ ] Video antes/despues grabado.
- [ ] La rama se puede mergear sin conflictos.