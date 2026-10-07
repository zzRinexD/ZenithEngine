using System;
using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Camera/Orbit Follow Camera")]
public class OrbitFollowCamera : MonoBehaviour
{
    public enum OrbitMode
    {
        HoldRightClick,
        LockedCursor
    }

    [Header("Target")]
    [Tooltip("Transform al que sigue la camara. Sin el el componente no hace nada.")]
    public Transform Target;

    [Tooltip("Altura del pivote sobre los pies del target. Es el punto alrededor del que orbita la camara.")]
    public float TargetHeight = 1.5f;

    [Header("Orbit")]
    [Tooltip("HoldRightClick: orbita solo con el boton derecho. LockedCursor: captura el cursor.")]
    public OrbitMode Mode = OrbitMode.LockedCursor;

    [Tooltip("Distancia de la camara al pivote. El zoom con rueda modifica este valor en runtime.")]
    public float Distance = 6f;

    [Tooltip("Sensibilidad del raton, en grados por pixel. Rango recomendado: 0.05 - 0.5.")]
    public float Sensitivity = 0.13f;
    [Tooltip("Multiplicador de sensibilidad en modo HoldRightClick.")]
    public float HoldModeSensitivityMultiplier = 1.8f;

    [Tooltip("Suavizado de la rotacion. Mayor = mas rigido e inmediato."), Range(5f, 30f)]
    public float RotationSmoothing = 18f;

    [Tooltip("Limite inferior de inclinacion, en grados. Negativo = por encima del horizonte.")]
    public float MinPitch = -36f;

    [Tooltip("Limite superior de inclinacion, en grados. Positivo = por debajo del horizonte.")]
    public float MaxPitch = 18f;

    [Header("Feel (optional)")]
    [Tooltip("Invierte el eje vertical. Para quien juegue con control invertido.")]
    private bool _invertY = false;

    [Tooltip("Normaliza la sensibilidad por la altura del framebuffer, para que a 4K no gire el doble que a 1080p.")]
    private bool _resolutionNormalizedSensitivity = true;

    [Tooltip("Altura de referencia en pixeles. 1080 es el estandar."), EnableIf("_resolutionNormalizedSensitivity")]
    private float _referenceHeight = 1080f;

    [Header("Follow")]
    [Tooltip("Suavizado de la posicion del pivote. Mayor = la camara pisa mas."), Range(5f, 30f)]
    public float FollowSmoothing = 10f;

    [Tooltip("Altura del punto de mira sobre los pies del target.")]
    public float ChestOffset = 1.0f;

    [Header("Zoom")]
    [Tooltip("Zoom con la rueda del raton. Si esta desactivado, la distancia es fija.")]
    private bool _zoomEnabled = true;

    [Tooltip("Distancia minima al hacer zoom in. Es el rango del zoom, no el minimo por colision (MinDistance)."), EnableIf("_zoomEnabled")]
    private float _minZoomDistance = 2f;

    [Tooltip("Distancia maxima al hacer zoom out. Es el rango del zoom, no el minimo por colision (MinDistance)."), EnableIf("_zoomEnabled")]
    private float _maxZoomDistance = 15f;

    [Tooltip("Velocidad del zoom. 1 = un tope de rueda recorre el rango completo."), EnableIf("_zoomEnabled")]
    private float _zoomSpeed = 1f;

    [Header("Collision")]
    [Tooltip("Evita que la camara atraviese paredes y geometria. Acerca la camara automaticamente cuando algo se interpone.")]
    public bool CollisionEnabled = true;

    [Tooltip("Que tan rapido se acerca la camara al obstaculo. Alto = casi instantaneo."), EnableIf("CollisionEnabled")]
    private float _collisionPullInSpeed = 20f;

    [Tooltip("Que tan rapido se aleja la camara cuando el obstaculo desaparece. Bajo = sale despacio."), EnableIf("CollisionEnabled")]
    private float _collisionPushOutSpeed = 5f;

    [Tooltip("Margen que deja la camara respecto al obstaculo."), EnableIf("CollisionEnabled")]
    public float CollisionRadius = 0.2f;

    [Tooltip("Distancia minima a la que la colision puede acercar la camara."), EnableIf("CollisionEnabled")]
    public float MinDistance = 1f;

    [Header("Teleport")]
    [Tooltip("Salta al pivote en vez de volar hasta el cuando el target se teletransporta.")]
    private bool _snapOnTeleport = true;

    [Tooltip("Distancia en un solo frame a partir de la cual se considera teleport."), EnableIf("_snapOnTeleport")]
    private float _teleportThreshold = 5f;

    [Header("Gamepad (optional)")]
    [Tooltip("Permite orbitar la camara con el stick derecho del mando.")]
    private bool _gamepadEnabled = true;

    [Tooltip("Indice del mando. 0 es el primero."), Range(0, 15), EnableIf("_gamepadEnabled")]
    private int _gamepadIndex = 0;

    [Tooltip("Sensibilidad del stick derecho."), EnableIf("_gamepadEnabled")]
    private float _gamepadSensitivity = 2f;

    [Tooltip("Zona muerta del stick. Evita que la camara derive sola."), Range(0f, 0.5f), EnableIf("_gamepadEnabled")]
    private float _gamepadDeadzone = 0.15f;

    private float _yawTarget;
    private float _pitchTarget;
    private float _yaw;
    private float _pitch;
    private Float3 _smoothedTargetPos;
    private Float3 _lastRawPivot;
    private float _currentDistance;
    private bool _hasTargetPos;
    private OrbitMode _lastAppliedMode = (OrbitMode)(-1);

    /// <summary>
    /// Forward horizontal de la camara, en coordenadas de mundo. Es la direccion en la que
    /// se mueve el jugador al pulsar W.
    /// <para>
    /// Se deriva del yaw, no de la posicion, a proposito: leerla de las posiciones
    /// arrastraba el retardo del pivote suavizado (la camara se coloca desde
    /// <c>_smoothedTargetPos</c> pero el punto de mira se calculaba desde
    /// <c>Target.Position</c> crudo), y hacia que se moviera el jugador. Ademas el error
    /// crecia al acercarse la camara por colision, que es justo cuando mas hace falta.
    /// </para>
    /// </summary>
    public Float3 FlatForward { get; private set; }

    /// <summary>
    /// Right horizontal de la camara, en coordenadas de mundo. Es la direccion de D.
    /// Siempre perpendicular a <see cref="FlatForward"/>.
    /// </summary>
    public Float3 FlatRight { get; private set; }

    public override void OnEnable()
    {
        _lastAppliedMode = (OrbitMode)(-1);
        ApplyCursorState();
    }

    public override void OnDisable()
    {
        // Siempre restaurar el cursor al desactivar
        Input.SetCursorVisible(true);
        Input.CursorLockState = CursorLockMode.None;
    }

    public override void LateUpdate()
    {
        if (Target == null || Target.GameObject.IsNotValid()) return;

        // Aplicar estado del cursor segun modo (por si el usuario lo cambia en runtime)
        ApplyCursorState();

        // Si estamos en LockedCursor y el cursor se desbloqueo (ESC), permitir
        // re-capturarlo con click izquierdo dentro de la ventana.
        if (Mode == OrbitMode.LockedCursor
            && Input.CursorLockState != CursorLockMode.Locked
            && Input.GetMouseButtonDown(0))
        {
            Input.SetCursorVisible(false);
            Input.CursorLockState = CursorLockMode.Locked;
        }

        // 1. Leer input y actualizar yaw/pitch
        bool shouldOrbit;
        if (Mode == OrbitMode.LockedCursor)
        {
            // Solo orbitar si el cursor esta efectivamente locked
            shouldOrbit = Input.CursorLockState == CursorLockMode.Locked;
        }
        else
        {
            // HoldRightClick: solo orbitar mientras el boton derecho esta pulsado
            shouldOrbit = Input.GetMouseButton(1);
        }

        // Suavizar la rotacion hacia el objetivo (frame-rate independent)
        float dtRot = Time.DeltaTime;
        float tRot = 1f - MathF.Exp(-RotationSmoothing * dtRot);

        // 1a. Sensibilidad efectiva. El delta del raton viene en pixeles del framebuffer, asi
        // que a 4K se recibe el doble que a 1080p para el mismo movimiento fisico. La guarda
        // height > 0 es obligatoria: con framebuffer 0 (headless, tests, antes de que exista
        // la ventana) esto seria una division por cero y un NaN en _pitchTarget que se lleva
        // la camara lejos para siempre.
        float sens = Sensitivity;
        if (_resolutionNormalizedSensitivity)
        {
            int height = Window.InternalWindow.FramebufferSize.Y;
            if (height > 0)
                sens *= _referenceHeight / height;
        }

        if (shouldOrbit)
        {
            float mult = Mode == OrbitMode.HoldRightClick ? HoldModeSensitivityMultiplier : 1f;
            float pitchSign = _invertY ? -1f : 1f;
            _yawTarget += Input.MouseDelta.X * sens * mult;
            _pitchTarget += pitchSign * Input.MouseDelta.Y * sens * mult;
        }

        // 1a-bis. Gamepad: el stick no necesita un modo de "boton mantenido", asi que va
        // fuera del bloque de shouldOrbit. El eje se integra con dt porque es una velocidad,
        // no un salto: sin el dt la camara se teletransporta al pulsar el stick.
        if (_gamepadEnabled && Input.IsGamepadConnected(_gamepadIndex))
        {
            Float2 stick = Input.GetGamepadRightStick(_gamepadIndex);
            if (Maths.Abs(stick.X) > _gamepadDeadzone || Maths.Abs(stick.Y) > _gamepadDeadzone)
            {
                float pitchSign = _invertY ? -1f : 1f;
                _yawTarget += stick.X * _gamepadSensitivity * dtRot;
                _pitchTarget += pitchSign * stick.Y * _gamepadSensitivity * dtRot;
            }
        }

        // 1a-ter. Zoom con rueda. Fuera del bloque de shouldOrbit a proposito: el zoom
        // tambien tiene que funcionar con el cursor libre. El escalado por Distance lo hace
        // relativo (un tope cerca mueve menos que uno lejos). Distance se muta a proposito,
        // para que el Inspector muestre el valor actual.
        if (_zoomEnabled)
        {
            float wheel = Input.MouseWheelDelta;
            if (wheel != 0f)
            {
                float minZoom = _minZoomDistance;
                float maxZoom = Maths.Max(_minZoomDistance, _maxZoomDistance);
                Distance = Maths.Clamp(
                    Distance - wheel * _zoomSpeed * (Distance * 0.1f),
                    minZoom, maxZoom);
            }
        }

        _pitchTarget = Maths.Clamp(_pitchTarget, MinPitch, MaxPitch);

        // El suavizado va despues de integrar el input, no antes: al reves el objetivo
        // acumula un frame de retardo y la camara se siente pegada.
        _yaw = Maths.Lerp(_yaw, _yawTarget, tRot);
        _pitch = Maths.Lerp(_pitch, _pitchTarget, tRot);

        // 1b. Publicar la base de movimiento del personaje, antes de tocar el pivote.
        // Sale del yaw (lo que la camara dibuja de verdad) y no de _yawTarget, que seria
        // una suavizacion por delante, ni de Transform.Rotation, que al final del frame
        // queda apuntado al lookAt e incluye el pitch.
        PublishMovementBasis();

        // 2. Calcular el pivote (punto alrededor del que orbita la camara)
        Float3 targetPivot = Target.Position + new Float3(0, TargetHeight, 0);

        // 3. Suavizar SOLO el pivote, no la posicion final de la camara
        float dt = Time.DeltaTime;
        if (!_hasTargetPos)
        {
            _smoothedTargetPos = targetPivot;
            _currentDistance = Distance;
            _hasTargetPos = true;
        }
        else if (_snapOnTeleport
            && Float3.Distance(targetPivot, _lastRawPivot) > _teleportThreshold)
        {
            // Teletransporte: saltar en vez de barrer el mapa. Se compara contra el pivote
            // crudo anterior, no contra el suavizado: el suavizado siempre va atrasado, y su
            // retardo crece con la velocidad, asi que un sprint normal lo dispararia.
            _smoothedTargetPos = targetPivot;
            _currentDistance = Distance;
        }
        else
        {
            float t = 1f - MathF.Exp(-FollowSmoothing * dt);
            _smoothedTargetPos = new Float3(
                Maths.Lerp(_smoothedTargetPos.X, targetPivot.X, t),
                Maths.Lerp(_smoothedTargetPos.Y, targetPivot.Y, t),
                Maths.Lerp(_smoothedTargetPos.Z, targetPivot.Z, t)
            );
        }
        _lastRawPivot = targetPivot;

        // 4. Calcular la posicion deseada de la camara a partir del pivote suavizado
        Quaternion rotation = Quaternion.FromEuler(_pitch, _yaw, 0);
        Float3 offsetDir = rotation * Float3.UnitZ;

        // 5a. Resolver la distancia objetivo aplicando la colision
        float targetDistance = Distance;
        if (CollisionEnabled)
        {
            Float3 rayDir = -offsetDir; // desde el pivote hacia la camara
            PhysicsWorld? world = GameObject.IsValid() && GameObject.Scene.IsValid()
                ? GameObject.Scene.Physics
                : null;
            if (world != null
                && world.Raycast(_smoothedTargetPos, rayDir, targetDistance, out RaycastHit hitInfo))
            {
                targetDistance = Maths.Max(hitInfo.Distance - CollisionRadius, MinDistance);
            }
        }

        // 5b. Spring: rapido al acercarse (clipping dentro de una pared es peor que un
        // tiron) y lento al alejarse (evita que la camara salga disparada al pasar un hueco).
        float springSpeed = targetDistance < _currentDistance
            ? _collisionPullInSpeed
            : _collisionPushOutSpeed;
        _currentDistance = Maths.Lerp(
            _currentDistance, targetDistance, 1f - MathF.Exp(-springSpeed * dt));

        // 5c. Reconstruir la posicion con la distancia real
        Float3 desiredPos = _smoothedTargetPos - offsetDir * _currentDistance;

        // 6. Aplicar a la camara
        Transform.Position = desiredPos;

        // 7. Punto de mira: se deriva del pivote suavizado, no de Target.Position crudo.
        // Antes la camara se colocaba desde _smoothedTargetPos pero el lookAt se recalculaba
        // desde el target sin suavizar, y el punto de mira se separaba del personaje
        // mientras este se movia. ChestOffset se mide desde los pies del target y
        // _smoothedTargetPos ya lleva TargetHeight sumado, asi que se resta para llegar al
        // mismo punto del mundo que antes.
        Float3 lookAtPoint = _smoothedTargetPos + new Float3(0, ChestOffset - TargetHeight, 0);
        Float3 toLookAt = Float3.Normalize(lookAtPoint - desiredPos);
        Transform.Rotation = Quaternion.LookRotation(toLookAt, Float3.UnitY);
    }

    /// <summary>
    /// Rellena <see cref="FlatForward"/> y <see cref="FlatRight"/> a partir del yaw actual.
    /// Se separan del bloque de rotacion a proposito: son parte del contrato publico de la
    /// camara hacia el personaje, no un detalle del dibujado.
    /// </summary>
    private void PublishMovementBasis()
    {
        Quaternion flatRot = Quaternion.FromEuler(0f, _yaw, 0f);
        Float3 fwd = flatRot * Float3.UnitZ;
        fwd.Y = 0f;

        // Un yaw puro siempre da un forward aplanado de longitud ~1, asi que este caso es
        // defensivo. Publicar una base neutra, nunca un NaN: se propagaria a la velocidad
        // del personaje y lo perderiamos sin vuelta.
        if (Float3.LengthSquared(fwd) < 1e-6f)
        {
            FlatForward = Float3.UnitZ;
            FlatRight = Float3.UnitX;
            return;
        }

        FlatForward = Float3.Normalize(fwd);
        // Misma convencion que usaba el personaje antes (right = cross(up, forward)):
        // da +X cuando el forward es +Z.
        FlatRight = Float3.Normalize(Float3.Cross(Float3.UnitY, FlatForward));
    }

    private void ApplyCursorState()
    {
        if (Mode == _lastAppliedMode) return;
        _lastAppliedMode = Mode;

        if (Mode == OrbitMode.LockedCursor)
        {
            Input.SetCursorVisible(false);
            Input.CursorLockState = CursorLockMode.Locked;
        }
        else
        {
            Input.SetCursorVisible(true);
            Input.CursorLockState = CursorLockMode.None;
        }
    }

    public override void DrawGizmos()
    {
        if (Target == null || Target.GameObject.IsNotValid()) return;

        Float3 pivot = Target.Position + new Float3(0, TargetHeight, 0);
        Float3 lookAtPoint = Target.Position + new Float3(0, ChestOffset, 0);

        Debug.DrawLine(Target.Position, pivot, Color.Yellow);
        Debug.DrawLine(pivot, Transform.Position, Color.Cyan);
        Debug.DrawLine(lookAtPoint, Transform.Position, Color.Magenta);
    }
}
