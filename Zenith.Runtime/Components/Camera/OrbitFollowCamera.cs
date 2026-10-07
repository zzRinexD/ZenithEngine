using System;
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
    public Transform Target;
    public float TargetHeight = 1.5f;

    [Header("Orbit")]
    public OrbitMode Mode = OrbitMode.LockedCursor;
    public float Distance = 6f;
    public float Sensitivity = 0.13f;
    public float HoldModeSensitivityMultiplier = 1.8f;
    public float RotationSmoothing = 18f;
    public float MinPitch = -36f;
    public float MaxPitch = 18f;

    [Header("Follow")]
    public float FollowSmoothing = 10f;
    public float ChestOffset = 1.0f;

    [Header("Collision")]
    public bool CollisionEnabled = false;
    public float CollisionRadius = 0.2f;
    public float MinDistance = 1f;

    private float _yawTarget;
    private float _pitchTarget;
    private float _yaw;
    private float _pitch;
    private Float3 _smoothedTargetPos;
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
        if (shouldOrbit)
        {
            float mult = Mode == OrbitMode.HoldRightClick ? HoldModeSensitivityMultiplier : 1f;
            _yawTarget += Input.MouseDelta.X * Sensitivity * mult;
            _pitchTarget += Input.MouseDelta.Y * Sensitivity * mult;
        }
        _pitchTarget = Maths.Clamp(_pitchTarget, MinPitch, MaxPitch);

        // Suavizar la rotacion hacia el objetivo (frame-rate independent)
        float dtRot = Time.DeltaTime;
        float tRot = 1f - MathF.Exp(-RotationSmoothing * dtRot);
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
            _hasTargetPos = true;
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

        // 4. Calcular la posicion deseada de la camara a partir del pivote suavizado
        Quaternion rotation = Quaternion.FromEuler(_pitch, _yaw, 0);
        Float3 offsetDir = rotation * Float3.UnitZ;
        Float3 desiredPos = _smoothedTargetPos - offsetDir * Distance;

        // 5. Colision opcional
        if (CollisionEnabled)
        {
            Float3 rayDir = -offsetDir; // desde el pivote hacia la camara
            PhysicsWorld? world = GameObject.IsValid() && GameObject.Scene.IsValid()
                ? GameObject.Scene.Physics
                : null;
            if (world != null)
            {
                if (world.Raycast(_smoothedTargetPos, rayDir, Distance, out RaycastHit hitInfo))
                {
                    float d = Maths.Max(hitInfo.Distance - CollisionRadius, MinDistance);
                    desiredPos = _smoothedTargetPos + rayDir * d;
                }
            }
        }

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
