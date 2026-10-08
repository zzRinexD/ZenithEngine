using System;
using System.Collections.Generic;
using System.Linq;
using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Movimiento de personaje en tercera persona sobre un <see cref="CharacterController"/>.
///
/// Contrato con la camara: el movimiento SIEMPRE se interpreta en el marco de la camara
/// (<see cref="OrbitFollowCamera"/>). "Adelante" es hacia donde mira la camara, sin excepcion.
/// Lo que hace <see cref="StrafeMode"/> no es cambiar la direccion del movimiento, sino elegir
/// hacia donde apunta el cuerpo: con strafe mira a donde mira la camara; sin strafe gira hacia
/// donde se mueve. Esa distincion es deliberada y las dos rutas comparten la misma velocidad.
///
/// La orientacion del cuerpo se compone sobre la rotacion local base del modelo, que se
/// captura al activarse y nunca se sobrescribe: un mesh importado mirando a -Z sobrevive.
/// <see cref="Facing"/> se SUMA a esa base.
/// </summary>
[AddComponentMenu("Character/Third Person Character Movement")]
public class ThirdPersonCharacterMovement : MonoBehaviour
{
    /// <summary>Correccion extra sobre el frente, para mallas cuyo forward no coincide con +Z.</summary>
    public enum ModelFacing
    {
        Forward_Z,       // +0
        Right_X,         // +90
        Back_NegativeZ,  // +180
        Left_NegativeX,  // -90
    }

    [Header("References")]
    [Tooltip("Camara de la que se toma el marco de movimiento. Debe ser una OrbitFollowCamera: es la que publica FlatForward y FlatRight. Sin ella el componente no se mueve.")]
    public OrbitFollowCamera Camera;

    [Tooltip("CharacterController que aplica el desplazamiento y resuelve la colision. Si se deja vacio se toma el del mismo GameObject.")]
    public CharacterController Controller;

    [Header("Speed")]
    [Tooltip("Velocidad horizontal andando, en unidades/segundo.")]
    public float WalkSpeed = 5f;

    [Tooltip("Velocidad horizontal con Shift, en unidades/segundo. Shift izquierdo o derecho.")]
    public float RunSpeed = 8f;

    [Header("Acceleration")]
    [Tooltip("Rapidez con la que se alcanza la velocidad objetivo. Mayor = arranca mas de golpe.")]
    public float Acceleration = 25f;

    [Tooltip("Rapidez con la que se pierde la velocidad al soltar el input. Mayor = frena mas rapido.")]
    public float Deceleration = 30f;

    [Header("Gravity & Jump")]
    [Tooltip("Aceleracion vertical, en unidades/s2. Negativo.")]
    public float Gravity = -20f;

    [Tooltip("Velocidad vertical inicial del salto, en unidades/segundo. 8 con Gravity = -20 sube ~1.6 m.")]
    public float JumpForce = 8f;

    [Tooltip("Velocidad vertical hacia abajo a la que se pega al suelo. Evita que el personaje cabecee contra el terreno.")]
    public float GroundedStickSpeed = -2f;

    [Tooltip("Limite inferior de la velocidad vertical, en unidades/segundo. Corta la caida en رحلة larga.")]
    public float MaxFallSpeed = -50f;

    [Header("Orientation")]
    [Tooltip("Con strafe el cuerpo mira siempre hacia donde mira la camara. Sin strafe gira hacia donde se mueve, y se queda quieto si no hay input.")]
    public bool StrafeMode = false;

    [Tooltip("Rapidez con la que el cuerpo gira hacia su objetivo de orientacion, en unidades de amortiguacion. Mayor = gira mas de golpe.")]
    public float TurnSpeed = 12f;

    [Header("Model")]
    [Tooltip("Transform del cuerpo que rota. Si se deja vacio, rota el GameObject entero. Su rotacion local se captura al activarse y se conserva: el componente compone el giro encima, no la borra.")]
    public Transform ModelRoot;

    [Tooltip("Correccion de orientacion adicional, en grados. Se SUMA a la rotacion local base del ModelRoot. Deja esto en Forward_Z si ya corregiste la orientacion de la malla al importarla.")]
    public ModelFacing Facing = ModelFacing.Forward_Z;

    [Header("Input")]
    [Tooltip("Entrada minima para considerar que hay movimiento. Con teclado el valor solo puede ser 0, 1 o diagonal, asi que cualquier umbral entre 0 y 1 se comporta igual.")]
    public float MovementThreshold = 0.05f;

    [Header("Diagnostics")]
    [Tooltip("Escribe en la consola, una vez por segundo mientras hay input, los numeros reales de este frame: posicion y forward de la camara, posicion del cuerpo, FlatForward y velocidad. Sirve para separar un problema de fisica (la velocidad no se aplica) de uno de base (la velocidad es correcta pero la camara que se ve no es la que la publica).")]
    public bool LogMovementDiagnostics = false;

    [Header("Turning (optional)")]
    [SerializeField, Tooltip("Permite una tasa distinta al cambiar de sentido de marcha.")]
    private bool _turnRateEnabled = false;

    [SerializeField, Tooltip("Tasa usada al cambiar de sentido. Mayor = gira mas rapido."), EnableIf("_turnRateEnabled")]
    private float _turnRate = 20f;

    private float _verticalVelocity;
    private Float3 _velocity = Float3.Zero;

    /// <summary>Rotacion local del modelo tal y como la dejo el autor. Se compone, no se reemplaza.</summary>
    private Quaternion _baseLocalRotation = Quaternion.Identity;

    /// <summary>Transform del que se capturo la base, para recapturar si <see cref="ModelRoot"/> cambia.</summary>
    private Transform _baseCapturedFrom;
    private bool _baseCaptured;

    /// <summary>
    /// El transform que gira. <see cref="ModelRoot"/> si esta asignado; el GameObject entero si no.
    /// </summary>
    private Transform Body => ModelRoot != null ? ModelRoot : Transform;

    private CharacterController Phys
    {
        get
        {
            if (Controller == null) Controller = GetComponent<CharacterController>();
            return Controller;
        }
    }

    public override void OnEnable()
    {
        // Un ciclo desactivar/activar NO debe recapturar: el transform ya estaria girado por
        // este componente y tomarlo como base sumaria el giro anterior una vez mas.
        CaptureBaseRotation();
    }

    /// <summary>
    /// Fija la rotacion local base del cuerpo, y avisa si no es identidad.
    /// <para>
    /// El aviso existe porque <see cref="Facing"/> se suma a esta base en lugar de reemplazarla:
    /// quien tenga las dos activas necesita saberlo, o creera que su ajuste se ha losto.
    /// </para>
    /// </summary>
    private void CaptureBaseRotation()
    {
        Transform body = Body;

        _baseCapturedFrom = body;
        _baseLocalRotation = body.LocalRotation;
        _baseCaptured = true;

        // q y -q son la misma rotacion: una base de 360 grados es identidad y no debe avisar.
        if (MathF.Abs(Quaternion.Dot(_baseLocalRotation, Quaternion.Identity)) < 0.9999f)
        {
            Debug.LogWarning(
                $"[{Name}] ModelRoot tiene rotacion local ({_baseLocalRotation.EulerAngles}). " +
                "ThirdPersonCharacterMovement la conserva y gira encima; Facing se SUMA a ella. " +
                "Si estabas usando Facing para compensar el sentido de la malla, deja Facing en " +
                "Forward_Z y corrige la rotacion en el ModelRoot: con las dos, el ajuste sale doblado.");
        }
    }

    public override void Update()
    {
        CharacterController phys = Phys;
        if (Camera == null || phys == null) return;

        float dt = Time.DeltaTime;
        if (dt <= 0f) return;

        // El ModelRoot pudo asignarse despues de OnEnable, o reasignarse en runtime: en ambos
        // casos hay que volver a fijar la base, pero nunca sobre un transform ya girado.
        if (!_baseCaptured || _baseCapturedFrom != Body)
            CaptureBaseRotation();

        ReadMovementInput(out float forward, out float strafe);

        // Marco de la camara. "Adelante" es donde mira la camara; no hay ningun otro marco.
        Float3 camForward = Camera.FlatForward;
        Float3 camRight = Camera.FlatRight;

        // Base degenerada: no mover. Un NaN aqui viaja por la velocidad hasta la posicion y
        // el personaje desaparece sin recuperacion.
        if (IsDegenerate(camForward) || IsDegenerate(camRight))
            return;

        Float3 wishDirection = camForward * forward + camRight * strafe;
        float wishMagnitude = Float3.Length(wishDirection);

        bool hasInput = wishMagnitude > MovementThreshold;
        if (hasInput) wishDirection /= wishMagnitude;

        UpdateVelocity(wishDirection, hasInput, dt);
        UpdateVerticalVelocity(dt);

        phys.Move(_velocity * dt + VerticalStep(dt));

        UpdateOrientation(wishDirection, hasInput, dt);

        if (LogMovementDiagnostics && (hasInput || Float3.LengthSquared(_velocity) > 1f))
            LogDiagnostics(hasInput);
    }

    private float _lastDiagnosticTime = -999f;

    /// <summary>
    /// Vuelca los numeros de este frame a la consola, como mucho una vez por segundo.
    /// <para>
    /// Es la forma de cerrar el bucle sin suposiciones: si "dot(bodyVel, camFwd)" es negativo
    /// con S, el cuerpo avanza hacia donde mira la camara y el problema es de fisica o de que la
    /// camara que se ve no es la que publica la base. Si es positivo, el cuerpo retrocede bien y
    /// lo que se percibe viene de otra parte.
    /// </para>
    /// </summary>
    private void LogDiagnostics(bool hasInput)
    {
        if (Time.TimeSinceStartup - _lastDiagnosticTime < 1f) return;
        _lastDiagnosticTime = Time.TimeSinceStartup;

        Float3 camPos = Camera.Transform.Position;
        Float3 bodyPos = Transform.Position;

        // De la camara al cuerpo, en horizontal. Positivo = la camara esta detras del cuerpo.
        Float3 toBody = Flatten(bodyPos - camPos);
        Float3 camForward = Flatten(Camera.FlatForward);

        Debug.Log(
            $"[diag] input={(hasInput ? "si" : "no")} " +
            $"camPos={Round(camPos)} bodyPos={Round(bodyPos)} " +
            $"camFlatForward={Round(camForward)} " +
            $"camToBody={Round(toBody)} camaraDetras={(Float3.Dot(toBody, camForward) > 0f ? "si" : "NO")} " +
            $"vel={Round(_velocity)} dot(vel,camFwd)={Float3.Dot(Flatten(_velocity), camForward):F2} " +
            $"bodyFwd={Round(Flatten(Transform.Rotation * Float3.UnitZ))} " +
            $"strafe={StrafeMode}",
            LogSeverity.Normal);

        // Que camara se esta viendo de verdad. El motor renderiza todas las de la escena, asi
        // que puede haber una mirando al personaje que no sea la que publica la base.
        List<Camera> renderers = Camera.GameObject.Scene.GatherActiveCameras();
        string seen = renderers.Count == 0
            ? "NINGUNA camara en la escena"
            : string.Join(" | ", renderers.Select(c =>
                $"'{c.GameObject.Name}'@{(c.GameObject == Camera.GameObject ? "orbit" : "OTRA")}" +
                $" pos={Round(c.Transform.Position)} fwd={Round(Flatten(c.Transform.Forward))}"));
        Debug.Log($"[diag] camaras que renderizan ({renderers.Count}): {seen}", LogSeverity.Normal);
    }

    /// <summary>Redondea a dos decimales: la consola no necesita mas y asi los numeros se comparan a ojo.</summary>
    private static Float3 Round(Float3 v) =>
        new Float3(
            (float)Math.Round(v.X, 2),
            (float)Math.Round(v.Y, 2),
            (float)Math.Round(v.Z, 2));

    /// <summary>Proyecta al plano del suelo. Un eje Y residual haria las comparaciones inutiles.</summary>
    private static Float3 Flatten(Float3 v)
    {
        v.Y = 0f;
        return IsDegenerate(v) ? Float3.Zero : Float3.Normalize(v);
    }

    /// <summary>Un vector de longitud casi cero no es una direccion utilizable.</summary>
    private static bool IsDegenerate(Float3 v) => Float3.LengthSquared(v) < 1e-6f;

    /// <summary>Teclas de movimiento, en el marco de la camara. Valores -1, 0 o 1.</summary>
    private static void ReadMovementInput(out float forward, out float strafe)
    {
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Up)) forward = 1f; else forward = 0f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.Down)) forward -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Right)) strafe = 1f; else strafe = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Left)) strafe -= 1f;
    }

    /// <summary>
    /// Acerca la velocidad horizontal a la deseada. La tasa depende de si hay que ganar o
    /// perder velocidad, no de si el objetivo es cero: soltar el input y soltar Shift son
    /// dos frenadas distintas, y ambas usan <see cref="Deceleration"/>.
    /// </summary>
    private void UpdateVelocity(Float3 wishDirection, bool hasInput, float dt)
    {
        float speed = hasInput
            ? (Input.IsShiftPressed ? RunSpeed : WalkSpeed)
            : 0f;

        Float3 target = wishDirection * speed;
        float currentSpeed = Float3.Length(_velocity);
        float rate = speed < currentSpeed ? Deceleration : Acceleration;

        if (_turnRateEnabled && currentSpeed > 0.01f && Float3.Dot(_velocity, target) < 0f)
            rate = _turnRate;

        // 1 - exp(-k*dt): el mismo peso por frame a 30 y a 144 FPS. Un k*dt a secas haria que
        // un frame de 30 FPS girase 0.4 y uno de 144 solo 0.083.
        float t = 1f - MathF.Exp(-rate * dt);
        _velocity = new Float3(
            Maths.Lerp(_velocity.X, target.X, t),
            Maths.Lerp(_velocity.Y, target.Y, t),
            Maths.Lerp(_velocity.Z, target.Z, t));
    }

    /// <summary>
    /// Gravedad, salto y pegado al suelo.
    /// <para>
    /// El estado de suelo se lee ANTES de mover: <see cref="CharacterController.Move"/> lo
    /// actualiza al final de su llamada, asi que leerlo despues permitiria un segundo salto en
    /// el mismo frame.
    /// </para>
    /// </summary>
    private void UpdateVerticalVelocity(float dt)
    {
        CharacterController phys = Phys;

        if (phys.IsGrounded)
        {
            if (Input.GetKeyDown(KeyCode.Space))
                _verticalVelocity = JumpForce;
            else if (_verticalVelocity < 0f)
                _verticalVelocity = GroundedStickSpeed;
        }
        else
        {
            _verticalVelocity = MathF.Max(_verticalVelocity + Gravity * dt, MaxFallSpeed);
        }
    }

    /// <summary>
    /// El desplazamiento vertical de este frame, acotado. Sin el tope, un dt grande o una caida
    /// larga meteen la capsula entera en el suelo en un solo paso.
    /// </summary>
    private Float3 VerticalStep(float dt)
    {
        float maxStep = 0.5f;
        float step = Math.Clamp(_verticalVelocity * dt, -maxStep, maxStep);
        return new Float3(0f, step, 0f);
    }

    /// <summary>
    /// Gira el cuerpo hacia donde toca, y lo hace en espacio LOCAL sobre la rotacion base.
    /// <para>
    /// Con strafe el objetivo es la camara. Sin strafe es la direccion de movimiento, y si no
    /// hay input el cuerpo se queda como esta: girar hacia el ultimo heading es "congelarse", no
    /// "quedarse quieto".
    /// </para>
    /// <para>
    /// Todo se compone en local porque <see cref="Transform.Rotation"/> es mundial: escribir ahi
    /// un objetivo derivado solo de la mirada borra cualquier rotacion base que el autor haya
    /// puesto en el modelo, y esa base es justamente lo que corrige el sentido de la malla.
    /// </para>
    /// </summary>
    private void UpdateOrientation(Float3 wishDirection, bool hasInput, float dt)
    {
        if (!StrafeMode && !hasInput) return;

        Float3 lookDirection = StrafeMode ? Camera.FlatForward : wishDirection;
        if (IsDegenerate(lookDirection)) return;

        // El cuerpo no debe inclinarse: el objetivo vive en el plano del suelo.
        Float3 flat = new Float3(lookDirection.X, 0f, lookDirection.Z);
        if (IsDegenerate(flat)) return;
        flat = Float3.Normalize(flat);

        Quaternion facing = Quaternion.LookRotation(flat, Float3.UnitY);
        facing = facing * Quaternion.FromEuler(new Float3(0f, FacingOffsetDegrees(), 0f));

        Transform body = Body;
        Quaternion parentRotation = body.Parent != null
            ? body.Parent.Rotation
            : Quaternion.Identity;

        // mundo = padre * local, luego local = padre^-1 * mundo. Resolverlo aqui deja el
        // objetivo en el mismo espacio en el que se interpola, que es el local.
        Quaternion localTarget =
            Quaternion.Inverse(parentRotation) * facing * _baseLocalRotation;

        // El clamp cubre un TurnSpeed negativo puesto a mano: sin el, Slerp extrapolaria.
        float t = Math.Clamp(1f - MathF.Exp(-TurnSpeed * dt), 0f, 1f);
        body.LocalRotation = Quaternion.Slerp(body.LocalRotation, localTarget, t);
    }

    private float FacingOffsetDegrees() => Facing switch
    {
        ModelFacing.Forward_Z => 0f,
        ModelFacing.Right_X => 90f,
        ModelFacing.Back_NegativeZ => 180f,
        ModelFacing.Left_NegativeX => -90f,
        _ => 0f,
    };

    public override void DrawGizmos()
    {
        Transform body = Body;
        Float3 origin = body.Position;

        // Ejes del cuerpo: azul su frente, rojo su derecha. Comparar la linea azul con el
        // desplazamiento real es como se distingue un problema de rotacion de uno de fisica.
        Debug.DrawLine(origin, origin + body.Rotation * Float3.UnitZ * 1.5f, Color.Blue);
        Debug.DrawLine(origin, origin + body.Rotation * Float3.UnitX * 1.5f, Color.Red);
        Debug.DrawLine(origin, origin + body.Rotation * Float3.UnitY * 1.5f, Color.Green);

        // Flecha de la velocidad: si no coincide con el frente, el cuerpo va de lado.
        if (Float3.LengthSquared(_velocity) > 0.01f)
        {
            Float3 tip = origin + _velocity * 0.15f;
            Debug.DrawLine(origin, tip, Color.Yellow);
        }

        if (Camera != null && !IsDegenerate(Camera.FlatForward))
        {
            // Magenta: el "adelante" de la camara, que es el marco en el que se mueve el
            // personaje. El frente del cuerpo (azul) debe apuntar al moving direction en
            // non-strafe, y a esta linea en strafe.
            Float3 marker = body.Position + new Float3(0f, 2.2f, 0f);
            Debug.DrawLine(marker, marker + Camera.FlatForward * 2f, Color.Magenta);
        }
    }
}