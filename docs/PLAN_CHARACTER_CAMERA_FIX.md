# PLAN - Fix de ThirdPersonCharacterMovement y OrbitFollowCamera

## Contexto

Este plan estaba antes centrado en la camara. **El diagnostico cambio:** el componente que
se siente horrible de usar es `ThirdPersonCharacterMovement`, no `OrbitFollowCamera`.

Como se sabe: un juego 2D de plataformas, hecho con el resto del motor, se siente
perfecto. La diferencia es que **el 2D no usa `ThirdPersonCharacterMovement`**. Ese
componente esta a medio implementar y arrastra el feel de todo el sistema porque la
camara le pasa la informacion de "hacia donde me muevo" de forma primitiva.

La prueba concreta esta en `ThirdPersonCharacterMovement.cs:61`:

```csharp
Float3 camToTarget = Camera.Target.Position - Camera.Transform.Position;
camToTarget.Y = 0;
Float3 flatF = Float3.Normalize(camToTarget);
```

El personaje no lee "hacia donde mira la camara". Lee **un vector entre dos posiciones**.
Eso no es lo mismo, y por eso falla:

1. **Mezcla dos verdades distintas.** La camara se coloca desde `_smoothedTargetPos`
   (`OrbitFollowCamera.cs:113-118`), el pivote **suavizado**, que va atrasado. La linea 61
   usa `Camera.Target.Position`, el target **crudo y del frame actual**. La diferencia
   entre ambos es un vector de retraso que apunta hacia donde te estas moviendo. O sea:
   **"adelante" no es un eje, es una direccion que se recalcula y se tuerce hacia donde
   corres.**
2. **El error escala con lo contrario de lo que se quiere.** Ese vector de retraso pesa
   `WalkSpeed / FollowSmoothing` ~= `5/10` = 0.5 unidades, fijo. El termino de la camara
   pesa `Distance` = 6. A distancia normal son ~3 grados de deriva. Pero con
   `CollisionEnabled` la camara puede bajar a `MinDistance = 1` (`OrbitFollowCamera.cs:137`):
   el mismo 0.5 ahora pesa 5 veces mas -> **~15 grados de error justo cuando estas contra
   una pared.** La base de movimiento se degrada exactamente cuando mas la necesitas.
3. **Un frame de atraso.** El personaje lee en `Update()` (linea 46); la camara escribe en
   `LateUpdate()` (linea 58).
4. **Acoplamiento duro, y no null-safe.** Exige el componente `OrbitFollowCamera` y mete la
   mano en `Camera.Target`, un campo de otro componente. La guarda de la linea 48 chequea
   `Camera` y `Controller`, pero **no `Camera.Target`**: si el Target esta sin asignar,
   `NullReferenceException`. Y no hay forma de usar este componente con otra camara.

**La camara esta bastante bien.** El pivote ya se suaviza, la rotacion ya usa suavizado
exponencial frame-rate independent (`:96-99`), la colision ya existe (`:127-141`), el
cursor ya se restaura en `OnDisable` (`:51-56`), el re-lock con click izquierdo ya esta
(`:67-73`). De los 7 problemas de feel de la auditoria original, **2 ya estan resueltos**
(cursor lock ausente, robo de cursor en `OnEnable`): no se tocan. De la camara solo queda
un P0 real: **el pitch invertido**.

Ademas, `ThirdPersonCharacterMovement` tiene features **declaradas y nunca usadas**:

| Campo | Linea | Estado real |
|---|---|---|
| `JumpForce` | 42 | **No se puede saltar.** No hay un solo `GetKeyDown` en el archivo. |
| `RunSpeed` | 28 | **Shift no hace nada.** Aparece en el Inspector como si funcionara. |

Los dos son P0: un componente de personaje sin salto no se puede usar, y un campo que no
hace nada es peor que no tenerlo, porque engaña en el Inspector.

**Lo que este plan NO hace:** reescribir la arquitectura, tocar el input del motor, ni
imponer nada. Todo lo nuevo es opcional o es un bug.

---

## Filosofia

- **Bugs: obligatorios.** Se arreglan siempre, no son opcionales.
- **Prioridad: jugabilidad antes que feel.** Entre dos P0, va primero el que **restaura la
  jugabilidad basica** y despues el que solo mejora el feel. Si un fix hace el control
  inutilizable ("no puedo apuntar"), bloquea la evaluacion de todos los demas: nadie puede
  juzgar si el siguiente mejora el sentido con el control roto debajo. Por eso el Bug 1.0
  (pitch invertido) va primero aunque sea de la camara, en una fase titulada "personaje".
- **Features: opcionales.** Cada feature nueva arranca con un toggle
  `[SerializeField] private bool _xxxEnabled`, y sus parametros se ocultan detras de
  `[EnableIf("_xxxEnabled")]` para que el Inspector no se llene de campos muertos.
- **Defaults: sensatos.** Ningun default rompe el comportamiento actual.
- **Tuning: expuesto, no hardcodeado.** Todo numero que se pueda tocar va a un campo.
- **El usuario manda.** El "se siente bien" no lo decide el codigo, lo decide el usuario.
- **Nombres de campo existentes: intocables.** La serializacion de Zenith es **por nombre
  de campo** (`RuntimeUtils.cs:341-345`) y **no hay `FormerlySerializedAs`**. Renombrar
  `JumpForce` a `_jumpForce` dejaria todas las escenas y prefabs con el default. Solo se
  anade logica y tooltips; no se renombra nada.

---

## Objetivos

- [ ] Que el personaje reciba una base de movimiento limpia de la camara (handshake)
- [ ] Que `JumpForce` y `RunSpeed` hagan algo
- [ ] Arreglar el moonwalk lateral (A/D no rota el modelo)
- [ ] Arreglar el salto de 45 grados al correr en diagonal
- [ ] Arreglar el pitch invertido de la camara (sigue siendo P0)
- [ ] Cubrir con tests automatizados lo que es determinista
- [ ] Dejar escrito el criterio manual para lo que no se puede testear (feel)

---

## No-objetivos (fuera de alcance)

- No se reescribe la arquitectura ni se parte el `LateUpdate` / `Update` en metodos mas
  pequenos (salvo lo que exija un bug).
- No se cambia el nombre de ningun componente ni de ningun campo existente.
- No se anaden las features que la auditoria original listo pero que aqui no se concretan
  (look-ahead, FOV kick, shoulder offset, crouch, sprint stamina): requieren decisiones de
  diseno que el usuario no ha tomado.
- No se toca `CharacterController` mas alla de **leerlo**. Es un componente solido de 860
  lineas con colision, slide, step-up y snap. Este plan solo lo consume.
- No se migra el input del personaje a `InputAction`. Es una feature mayor y va aparte.
  Lo unico que se hace es anadir `GetKeyDown` para el salto, que ya es la API del motor.
- No se implementa FOV kick ni smooth de camara adicional.

---

## Estado actual

**Veredicto: el personaje esta a medio implementar; la camara esta tuning. NO reescritura.**

El `CharacterController` es de fiar: `Move()` hace colision, slide, step-up y snap a
suelo, y expone `IsGrounded`, `Collisions`, `GroundNormal`, `GroundSlopeAngle`, `Velocity`,
`Cast`. **No tiene ningun metodo de salto** - hay que implementarlo en el personaje, que
ya tiene la plumbing de velocidad vertical (`ThirdPersonCharacterMovement.cs:74-85`,
`106-110`).

Detalle critico para el salto, verificado en `CharacterController.cs:245`:

```csharp
if (wasGrounded && motion.Y <= 0)
    finalPosition = SnapToGround(finalPosition);
```

`SnapToGround` solo corre cuando el movimiento vertical es **hacia abajo o cero**. En el
frame del salto `motion.Y > 0`, asi que el snap se salta y el salto no se cancela. **El
salto funciona sin tocar `CharacterController`.**

Otro detalle: `CharacterController.Move` actualiza `IsGrounded` al final (`:259`). Por eso
el salto debe comprobar el grounded de **principio de frame**, que es lo que ya hace
`wasGrounded` en la linea 72. Con eso no hay doble salto ni bunny-hop.

**Cobertura de tests: 23 tests nuevos, todos en verde** (`OrbitFollowCameraTests`,
`ThirdPersonCharacterMovementTests`). La suite Runtime paso de 1274 a 1297 y la de Editor
sigue en 722, sin regresiones.

---

## BLOQUEER FOUND DURANTE LA EJECUCION - CharacterController no se puede despegar del suelo

**Este hallazgo invalida el supuesto del Riesgo R2/R4 y deja el Bug 1.2 a medias.**

Al implementar el salto se verifico que **`CharacterController.Move` no puede mover al
personaje hacia arriba cuando esta tocando exactamente una superficie**:

```
Move(+Y) desde el suelo      -> Y no cambia, flags = Above, achieved = (0,0,0)
Move(+Y) con 0.05 de hueco   -> Y 0.95 -> 1.078   (funciona)
Move(-Y) con 0.05 de hueco   -> Y 0.95 -> 0.92    (funciona)
Move(+X) con 0.05 de hueco   -> se mueve           (funciona)
```

El cast de forma hacia arriba reporta un hit a distancia 0 contra el suelo en el que esta
apoyado, con una normal que `Record` clasifica como `Above`. `CollideAndSlide` proyecta el
movimiento sobre esa superficie y el resultado es movimiento cero.

La gravedad funciona **por la misma causa**: `SnapToGround` (`CharacterController.cs:245`)
vuelve a pegar al personaje al suelo cada frame, asi que siempre esta en contacto exacto.

**Por que no se ha arreglado aqui:** el plan declara explicitamente que
`CharacterController` es fuera de alcance ("solo leerlo"). Son 860 lineas de fisica con
slide, step-up y depenetration, y arreglarlo necesita su propia auditoria. Ademas el arreglo
probablemente este en `PhysicsWorld.ShapeCast` o en como `CollideAndSlide` distingue un
contacto inicial de un golpe real, no en el componente.

**Efecto secundario observado:** como el personaje nunca se despega, `_verticalVelocity`
positiva nunca se amortigua - el clamp de `ThirdPersonCharacterMovement` solo pisa
velocidades **negativas**, y la rama de gravedad solo corre cuando no hay suelo. La
velocidad se queda en 7.67 para siempre. En un setup que funcione esto no se ve, pero es
fragilidad: el clamp de grounded deberia apply tambien a positivas, o la gravedad
deberia aplicarse siempre y el "pegado" modelarse aparte.

**Que hay que decidir:**

1. Auditar y arreglar `CharacterController` / `PhysicsWorld.ShapeCast` (recomendado: es un
   P0 de la misma clase que los que ya se arreglaron, porque sin el el personaje no salta).
2. O meter un parche acotado: en `CollideAndSlide`, ignorar un hit cuya normal apunte en
   contra del sentido del movimiento **y** cuya distancia sea 0 (es decir, treated como
   "ya tocando" y no como "bloqueado"). Es un cambio pequeno pero en fisica, asi que
   necesita tests propios.

**Test que documenta el bloqueo:**
`ThirdPersonCharacterMovementTests.Jump_CharacterLeavesTheGround_BlockedByCharacterControllerCannotSeparate`
- cuando se arregle, hay que **invertir** sus aserciones.

---

## ALCANCE REAL DEL BLOQUER: el personaje no puede ANDAR

El titulo de arriba se queda corto. Midiendo el caso general:

| hueco entre la capsula y el suelo | distancia recorrida en 40 frames |
|---|---|
| **0.000 (tocando)** | **0.0000** |
| 0.020 | 3.09 |
| 0.050 | 3.09 |

**El personaje no se mueve en NINGUNA direccion mientras toca el suelo exactamente.** La
velocidad se calcula bien (llega a 5.0) pero la posicion no cambia. Y `SnapToGround` lo
devuelve a tocar exactamente cada frame, asi que se queda pegado de forma permanente.

Corregido por el usuario: **es especifico de `MeshCollider`**. Con `BoxCollider` se mueve bien
en el juego (aunque en un rig de test reproducible tambien se atasca). La causa probable son
**internal edge artifacts**: la capsula contacta una arista interna de la triangulacion, la
normal sale lateral en vez de vertical, y el slide contra esa normal anula el movimiento
horizontal.

Esto explica TODOS los sintomas de feel que se reportaron:
- "me quedo atascado al piso" -> literalmente esto
- "camina sin ganas" -> movimiento cero
- "se bloquea" -> el cast no avanza
- StrafeMode "se siente bien" -> **tapa** este bug y el de la desincronizacion, porque al no
  moverse apenas se nota el problema de rotacion

### Intento de arreglo (REVERTIDO - no ha pasado la suite)

Se escribio un parche en `CollideAndSlide` (`CharacterController.cs:627`) con la regla:
un contacto a distancia `<= SkinWidth` es de reposo; si `dot(normal, direccion) > 0` la
superficie esta al lado o detras y se avanza el resto de la distancia completo, si no se
proyecta sobre el plano y se desliza. Es decir: **la distancia es la senal, no la normal**,
porque en un mallado la normal de una arista interna puede apuntar de frente al movimiento y
fingir ser una pared.

Resultados con el parche:

| test | sin parche | con parche |
|---|---|---|
| Quad de 2 triangulos | 0.0000 | **pasa** |
| Grid de triangulos | 0.0000 | **pasa** |
| Muro de malla sigue bloqueando | pasa | **pasa** |
| Rampa empinada no lanza | pasa | **pasa** |
| Rampa suave sube | caia al vacio (-25) | no sube |
| Suelo Box | 0.0000 | 0.5171 (de ~3 esperados) |
| Step-up a box bajo | no sube | no sube |
| Las 4 direcciones | 0.0000 | 0.0284 |

**Se revirtio porque hacia colgar la suite completa** (los 1299 tests pasaban en 25s; con el
parche la suite no terminaba en 15 minutos). Probablemente un bucle entre la rama nueva,
`TryStepUp` y la recursión, o un test de NavMesh donde el personaje ahora desliza sin parar.
Sin investigar no se deja un cambio de fisica a medias.

### Lo que falta para cerrarlo

1. **Aislar el cuelgue.** Correr la suite con el parche y timeout por test para identificar
   cual se queda colgado. Es lo primero: un fix de fisica que cuelga la suite no es
   entregable.
2. **El `Depenetrate` de la rama de reposo** puede estar empujando al personaje hacia atras:
   explica que el suelo Box solo avance el 17% de lo esperado.
3. **`TryStepUp` tiene un rango de cast hacia abajo insuficiente**: desde `position + StepSize`
   solo barre `StepSize + SkinWidth + 0.1`, y con el personaje a 0.9 sobre el suelo no alcanza
   a ver el borde de un escalon de 0.15. Es un bug preexistente, independiente.
4. **Internal edge filtering** (opcion 3 del enunciado) sigue siendo lavia limpia para el
   artefacto de malla, y requiere acceso a la geometria que `CharacterController` no tiene.
   Alternativa sin tocar geometria: **reintentar el cast desde `position + Up * epsilon`**
   cuando el cast principal se bloquea a distancia cero, y usar el resultado elevado si llega
   mas lejos. Es la tecnica clasica de mover el origen del cast.

---

## Fases

### Fase 0 - Preparacion (~20 min)

- [ ] Grabar la linea base: `git rev-parse --short HEAD` y `git status --short` vacio.
- [ ] Grabar video de 30s del comportamiento actual: caminar, correr en diagonal, puro
      A/D,Shift, Space, y orbitar la camara. Sin esto no hay forma de argumentar "se
      siente mejor".
- [ ] Anotar la linea base de tests: `dotnet test` y guardar el recuento exacto
      (referencia del repo: 1274 Runtime / 722 Editor - **confirmar, no asumir**).
- [ ] Crear rama `tweak/character-feel` desde `main`.

**Esfuerzo:** 20 min.

---

### Fase 1 - P0: jugabilidad basica primero, feel despues

**Regla de orden de esta fase:** los fixes que **restauran jugabilidad** van antes que los
que **mejoran el feel**. El pitch invertido es "no puedo jugar"; el handshake es "juego,
pero se siente raro". Sin el pitch arreglado el usuario no puede ni apuntar, asi que no
puede jugar el personaje para evaluar si el handshake mejora nada. Por eso 1.0 va primero.

| # | Fix | De quien es | Esfuerzo |
|---|---|---|---|
| 1.0 | Pitch invertido + rango | camara | 5 min |
| 1.1 | Handshake: la camara publica la base de movimiento | ambos | 30 min |
| 1.2 | `JumpForce` implementado | personaje | 1h |
| 1.3 | `RunSpeed` implementado | personaje | 30 min |
| 1.4 | Rotar con A/D puro | personaje | 30 min |
| 1.5 | Damping del giro en diagonal | personaje | 1h |
| 1.6 | `MovementThreshold` documentado | personaje | 5 min |
| 1.7 | `accelRate` binario: frena con la tasa equivocada | personaje | 15 min |
| 1.7 | `accelRate` binario: frena con la tasa equivocada | personaje | 15 min |

#### Bug 1.0 - Pitch invertido + su rango invertido

*El unico fix de camara de toda la Fase 1. Va primero por la regla de jugabilidad.*

- **Archivo:** `OrbitFollowCamera.cs:91` y `:25-26`
- **Por que es lo primero:** con el pitch invertido, mover el raton arriba hace mirar la
  camara hacia el suelo. Es un mapeo de control roto, no una cuestion de gusto: el jugador
  no puede apuntar. Todo lo demas de esta fase es tuning, y el tuning no se puede evaluar
  con un control roto debajo.
- **Diagnostico:** el signo de la entrada *no* es el bug aislado; el bug es la
  **combinacion** entrada + convencion de `FromEuler`. En este motor el pitch positivo es
  mirar **abajo** (la camara del editor lo calcula asi, `EditorCamera.cs:638`:
  `-MathF.Sin(pitchRad)` en la Y del forward). La linea 91 resta el delta Y, asi que mover
  el raton **arriba** (delta negativo) **aumenta** el pitch -> la camara mira **abajo**.
- **Cambio (dos lineas, inseparable):**
  1. `OrbitFollowCamera.cs:91` - de
     `_pitchTarget -= Input.MouseDelta.Y * Sensitivity * mult;`
     a `_pitchTarget += Input.MouseDelta.Y * Sensitivity * mult;`
  2. `OrbitFollowCamera.cs:25-26` - invertir tambien el clamp y sus defaults, si no el
     rango queda al reves:
     - `MinPitch = -18f` -> `MinPitch = -36f`
     - `MaxPitch = 36f` -> `MaxPitch = 18f`
- **Por que el punto 2 es obligatorio:** hoy el rango es "18 arriba / 36 abajo"
  (asimetrico, y la asimetria confirma que los defaults se escribieron bajo la convencion
  "positivo = abajo"). Al invertir el signo, ese mismo rango pasaria a ser "36 arriba /
  18 abajo": un cambio de feel no pedido, colado dentro de un fix de bug.
- **Verificacion:** raton arriba -> la camara **sube** y se para en 18 grados sobre el
  horizonte; raton abajo -> baja hasta 36. Antes: lo contrario en ambos extremos. Y con el
  Bug 1.1 ya aplicado, W sigue moviendo en la direccion de la pantalla.
- **Esfuerzo:** 5 min. **Es el fix mas barato del plan y el que mas bloquea.**
- **Commit:** `Fix: Invert pitch sign and pitch range in OrbitFollowCamera.`

#### Bug 1.1 - Handshake: la camara publica la base de movimiento

- **Archivos:** `OrbitFollowCamera.cs` (nuevo), `ThirdPersonCharacterMovement.cs:58-68`
- **Estado actual:** el personaje deriva su base de las posiciones de camara y target
  (linea 61). Los 4 problemas del "Contexto" salen de ahi.

**Cambio en `OrbitFollowCamera`** - dos propiedades publicas, calculadas del yaw:

```csharp
/// <summary>
/// Forward horizontal de la camara, en coordenadas de mundo. Es la direccion en la
/// que se mueve el jugador al pulsar W. Se deriva del yaw, no de la posicion, para
/// que no se contamine con la suavizacion del pivote ni con la colision.
/// </summary>
public Float3 FlatForward { get; private set; }

/// <summary>
/// Right horizontal de la camara, en coordenadas de mundo. Direccion de D.
/// </summary>
public Float3 FlatRight { get; private set; }
```

Se rellenan en `LateUpdate`, justo despues del suavizado de rotacion (despues de la
linea 99), antes de calcular el pivote:

```csharp
// La base de movimiento del personaje se deriva del yaw, no de la posicion: asi no
// se arrastra el retardo del pivote suavizado ni cambia cuando la colision acerca
// la camara. Se publica antes de usar el pivote para que no dependa de el.
Quaternion flatRot = Quaternion.FromEuler(0f, _yaw, 0f);
Float3 fwd = flatRot * Float3.UnitZ;
fwd.Y = 0f;
if (Float3.LengthSquared(fwd) < 1e-6f)
{
    // Degenerado (no deberia pasar: el yaw puro siempre da un forward aplanado de
    // longitud ~1). Publicar una base neutra, no un NaN que se propaga a la
    // velocidad del personaje.
    FlatForward = Float3.UnitZ;
    FlatRight = Float3.UnitX;
}
else
{
    FlatForward = Float3.Normalize(fwd);
    FlatRight = Float3.Normalize(Float3.Cross(Float3.UnitY, FlatForward));
}
```

- **Por que del `_yaw` (suavizado) y no de `_yawTarget`:** `_yawTarget` es el objetivo
  instantaneo del input; `_yaw` es lo que la camara dibuja de verdad. Usar `_yawTarget`
 adelantaria la base de movimiento una suavizacion respecto a lo que se ve.
- **Por que de la rotacion y no de `Transform.Rotation`:** `Transform.Rotation` se
  sobrescribe al final del frame con `LookRotation` hacia el lookAt
  (`OrbitFollowCamera.cs:149`), que arrastra el pitch y el acoplamiento con el pivote. La
  base de movimiento debe salir del yaw y solo del yaw.
- **Por que el `Cross(UnitY, forward)` y no al reves:** es la misma convencion que ya usa
  el personaje en la linea 66, para no invertir la izquierda/derecha al migrar.

**Cambio en `ThirdPersonCharacterMovement`** - reemplazar las lineas 58-68:

```csharp
// Base de movimiento publicada por la camara. Antes se derivaba de
// Camera.Target.Position - Camera.Transform.Position, lo que mezclaba el pivote
// suavizado con el target crudo y se degradaba cuando la colision acercaba la camara.
Float3 flatF = Camera.FlatForward;
Float3 flatR = Camera.FlatRight;

// Base degenerada: no mover. Un NaN aqui se propaga a la velocidad y el personaje
// desaparece para siempre, y un forward de longitud ~0 no significa nada.
if (Float3.LengthSquared(flatF) < 1e-6f || Float3.LengthSquared(flatR) < 1e-6f)
    return;

Float3 moveDir = flatF * inputY + flatR * inputX;
float mag = Float3.Length(moveDir);
```

- **Que elimina:** la mezcla de verdades, el error escalado por colision, el acoplamiento
  duro y la excepcion por `Camera.Target` nulo. El retardo de un frame **no desaparece**
  (el personaje sigue leyendo en `Update` lo que la camara publico en `LateUpdate`), pero
  **deja de importar**: ahora el valor es un yaw suavizado e independiente del target, asi que
  que un frame de atraso es indetectable. Documentarlo en el tooltip de `Camera`.
- **Verificacion:** con la camara a distancia 6 y luego pegada a una pared
  (`CollisionEnabled` ON), W debe mover en **la misma direccion** en ambos casos. Antes:
  ~3 grados de diferencia y ~15 respectivamente. Ademas: girar la camara mientras se
  camina no debe hacer que el personaje derive de lado.
- **Automatizable:** si, con `FakeInputHandler.SetMouseDelta` + yaw fijo.
- **Esfuerzo:** 30 min.
- **Commit:** `Fix: Publish movement basis from camera; use it in character movement.`

#### Bug 1.2 - `JumpForce` implementado (hoy el personaje no salta)

- **Archivo:** `ThirdPersonCharacterMovement.cs` (logica nueva) + `:41-42` (tooltip)
- **Estado actual:** `public float JumpForce = 8f;` (linea 42) y **cero uso**. No hay
  ningun `GetKeyDown` en el archivo.
- **Verificacion previa (hecha):** `CharacterController` **no expone ningun metodo de
  salto**. Hay que aplicarlo desde aqui, sobre la velocidad vertical que ya existe.
- **Cambio** - insertar entre la linea 72 (`wasGrounded`) y la linea 74:

```csharp
// Salto. Se comprueba el grounded de PRINCIPIO de frame (linea de arriba), no el
// actual: CharacterController.Move actualiza IsGrounded al final, asi que leerlo aqui
// permitiria un segundo salto en el mismo frame.
if (wasGrounded && Input.GetKeyDown(KeyCode.Space))
{
    _verticalVelocity = JumpForce;
    wasGrounded = false; // este frame no estamos pegados al suelo
}
```

- **Por que esto basta:** la rama de grounded (`:74-79`) solo toca la velocidad si es
  negativa (`if (_verticalVelocity < 0f) _verticalVelocity = -0.5f;`), asi que un valor
  positivo no lo pisa. Y `SnapToGround` de `CharacterController.cs:245` solo corre con
  `motion.Y <= 0`, asi que en el frame del salto esta apagado.
- **Por que hay que forzar `wasGrounded = false`:** sin esto, la rama de `:74-79` corre en
  el mismo frame con el `wasGrounded` viejo. Es inofensivo hoy (solo clampa negativos),
  pero deja el estado del frame incoherente. Forzarlo hace que el resto del frame razone
  sobre "estamos en el aire".
- **Tooltip del campo:**
  `"Velocidad vertical inicial del salto, en unidades/segundo. 8 salta alrededor de 1.6 m con Gravity = -20."`
- **Verificacion:** Space -> el personaje despega. Mantener Space pulsado -> **un solo
  salto** (no se rebota al aterrizar). Space en el aire -> nada. Space contra un techo ->
  no atraviesa.
- **Riesgo residual:** no hay deteccion de golpe contra el techo, asi que tras tocarlo la
  velocidad vertical sigue siendo positiva hasta que la gravedad la gane. Se acepta: es
  imperceptible y un Fix tiene su propio coste.
- **Automatizable:** si, con `FakeInputHandler.PressKey(KeyCode.Space)`.
- **Esfuerzo:** 1h.
- **Commit:** `Feat: Implement jump using JumpForce.`

#### Bug 1.3 - `RunSpeed` implementado (hoy Shift no hace nada)

- **Archivo:** `ThirdPersonCharacterMovement.cs` (linea 91) + `:26-28` (tooltips)
- **Estado actual:** `public float RunSpeed = 8f;` (linea 28) y **cero uso**.
- **Cambio** - reemplazar la linea 91:

```csharp
// antes
targetVelocity = moveDir * WalkSpeed;
// despues
bool wantsRun = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
targetVelocity = moveDir * (wantsRun ? RunSpeed : WalkSpeed);
```

- **Cuidado con una cosa:** `Acceleration = 25` esta calibrado para `WalkSpeed = 5`. Con
  `RunSpeed = 8` la diferencia se alcanza en ~0.1 s, o sea que el sprint se siente
  instantaneo. Es aceptable, pero es **el parametro que mas probable hay que tocar** en
  el tuning. Anotarlo en la verificacion 5.4.
- **Tooltips:**
  - `WalkSpeed`: `"Velocidad horizontal andando, en unidades/segundo."`
  - `RunSpeed`: `"Velocidad horizontal con Shift, en unidades/segundo. Requiere Shift izquierdo o derecho."`
- **Verificacion:** Shift -> el personaje acelera hasta 8. Sin Shift -> hasta 5. Mezclar
  Shift con una direccion durante un frame no debe dar un salto de velocidad.
- **Automatizable:** si, con `FakeInputHandler.PressKey(KeyCode.LeftShift)`.
- **Esfuerzo:** 30 min.
- **Commit:** `Feat: Implement run using RunSpeed.`

#### Bug 1.4 - El personaje no rota con A/D puro (moonwalk lateral)

- **Archivo:** `ThirdPersonCharacterMovement.cs:115-132`
- **Problema:** linea 127, `shouldRotate = inputY > 0.01f;`. Con **puro A/D** el modelo no
  rota nunca: el personaje se desliza de lado mirando al frente. Es la sensacion de
  "moonwalk" y es de las que mas se notan sin saber explicar por que.
- **Cambio** - reemplazar las lineas 124-128:

```csharp
else
{
    // En FaceMovement rota cuando hay componente horizontal **o** vertical, para que
    // el puro A/D no produzca un moonwalk lateral. Antes solo giraba con input
    // vertical, y el personaje se deslizaba de lado mirando al frente.
    shouldRotate = inputY > 0.01f || inputX > 0.01f;
}
```

- **Por que `||` y no "cualquier input":** con `inputY < 0` (S) y sin componente lateral el
  comportamiento actual es no rotar (backpedal), y eso **se mantiene**: `S` puro da
  `inputX = 0` y `inputY = -1`, asi que no rota. Lo unico que cambia es que A/D puro ahora
  rota. Backwards con `S` + `A` rota, que es lo esperable.
- **Verificacion:** A puro -> el modelo se vuelve hacia la izquierda y sigue andando hacia
  ahi. D puro -> a la derecha. S puro -> el modelo **no** rota (se mantiene). W -> adelanta
  como antes.
- **Automatizable:** si.
- **Esfuerzo:** 30 min.
- **Commit:** `Fix: Rotate character with A/D when no vertical input.`

#### Bug 1.5 - Giro de 45 grados al correr en diagonal

- **Archivo:** `ThirdPersonCharacterMovement.cs:130-149`
- **Problema:** fuera de `StrafeMode` el modelo mira a `moveDir` (linea 132), que con
  W+D es un vector a 45 grados. El Slerp de la linea 147-148 intenta suavizarlo, pero con
  la formula actual (`TurnSpeed * Time.DeltaTime`, lineal) el resultado depende del
  framerate y no es el damping exponencial que ya usan el resto del componentes.
- **Cambio** - sustituir el Slerp de las lineas 147-148:

```csharp
Transform model = ModelRoot != null ? ModelRoot : Transform;

// Normalizar antes de mirar: si el input es analogico (gamepad) el vector puede
// quedar por debajo de MovementThreshold sin llegar a ser cero, y LookRotation
// sobre un vector diminuto no da una orientacion fiable.
Float3 lookTarget = Float3.Normalize(lookDir);
Quaternion targetRotation = Quaternion.LookRotation(lookTarget, Float3.UnitY);
// ... (offset de Facing, lineas 136-144, sin cambios) ...

// Damping exponencial: mismo patron que la camara (OrbitFollowCamera.cs:97) y que
// el propio movimiento del personaje (linea 96). 1 - Exp(-k*dt) es independiente
// del framerate, a diferencia de TurnSpeed * dt.
float tTurn = Maths.Clamp(1f - MathF.Exp(-TurnSpeed * Time.DeltaTime), 0f, 1f);
model.Rotation = Quaternion.Slerp(model.Rotation, targetRotation, tTurn);
```

- **El clamp no es cosmetico:** con `TurnSpeed` negativo puesto a mano desde el Inspector
  (que el Inspector permite), `1 - Exp(+x)` da `t > 1` y Slerp **extrapola** en vez de
  interpolar. Con `t` en `[0,1]` el Slerp degrada a Lerp, que es lo correcto.
- **Verificacion:** W y D a la vez,)-> el modelo pasa por 45 grados **suavemente**; a 30 y
  a 144 FPS el angulo por frame es el mismo. `TurnSpeed = -5` en el Inspector -> el
  personaje no se teletransporta ni explota.
- **Automatizable:** si.

> **Nota de commits (importante):** este bug **absorbe** dos correcciones que en el plan
> anterior estaban aparte - la independencia de framerate (el `TurnSpeed * dt` lineal) y el
> clamp del Slerp. Las tres cosas tocan la misma expresion de las lineas 147-148. **Un solo
> commit**, no tres: partir una linea en tres commits para cuadrar con una lista de
> auditoria no aporta nada y hace el historial mas dificil de leer.
>
- **Esfuerzo:** 1h.
>
> **Commit:** `Fix: Make character rotation framerate-independent and clamp slerp t.`

#### Bug 1.6 - `MovementThreshold` es decorativo, y es una trampa latente

- **Archivo:** `ThirdPersonCharacterMovement.cs:69, 88`
- **Problema A (decorativo):** `mag` solo puede ser 0, 1 o `sqrt(2)` porque el input viene
  de `GetKey` (lineas 53-56), que devuelve 0 o +-1. Comparar eso contra
  `MovementThreshold = 0.05f` (linea 38) es lo mismo que `mag > 0`. El campo aparece en el
  Inspector como si graduara algo cuando no lo hace.
- **Problema B (trampa latente):** `moveDir` solo se normaliza **dentro** del `if` de la
  linea 88. Si `shouldRotate` (linea 118) es true pero `mag <= MovementThreshold`, se llega
  a `Quaternion.LookRotation` con un vector **sin normalizar**. Hoy no se puede alcanzar
  desde teclado (si hay `inputY > 0.01` hay al menos un `GetKey` pulsado, o sea
  `mag >= 1`), asi que no es un bug activo. **Pero se vuelve alcanzable en cuanto entre
  input analogico**, que es justo lo que trae el gamepad de la Fase 4.
- **Comportamiento real del caso degenerado (medido contra Prowl.Vector 3.5.0):**
  - `Float3.Normalize(Float3.Zero)` devuelve `(0, 0, 0)`, no `NaN`. El `Normalize` de este
    motor es seguro con el vector cero.
  - `Quaternion.LookRotation((0,0,0), Float3.UnitY)` devuelve `(0, 0, 0, 0.7071)` con
    Euler `(0, 0, 0)`. Tambien es finito: **no crashea ni produce `NaN`.**
  - El sintoma real, por tanto, no es una excepcion: es que el modelo **se orientaria de
    golpe a la identidad** (mira al eje del mundo) durante un frame. Un glitch visible, no
    una corrupcion.
  - Por eso **subir el `Normalize` fuera del `if` no arregla nada**: seguiria llegando un
    vector cero a `LookRotation`. El guard tiene que ir donde se usa, no donde se calcula.
- **Cambio (preventivo, 1 linea):** el Bug 1.5 ya normaliza `lookDir` antes de
  `LookRotation`, lo que neutraliza el Problema B. Para el A:
  - Opcion 1 (la elegida): **dejar `MovementThreshold` como esta y documentarlo** en el
    tooltip - `"Umbral de input minimo para considerar que hay movimiento. Con teclado
    solo hay 0, 1 o diagonal, asi que cualquier valor entre 0 y 1 se comporta igual."`
    Es un par de lineas y no cambia el comportamiento.
  - Opcion 2 (NO recomendada): cambiar el input a analogico y hacer que el threshold
    signifique algo. Es una feature mayor, fuera de alcance.
- **Verificacion:** el personaje no se mueve con `MovementThreshold = 0` ni con
  `MovementThreshold = 10` (con `= 10` tampoco se mueve hoy, y el tooltip lo dice).
- **Esfuerzo:** 5 min.
- **Commit:** `Docs: Document that MovementThreshold is inert with keyboard input.`

> **Sobre el "Bug C" de la auditoria manual (offset de `Facing` que se duplica).** Se
> investigo y **no es un bug de codigo**. Tres comprobaciones:
>
> 1. `lookDir` siempre tiene `Y = 0`, porque `flatF` y `flatR` salen de la base de la
>    camara con la Y forzada a 0. Por tanto `Quaternion.LookRotation` siempre produce
>    **pitch 0** (medido: `LookRotation(diag45, Up).EulerAngles = (-0; 45; 0)`).
> 2. Con pitch 0, el Y local del modelo **coincide con el Y del mundo**, asi que
>    post-multiplicar por `FromEuler(0, offsetDeg, 0)` aplica el offset alrededor del Y del
>    mundo, que es lo correcto. Medido: 45 grados + 90 = 135, como debe ser.
> 3. `Transform.Rotation` es **espacio mundo** (se compone subiendo por la cadena de
>    padres, `Transform.cs:56-82`, y el setter divide por el padre al escribir). El
>    `Slerp` es un giro hacia objetivo estandar en mundo, no acumula nada.
>
> **Aplicar el offset antes del `LookRotation` seria un no-op.** El riesgo real es de
> **autoria**: `ModelRoot` con rotacion base en el editor **y** `Facing` puesto son el
> mismo ajuste por dos caminos a la vez, y los offsets se suman. Eso se resuelve
> documentandolo en los tooltips (Feature 3.1), no reordenando la multiplicacion.

#### Bug 1.7 - La eleccion de `accelRate` es binaria y frena mal

*Este es el bug real que hay detras del "Bug A" de la auditoria manual. El lerp por eje no
es un bug (ver nota al final de este bug); lo que si lo es es la eleccion de `accelRate`.*

- **Archivo:** `ThirdPersonCharacterMovement.cs:95`
- **Codigo actual:**
  ```csharp
  float accelRate = targetVelocity == Float3.Zero ? Deceleration : Acceleration;
  ```
- **Problema 1 (el de RunSpeed, y el que se nota):** el criterio mira **solo si el
  objetivo es cero**. No mira si estamos acelerando o frenando. Consecuencia concreta:
  soltar Shift mientras se sigue con W baja el objetivo de `RunSpeed = 8` a
  `WalkSpeed = 5`, asi que `targetVelocity != Zero` y **`accelRate = Acceleration = 25`**.
  El personaje frena de 8 a 5 con la tasa de *aceleracion*. En cambio, si sueltas W del todo,
  `targetVelocity == Zero` y frena con `Deceleration = 30`. **La misma accion (frenar) usa
  dos tasas distintas segun cuanto brakes**, y no hay ningun campo para ajustarlo.
- **Problema 2 (giro de direccion):** girar de +X a -X usa `Acceleration`, no una tasa de
  giro mayor. En la practica casi todos los feel buenos dan un "turn rate" mas alto que la
  aceleracion desde el reposo, porque cambiar de direccion tiene que leerse rapido.
- **Cambio** - comparar la magnitud actual contra la magnitud objetivo:
  ```csharp
  // Elegir la tasa por si estamos acelerando o frenando, no por si el objetivo es
  // cero. Antes, soltar Shift (8 -> 5) frenaba con Acceleration, igual que arrancar
  // desde el reposo: dos acciones distintas con la misma tasa.
  float currentSpeed = Float3.Length(_currentHorizontalVelocity);
  float targetSpeed = Float3.Length(targetVelocity);
  float accelRate = targetSpeed < currentSpeed ? Deceleration : Acceleration;
  ```
- **Por que `currentSpeed`/`targetSpeed` y no comparar vectores:**`_currentHorizontalVelocity`
  y `targetVelocity` estan **colineales** salvo en el frame en que cambia la direccion del
  input, donde la componente lateral desciende de forma suave. Comparar magnitudes captura
  "freno" correctamente sin que un giro brusco dispare `Deceleration` por un error de
  magnitud.
- **Nota sobre el lerp por eje (NO es un bug):** el codigo escribe el lerp componente a
  componente con `Maths.Lerp`, y se propuso cambiarlo a un unico `Float3.Lerp(...)`.
  Verificado contra Prowl.Vector 3.5.0: **`Float3.Lerp` no existe** (0 overloads; lo unico
  parecido es `Float3.MoveTowards(Float3, Float3, float)`), asi que ese cambio **no
  compila**. Y aunque existiera, seria un refactor sin cambio de comportamiento: el lerp
  componente a componente con el mismo `t` es matematicamente identico a `a + (b-a)*t`
  (comprobado: ambos dan `(1.9, 2.9, 4.2)`). Ademas va contra la convencion del repo, que
  usa `Maths.Lerp` por componente en 12 sitios y `Float3.Lerp` en 0.
- **Por que "un modo gradual" no va aqui:** un knob extra para la tasa de giro es una
  **feature**, no un fix, y la filosofia del proyecto prohibe imponerla. Va como feature
  opcional en la Fase 4 (Feature 4.7).
- **Verificacion:** Sprint a 8, soltar Shift sin soltar W -> el personaje frena de 8 a 5.
  Antes: esa frenada usaba `Acceleration` (25). Automatizable: `PressKey(LeftShift)` +
  `ReleaseKey(LeftShift)` y medir la velocidad por frame.
- **Esfuerzo:** 15 min.
- **Commit:** `Fix: Choose acceleration rate by whether slowing down, not by target being zero.`

**Esfuerzo Fase 1: ~3h 55 min** (8 items; el codigo son ~30 lineas en total. El peso real
es verificar que el movimiento no se rompio en ningun commit intermedio - en especial
despues de 1.0, que cambia la convencion de pitch y toca la misma linea de input que
usan el resto de fixes).

---

### Fase 2 - Bugs de camara restantes

*El pitch ya se resolvio en el Bug 1.0, por la regla de jugabilidad. Aqui quedan los dos
que no bloquean jugar: ninguno impide apuntar ni mover al personaje.*

#### Bug 2.1 - Comentarios mentirosos en `DefaultInputHandler`

- **Archivo:** `Zenith.Runtime/InputManagement/DefaultInputHandler.cs:68` y `:382`
- **Cambio:** no hay inversion de Y en ninguno de los dos sitios.
  - `:68` - `return new Float2(delta.X, delta.Y); // Invert Y to match gamepad (up = positive)`
    -> borrar el comentario.
  - `:382` - `return new Float2(thumbstick.X, thumbstick.Y); // We flip y to make UP on the stick positive`
    -> borrar el comentario.
- **Por que importa mas de lo que parece:** el Bug 1.0 existe precisamente porque alguien
  confio en la convencion "mouse up = positivo" que estos comentarios prometen. Un
  comentario que promete una normalizacion que no ocurre es la causa raiz de que el proximo
  agente vuelva a invertir el pitch.
- **Verificacion:** no queda ningun comentario en el archivo que describa una inversion que
  el codigo no hace.
- **Alternativa descartada:** implementar la normalizacion. Eso es un cambio de
  comportamiento del input, fuera de alcance. Se elige borrar el comentario.
- **Commit:** `Docs: Remove comments claiming Y inversion that DefaultInputHandler does not do.`

#### Bug 2.2 - LookAt inconsistente con el pivote

- **Archivo:** `OrbitFollowCamera.cs:146-149`
- **Problema:** la posicion de la camara se deriva de `_smoothedTargetPos` (pivote
  suavizado, lineas 113-118), pero el punto de mira se recalcula desde `Target.Position`
  **crudo** (linea 147). Dos fuentes de verdad para la misma geometria -> el punto de mira
  "tira" del personaje mientras este se mueve.
- **Cambio:**
  ```csharp
  // antes
  Float3 lookAtPoint = Target.Position + new Float3(0, ChestOffset, 0);
  // despues
  Float3 lookAtPoint = _smoothedTargetPos + new Float3(0, ChestOffset - TargetHeight, 0);
  ```
- **Por que `ChestOffset - TargetHeight`:** `ChestOffset` (1.0) es hoy una altura medida
  desde los pies del target, mientras que `_smoothedTargetPos` ya lleva `TargetHeight`
  (1.5) sumado. Restarlo reproduce **exactamente** el mismo punto en el mundo
  (1.0 - 1.5 = -0.5) pero desde el pivote suavizado. No cambia el encuadre, solo quita el
  tiron.
- **Verificacion:** correr en linea recta y orbitar a la vez -> el personaje se queda quieto
  en el encuadre.
- **Commit:** `Fix: Use smoothed pivot for OrbitFollowCamera lookAt.`

**Esfuerzo Fase 2: ~30 min.**

---

### Fase 3 - Tooltips y desambiguacion de nombres

*(Los campos **ya estan en el Inspector** - son `public` (`RuntimeUtils.cs:345`). Lo que
falta es que el usuario sepa que son. Nada de esto cambia el comportamiento.)*

#### Feature 3.1 - Tooltips en ambos componentes

- **Archivo:** `OrbitFollowCamera.cs:15-35`, `ThirdPersonCharacterMovement.cs:17-42`
- **Cambio:** anadir `[Tooltip("...")]` a cada campo, sin tocar su nombre ni su default.
  Anadir `[Range]` donde el rango sea obvio. `Range` y `Tooltip` ya estan soportados
  (`GameObject/Attributes/InspectorAttributes.cs:11,36`; `AudioSource.cs:40-72` es el
  ejemplo a imitar).

  `ThirdPersonCharacterMovement` (los que mas importan, los demas son analogos):

  | Campo | Linea | Tooltip |
  |---|---|---|
  | `Camera` | 18 | "Camara de la que se toma la base de movimiento. Debe ser una OrbitFollowCamera: publica FlatForward y FlatRight." |
  | `Controller` | 19 | "CharacterController que hace el movimiento y la colision." |
  | `Acceleration` | 22 | "Rapidez con la que el personaje alcanza la velocidad objetivo." |
  | `Deceleration` | 23 | "Rapidez con la que el personaje frena. Mayor = frena mas rapido." |
  | `TurnSpeed` | 28 | "Velocidad de giro del modelo hacia la direccion de movimiento. Mayor = gira mas rapido." |
  | `ModelRoot` | 31 | "Transform del modelo que rota. Si esta vacio, rota el GameObject entero. **No le pongas rotacion en el editor si vas a usar `Facing`**: los dos mecanismos se suman y la orientacion sale mal." |
  | `Facing` | 32 | "Correccion de orientacion del modelo respecto a su forward. Usa esto **o** la rotacion base del `ModelRoot`, nunca las dos: son el mismo ajuste por dos caminos." |
  | `StrafeMode` | 35 | "Si esta activo, el personaje siempre mira hacia donde mira la camara en vez de hacia donde se mueve." |
  | `Gravity` | 41 | "Aceleracion hacia abajo, en unidades/s2. Negativo." |

- **Commit:** `Docs: Add inspector tooltips and ranges to camera and movement components.`

#### Feature 3.2 - Desambiguar los dos "min distance"

- **Archivo:** `OrbitFollowCamera.cs:32-35` (y el bloque nuevo de zoom en Fase 4)
- **Problema:** ya existe `public float MinDistance = 1f` en el bloque
  `[Header("Collision")]` (linea 35) - es el minimo **por colision**. La feature de zoom
  necesita sus propios min/max. Nombrarlos `_minDistance` / `_maxDistance` seria legal
  (distinto case) pero una trampa: dos campos con casi el mismo nombre visible en el mismo
  Inspector significando dos cosas distintas.
- **Cambio:** los nuevos campos de zoom se llaman **`_minZoomDistance`** /
  **`_maxZoomDistance`**, bajo `[Header("Zoom")]`, con tooltips que dicen explicitamente que
  son el rango de zoom y que `MinDistance` sigue siendo el minimo por colision.
- **Commit:** `Docs: Disambiguate collision and zoom distance fields in OrbitFollowCamera.`

**Esfuerzo Fase 3: ~1h 15 min.**

---

### Fase 4 - Features nuevas OPCIONALES (todas con toggle)

*(Cada feature arranca con `[SerializeField] private bool _xxxEnabled`, y sus parametros
con `[EnableIf("_xxxEnabled")]`. Se escriben y se commitean **una por una**.)*

#### Feature 4.1 - Colision de camara con spring arm (suavizada)

- **Archivo:** `OrbitFollowCamera.cs:33` (toggle), `:127-141` (logica)
- **Estado actual:** `CollisionEnabled = false` hardcodeado, y cuando se activa
  **teleporta**: el raycast (`:135`) sobrescribe `desiredPos` de golpe (`:138`). No hay
  estado intermedio.
- **Sub-cambio 4.1.a - Activar por defecto:**
  - `[SerializeField] private bool _collisionEnabled = true;`
  - Default `true`: es lo que espera cualquier escena con geometria, y su ausencia es un
    agujero en el que la camara se mete dentro de las paredes.
  - Tooltip: `"Evita que la camara atraviese paredes y geometria. Acerca la camara automaticamente cuando algo se interpone."`
- **Sub-cambio 4.1.b - Spring suave:**
  - `[SerializeField] private float _collisionPullInSpeed = 20f;` (`[EnableIf("_collisionEnabled")]`)
    - tooltip: `"Que tan rapido se acerca la camara al obstaculo. Alto = casi instantaneo."`
  - `[SerializeField] private float _collisionPushOutSpeed = 5f;` (`[EnableIf("_collisionEnabled")]`)
    - tooltip: `"Que tan rapido se aleja la camara cuando el obstaculo desaparece. Bajo = sale despacio."`
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
- **Por que esta feature es ahora mas relevante que antes:** con el Bug 1.1 aplicado, la
  camara puede acercarse a 1 sin que el movimiento del personaje se degrade. Antes, activar
  la colision rompia el input. Ese era el acoplamiento del que todo venia.
- **Verificacion:** empujar la camara contra una pared -> se acerca con suavidad, no
  atraviesa. Alejarse -> vuelve despacio. Y en todo momento, W sigue moviendo en la misma
  direccion de pantalla.
- **Commit:** `Feat: Add optional smoothed spring arm camera collision.`

#### Feature 4.2 - Zoom con rueda (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Campos nuevos:**
  - `[SerializeField, Tooltip("Zoom con la rueda del raton. Si esta desactivado, la distancia es fija.")] private bool _zoomEnabled = true;`
  - `[SerializeField, Tooltip("Distancia minima al hacer zoom in."), EnableIf("_zoomEnabled")] private float _minZoomDistance = 2f;`
  - `[SerializeField, Tooltip("Distancia maxima al hacer zoom out."), EnableIf("_zoomEnabled")] private float _maxZoomDistance = 15f;`
  - `[SerializeField, Tooltip("Velocidad del zoom. 1 = un tope de rueda recorre el rango completo."), EnableIf("_zoomEnabled")] private float _zoomSpeed = 1f;`
- **Logica** (junto a la linea 87, **fuera del bloque `if (shouldOrbit)`**: el zoom debe
  funcionar tambien con el cursor libre):
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
    mueve menos que uno lejos), que es lo que espera el jugador.
  - **Guarda:** si `_maxZoomDistance < _minZoomDistance`, el `Maths.Clamp` queda mal
    definido para valores fuera de rango. Normalizar los dos limites entre si antes, porque
    el Inspector deja escribir cualquier par.
  - **`Distance` se muta en runtime:** es intencional, para que el Inspector muestre el
    valor actual. Pero un proyecto guardado guarda la distancia con el zoom aplicado -
    documentado en el tooltip de `Distance` (Feature 3.1).
- **Verificacion:** rueda arriba -> acerca (tope 2), abajo -> aleja (tope 15). Con
  `_zoomEnabled = false`, la distancia no cambia.
- **Automatizable:** si, con `FakeInputHandler.SetMouseWheel`.
- **Commit:** `Feat: Add optional mouse wheel zoom to OrbitFollowCamera.`

#### Feature 4.3 - Gamepad en la camara (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Correccion importante respecto a la auditoria original:** la API real **no** es
  `Input.GetGamepadAxis(GamepadAxis.RightStickX/Y)`. No existe ningun enum `GamepadAxis`.
  Lo que hay es:
  - `Input.IsGamepadConnected(int gamepadIndex = 0)` - `Input.cs:212`
  - `Input.GetGamepadRightStick(int gamepadIndex = 0)` que devuelve `Float2` - `Input.cs:217`
- **Campos nuevos:**
  - `[SerializeField, Tooltip("Permite orbitar la camara con el stick derecho del mando.")] private bool _gamepadEnabled = true;`
  - `[SerializeField, Tooltip("Indice del mando. 0 es el primero."), Range(0, 15), EnableIf("_gamepadEnabled")] private int _gamepadIndex = 0;`
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
          // Mismo signo que el raton tras el Bug 1.0: +Y del stick = arriba = pitch +
          _pitchTarget += stick.Y * _gamepadSensitivity * dt;
      }
  }
  ```
  - **Unidades:** el raton suma `MouseDelta * Sensitivity` (pixeles -> grados). El stick es
    un eje normalizado -1..1, asi que necesita `* dt` para ser una velocidad y no un
    salto.
  - **Deadzone:** sin ella, un stick con deriva (todos los tienen) hace que la camara gire
    sola para siempre. El 0.15 es el que ya usa el sample de FlyCamera
    (`Samples/FlyCamera/Program.cs:134`).
  - **Relacion con el Bug 1.6:** esta feature es de la camara, no del personaje, asi que no
    introduce input analogico en `ThirdPersonCharacterMovement`. Si en el futuro se anade
    stick izquierdo al personaje, el Bug 1.6 vuelve a ser un bug activo.
- **Referencia de patron:** `Samples/FlyCamera/Program.cs:126-136` - pero ese sample usa
  `InputActionMap` con `DualAxisCompositeBinding`, un nivel de abstraccion que **no** se
  esta adoptando aqui.
- **Verificacion:** stick derecho -> la camara orbita. Soltar -> se para (sin deriva). Sin
  mando conectado -> sin efecto ni excepcion.
- **Automatizable:** si, con `FakeInputHandler.SetGamepadAxis(0, 1, ...)`.
- **Commit:** `Feat: Add optional gamepad orbit support to OrbitFollowCamera.`

#### Feature 4.4 - Invertir eje Y (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Campo:**
  - `[SerializeField, Tooltip("Invierte el eje vertical. Para quien juegue con control invertido.")] private bool _invertY = false;`
- **Logica** (una linea, en `:91` tras el fix del Bug 1.0):
  ```csharp
  float pitchSign = _invertY ? -1f : 1f;
  _pitchTarget += pitchSign * Input.MouseDelta.Y * Sensitivity * mult;
  ```
- **Default `false`:** el Bug 1.0 deja el comportamiento correcto.
- **Verificacion:** con el toggle ON, raton arriba -> camara baja. OFF -> sube.
- **Commit:** `Feat: Add InvertY option to OrbitFollowCamera.`

#### Feature 4.5 - Sensibilidad normalizada por resolucion (opcional)

- **Archivo:** `OrbitFollowCamera.cs`
- **Renombrar respecto a la auditoria original:** esto **no** es "DPI-aware". El motor no
  pide escalado por DPI al sistema operativo - GLFW no esta en modo `RawMouseMotion`
  (`DefaultInputHandler.ApplyCursorState:272-289` solo alterna
  `Disabled`/`Normal`/`Hidden`). Lo que si pasa es que **`Input.MouseDelta` esta en
  pixeles del framebuffer**, y un framebuffer 4K da el doble de deltas que uno 1080p para
  el mismo movimiento fisico del raton. El nombre correcto es **"sensibilidad normalizada
  por resolucion"**.
- **Campos:**
  - `[SerializeField, Tooltip("Normaliza la sensibilidad por la altura del framebuffer, para que a 4K no gire el doble que a 1080p.")] private bool _resolutionNormalizedSensitivity = true;`
  - `[SerializeField, Tooltip("Altura de referencia en pixeles. 1080 es el estandar."), EnableIf("_resolutionNormalizedSensitivity")] private float _referenceHeight = 1080f;`
- **Fuente de la resolucion:** **no existe `Screen.Height`**. Lo que hay:
  - `Window.InternalWindow.FramebufferSize.Y` - como hace `Camera.cs:284`.
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
    `_pitchTarget` -> la camara se va y no vuelve.
  - **Consecuencia a documentar:** al activar esto, `Sensitivity = 0.13` deja de ser "lo
    que feel bien a 1080p". El default `true` **cambia el feel actual en pantallas que no
    sean 1080p**, asi que hay que decidirlo con el usuario en la verificacion 5.4.
- **Verificacion:** misma distancia fisica de raton a 1920x1080 y a 3840x2160 -> el mismo
  angulo. Con el toggle OFF -> el angulo se dobla en 4K.
- **Commit:** `Feat: Add resolution-normalized sensitivity option to OrbitFollowCamera.`

#### Feature 4.6 - Snap por teleport (opcional)

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
  contra el suavizado, un sprint podria disparar el snap sin que hubiera hubo teleport.
- **Por que resetear tambien `_currentDistance`:** si habia un obstaculo pegado a la camara
  en el punto viejo, la distancia colapsada se arrastraria al nuevo sitio.
- **Verificacion:** teleportear el target lejos -> la camara aparece ya en su sitio, sin
  barrido. Correr normal -> el snap no se dispara nunca.
- **Commit:** `Feat: Add snap-on-teleport option to OrbitFollowCamera.`

#### Feature 4.7 - Tasa de giro separada de la aceleracion (opcional)

*Viene del "modo gradual" pedido en la auditoria manual. Es una feature, no un fix: por la
filosofia del proyecto no se impone, asi que nace apagada.*

- **Archivo:** `ThirdPersonCharacterMovement.cs` (tras el Bug 1.7)
- **Problema que resuelve:** el Bug 1.7 decide entre `Acceleration` y `Deceleration` segun
  si frena o acelera. No hay tercera opcion: **cambiar de direccion usa la misma tasa que
  arrancar desde el reposo.** En la practica casi todos los feel buenos dan un "turn rate"
  mas alto para el giro, porque cambiar de sentido tiene que leerse rapido o se siente
  como patinar.
- **Campos:**
  - `[SerializeField, Tooltip("Permite una tasa de giro distinta de la aceleracion al cambiar de direccion.")] private bool _turnRateEnabled = false;`
  - `[SerializeField, Tooltip("Tasa usada al cambiar de sentido. Mayor = gira mas rapido."), EnableIf("_turnRateEnabled")] private float _turnRate = 20f;`
- **Default `false`:** sin este flag el componente se comporta exactamente como ahora, con
  el arreglo del Bug 1.7. Encenderlo cambia el feel, asi que es decision del usuario.
- **Logica** (encima de la eleccion de `accelRate` del Bug 1.7):
  ```csharp
  if (_turnRateEnabled)
  {
      // Solo cuando hay un cambio de sentido real: mismo signo de componente
      // dominante, no el mismo eje.
      if (currentSpeed > 0.01f && Float3.Dot(_currentHorizontalVelocity, targetVelocity) < 0f)
          accelRate = _turnRate;
  }
  ```
  - **Por que el `Dot < 0` y no "cambió el eje":** un input en diagonal cambia los dos ejes
    sin ser un giro de verdad (ir de adelante-derecha a adelante-izquierda no es dar media
    vuelta). El `Dot` negativo solo se dispara cuando el objetivo apunta de verdad al lado
    contrario.
- **Verificacion:** con el toggle OFF, girar 180 grados se comporta como ahora. Con ON, el
  personaje llega antes a mirar al otro lado. Y avanzar en diagonal NO dispara la tasa de
  giro.
- **Commit:** `Feat: Add optional separate turn rate for direction changes.`

**Esfuerzo Fase 4: ~5h 15 min** (7 features, ~45 min cada una).

---

### Fase 5 - Verificacion y tests

#### Verificacion 5.1 - Bugs, automatizado

Todos contra `FakeInputHandler` + `scene.Update()` (que bombea `LateUpdate`,
`Scene.cs:936-952`):

- [ ] `ThirdPersonCharacterMovementTests.Jump_Jumps_WhenGroundedAndSpacePressed`
- [ ] `ThirdPersonCharacterMovementTests.Jump_DoesNotTrigger_InAir` (un solo salto)
- [ ] `ThirdPersonCharacterMovementTests.Run_ReachesRunSpeed_WithShift`
- [ ] `ThirdPersonCharacterMovementTests.Walk_UsesWalkSpeed_WithoutShift`
- [ ] `ThirdPersonCharacterMovementTests.PureStrafe_RotatesModel_WithAD`
- [ ] `ThirdPersonCharacterMovementTests.BackwardOnly_DoesNotRotate` (S puro sigue igual)
- [ ] `ThirdPersonCharacterMovementTests.TurnRate_FrameRateIndependent`
- [ ] `ThirdPersonCharacterMovementTests.SlerpT_Clamped_WhenTurnSpeedNegative`
- [ ] `ThirdPersonCharacterMovementTests.MovementBasis_SameAtCameraDistance1And6`
- [ ] `ThirdPersonCharacterMovementTests.DegenerateBasis_DoesNotProduceNaN`
- [ ] `ThirdPersonCharacterMovementTests.RunSpeed_ReleasedWhileHoldingW_UsesDeceleration`
- [ ] `ThirdPersonCharacterMovementTests.TurnRateDisabled_ByDefault_BehaviorUnchanged`
- [ ] `OrbitFollowCameraTests.PitchDelta_MouseUp_IncreasesPitch`
- [ ] `OrbitFollowCameraTests.Pitch_ClampedToMinMax`
- [ ] `OrbitFollowCameraTests.FlatForward_MatchesYawForward_NotLookAtDirection`
- [ ] `OrbitFollowCameraTests.FlatForward_OrthogonalToFlatRight`
- [ ] `OrbitFollowCameraTests.LookAt_UsesSmoothedPivotNotRawTarget`
- [ ] `OrbitFollowCameraTests.MissingTarget_DoesNotThrow` (el `Camera.Target` nulo ya no
      aparece en el personaje, pero la guarda de la camara sigue viva)

#### Verificacion 5.2 - Bugs, manual (sigue haciendo falta)

- [ ] Space salta, y mantenerlo pulsado no rebota
- [ ] Shift acelera hasta `RunSpeed`
- [ ] A puro: el modelo se vuelve a la izquierda, sin moonwalk
- [ ] W+D: el modelo pasa por 45 grados con suavidad, sin latigazo
- [ ] Limitador de FPS a 30 y a 144: el personaje gira igual
- [ ] Raton arriba -> camara sube y para en 18; raton abajo -> baja hasta 36
- [ ] Correr y orbitar a la vez: el personaje no se sale del encuadre

#### Verificacion 5.3 - Features, una por una, con el toggle OFF primero

El orden importa: **cada feature se prueba OFF (no debe cambiar nada respecto al video de
la Fase 0), luego ON.**

- [ ] Colision OFF -> camara identica a la linea base
- [ ] Colision ON -> contra una pared: se acerca suave, no atraviesa; al salir, se aleja
      despacio; **y W sigue yendo en la misma direccion de pantalla**
- [ ] Zoom OFF -> la distancia no cambia con la rueda
- [ ] Zoom ON -> rueda arriba acerca (tope 2), abajo aleja (tope 15)
- [ ] Gamepad OFF -> el stick no hace nada
- [ ] Gamepad ON -> el stick orbita; sin deriva al soltar; sin mando, sin crashear
- [ ] InvertY ON -> el pitch se invierte
- [ ] Normalizacion OFF -> la sensibilidad es la de siempre
- [ ] Normalizacion ON -> 1080p y 4K giran igual
- [ ] Snap OFF -> un teleport se ve como barrido (comportamiento actual)
- [ ] Snap ON -> el teleport salta

#### Verificacion 5.4 - Feel (subjetivo: lo decide el usuario)

Lo que no se puede testear, porque no hay assert posible para "esto se siente bien":

- [ ] Se siente mejor que la linea base? -> **Si / No**
- [ ] Que bug fue el que mas cambio el feel? -> __________
- [ ] El salto se siente bien con `JumpForce = 8` y `Gravity = -20`? -> **Si / No**
- [ ] `Acceleration = 25` aguanta el sprint a `RunSpeed = 8` o se siente instantaneo? ->
      **Si / No**
- [ ] Que feature marco la diferencia? -> __________
- [ ] Que feature sigue sintiendose mal? -> __________
- [ ] El default de `Sensitivity = 0.13` sigue siendo el correcto con la normalizacion
      por resolucion activada? -> **Si / No** (decision del usuario)
- [ ] `_collisionPullInSpeed = 20` / `_collisionPushOutSpeed = 5`: el tiron al pegarse a
      una pared se nota demasiado? -> __________

**Esfuerzo Fase 5: ~3h** (18 tests nuevos ~1h 40, verificacion manual ~1h 20).

---

### Fase 6 - Cierre

- [ ] `dotnet build` sin warnings nuevos
- [ ] `dotnet test` verde y **el recuento no ha bajado** respecto a la linea base de la
      Fase 0 (los 18 tests nuevos suben el total; si baja, algo se rompio)
- [ ] Grabar el video "despues" y ponerlo lado a lado con el de la Fase 0
- [ ] Actualizar este documento: marcar las casillas, anotar los defaults finales que haya
      elegido el usuario
- [ ] Mergear a `main` y tag - **el tag lo confirma el usuario**
- [ ] Los defaults que el usuario haya cambiado durante el tuning quedan como nuevos
      defaults en el codigo, con un comentario de una linea explicando por que

**Esfuerzo Fase 6: ~40 min.**

---

## Estimacion

| Fase | Contenido | Esfuerzo |
|---|---|---|
| 0 | Preparacion (rama, video, linea base de tests) | 20 min |
| 1 | 8 P0 (pitch, handshake, salto, run, A/D, 45 grados, threshold, accelRate) | 3h 55 min |
| 2 | 2 bugs de la camara (comentarios mentirosos, lookAt) | 30 min |
| 3 | Tooltips, rangos y desambiguacion de nombres | 1h 15 min |
| 4 | 7 features opcionales (con toggle) | 5h 15 min |
| 5 | 18 tests nuevos + verificacion manual | 3h |
| 6 | Build, tests, video, merge | 40 min |
| | **Total** | **~14h 45 min** |

Es mas que las ~11h estimadas, y el motivo es concreto: la Fase 1 son 7 P0 en vez de 1, el
salto y el run son features nuevas disfrazadas de bug (no es un one-liner, es implementar
la mecanica y decidir donde insertarla sin que `SnapToGround` lo cancele), y la Fase 5
crece de 13 a 18 tests porque el handshake, el salto y la frenada son comprobables y hay que
cubrir el caso degenerado (base cero -> orientacion identidad).

El trabajo de codigo son ~4h 30 min. Las 10h 15 restantes son verificacion, que es donde
se decide si esto ha servido de algo.

**Nota sobre el orden de la Fase 1:** el Bug 1.0 (pitch) cuesta 5 minutos y va primero. Es
el unico fix que estaba bloqueando - sin el, el jugador no puede apuntar y todo lo demas es
imposible de evaluar. Si hay que recortar tiempo, se recorta de la Fase 4 (features
opcionales), nunca de la Fase 1.

---

## Riesgos

| # | Riesgo | Mitigacion |
|---|---|---|
| R1 | **Renombrar campos rompe datos serializados.** La serializacion es por nombre y no hay `FormerlySerializedAs`. Renombrar `JumpForce` a `_jumpForce` deja escenas y prefabs con el default. | No se renombra nada. `FlatForward` / `FlatRight` son **propiedades**, no campos: no aparecen en el Inspector y no affecting la serializacion. |
| R2 | **`CharacterController` no tiene metodo de salto.** Verificado: no existe. | El salto se implementa en el personaje sobre `_verticalVelocity`, que ya existe. **No hace falta tocar `CharacterController`.** Verificado tambien que `SnapToGround` (`:245`) solo corre con `motion.Y <= 0`, asi que no cancela el salto. |
| R3 | **Doble salto por leer `IsGrounded` en el momento equivocado.** `Move` actualiza `IsGrounded` al final (`:259`). | Saltar usando el `wasGrounded` de la linea 72, que es el estado de principio de frame, y forzar `wasGrounded = false` despues. Test explicito de "mantener Space no rebota". |
| R4 | **El salto se cancela sin querer.** Si `motion.Y` saliera <= 0 en el frame del salto, `SnapToGround` (`CharacterController.cs:245`) pegaria al personaje al suelo. | El test de salto falla de inmediato si esto pasa. Es el primer assert del test de salto. |
| R5 | **`Acceleration = 25` no aguanta `RunSpeed = 8`.** El sprint se alcanza en ~0.1 s y se siente instantaneo. | Es el parametro mas probable que haya que tocar en el tuning. Esta en la verificacion 5.4 como pregunta al usuario, no como decision del codigo. |
| R6 | **Un `NaN` en la base de movimiento desaparece al personaje para siempre.** Se propaga a `_currentHorizontalVelocity` y a la posicion. | `OrbitFollowCamera` publica una base neutra (`UnitZ`/`UnitX`) en el caso degenerado, y el personaje hace `return` si la base viene con longitud ~0. Dos guards, no uno. |
| R7 | **`FlatForward` se publica en `LateUpdate` y se lee en `Update`: queda un frame de atraso.** | No se elimina, pero **deja de importar**: el valor es un yaw suavizado e independiente del target, no una posicion. Un frame de atraso sobre un valor suavizado es indetectable. Documentado en el tooltip de `Camera`. |
| R8 | **`EnableIf` necesita el nombre exacto del campo** (`"_xxxEnabled"`). Un typo no da error de compilacion: simplemente no greyea nada. | Revisar visualmente el Inspector con cada toggle en OFF despues de anadirlo, no confiar en el build. |
| R9 | **Defaults nuevos rompen proyectos existentes.** Poner `_collisionEnabled = true` cambia el comportamiento de cualquier escena que tuviera la camara atravesando paredes. | Es el default correcto (arregla un agujero), pero hace falta el video de la linea base para mostrar la diferencia. |
| R10 | **Division por cero en la normalizacion** si el framebuffer mide 0 (headless, tests) -> `NaN` en `_pitchTarget` y la camara se va para siempre. | Guarda `if (height > 0)` obligatoria. |
| R11 | **El feel no se puede automatizar.** Es posible pasar los 18 tests y que el personaje siga sintiendose mal. | La verificacion 5.4 es obligatoria y la decide el usuario. El video antes/despues es la evidencia. |
| R12 | **El giro a 45 grados con damping exponencial puede sentirse "tardio"** con `TurnSpeed = 12`. Antes era lineal (mas brusco). | `TurnSpeed` esta expuesto y es el primer candidato a subir en el tuning. Se comprueba en 5.4. |
| R13 | **Un fix puede tapar a otro y falsear la evaluacion.** Con el pitch roto, el personaje "va mal" aunque la base de movimiento sea correcta, y viceversa. | Ordenarlos por la regla de jugabilidad: el pitch (Bug 1.0) es "no puedo jugar" y el handshake (1.1) es "juego pero se siente raro". Cada uno se verifica por separado, en su commit, antes de pasar al siguiente. |
| R14 | **`Float3.Lerp` no existe en Prowl.Vector.** Un "simplificar el lerp por eje a una llamada" **no compila**. Verificado: 0 overloads en 3.5.0; lo unico parecido es `Float3.MoveTowards`. | El Bug 1.7 documenta el hallazgo y **no** cambia el lerp. Si alguien lo reintenta, la alternativa correcta es `Float3.MoveTowards` con distancia, que ademas no es lo mismo que lerp exponencial y cambiaria el comportamiento. |
| R15 | **`Float3.Normalize` es seguro con el vector cero, pero `LookRotation` no lo es conceptualmente.** Medido: `Normalize(0)` da `(0,0,0)` y `LookRotation((0,0,0), Up)` da una rotacion finita de Euler `(0,0,0)` - no hay `NaN` ni excepcion, pero el modelo se orienta de golpe a la identidad. | El guard va **donde se usa el vector** (Bug 1.5 normaliza `lookDir`), no donde se calcula. No subir el `Normalize` fuera del `if`: solo cambia un vector sin normalizar por un vector cero. |
| R16 | **`Facing` y la rotacion base del `ModelRoot` son el mismo ajuste por dos caminos.** Usar los dos suma los offsets y el modelo queda mal orientado. | Resuelto con tooltips que avisan explicitamente (Feature 3.1), no con un cambio de codigo. **No reordenar la multiplicacion del offset**: con `lookDir.Y == 0` el pitch siempre es 0, el Y local es el Y del mundo, y el orden es irrelevante (medido: 45 + 90 = 135 en ambos casos). |

---

## Criterios de "terminado"

- [ ] Los 8 P0 de la Fase 1 arreglados, cada uno con su commit, **en el orden del plan**
      (pitch primero).
- [ ] Los 2 bugs de la Fase 2 arreglados, cada uno con su commit.
- [ ] Soltar Shift **frena** con `Deceleration`, no con `Acceleration` (Bug 1.7).
- [ ] El Bug 1.0 incluye el cambio de rango de pitch, no solo el signo.
- [ ] El Bug 1.0 se puede jugar: raton arriba -> la camara sube.
- [ ] **El personaje puede saltar y puede correr.** `JumpForce` y `RunSpeed` hacen algo.
- [ ] **El personaje no hace moonwalk lateral** con A/D puro.
- [ ] La base de movimiento viene de la camara via `FlatForward` / `FlatRight`, no de
      posiciones.
- [ ] W mueve en la misma direccion de pantalla con la camara a distancia 6 y pegada a
      una pared.
- [ ] Todas las features nuevas son **opcionales**: cada una con su
      `[SerializeField] private bool _xxxEnabled` y sus parametros con
      `[EnableIf("_xxxEnabled")]`.
- [ ] Con todos los toggles en OFF, ambos componentes se comportan **exactamente** como la
      linea base grabada en la Fase 0 (salvo los bugs, que son fixes).
- [ ] Ningun campo existente se ha renombrado. `FlatForward` / `FlatRight` son
      propiedades.
- [ ] Todos los campos tienen tooltip, y los tooltips dicen la verdad.
- [ ] Cobertura de tests: de 0 a 18 tests nuevos en verde, sobre el recuento de la Fase 0.
- [ ] `dotnet build` limpio; `dotnet test` sin regresiones.
- [ ] El feel es mejor, segun el usuario (verificacion 5.4 respondida, no asumida).
- [ ] Video antes/despues grabado.
- [ ] La rama se puede mergear sin conflictos.