using System;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Character/Third Person Character Movement")]
public class ThirdPersonCharacterMovement : MonoBehaviour
{
    public enum ModelFacing
    {
        Forward_Z,      // offset 0
        Back_NegativeZ, // offset 180
        Right_X,        // offset 90
        Left_NegativeX  // offset -90
    }

    [Header("References")]
    public OrbitFollowCamera Camera;
    public CharacterController Controller;

    [Header("Feel")]
    public float Acceleration = 25f;
    public float Deceleration = 30f;

    [Header("Movement")]
    public float WalkSpeed = 5f;
    public float RunSpeed = 8f;
    public float TurnSpeed = 12f;

    [Header("Model")]
    public Transform ModelRoot;
    public ModelFacing Facing = ModelFacing.Forward_Z;

    [Header("Orientation")]
    public bool StrafeMode = false;

    [Header("Input")]
    public float MovementThreshold = 0.05f;

    [Header("Gravity")]
    public float Gravity = -20f;
    public float JumpForce = 8f;
    private float _verticalVelocity;
    private Float3 _currentHorizontalVelocity = Float3.Zero;

    public override void Update()
    {
        if (Camera == null || Controller == null) return;

        float inputX = 0f;
        float inputY = 0f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Left)) inputX -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Right)) inputX += 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Up)) inputY += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.Down)) inputY -= 1f;

        // Forward para el jugador = direccion desde la camara hacia el target.
        // Se calcula directamente porque Camera.Transform.Forward puede devolver
        // -Z (convencion OpenGL) y no es de fiar para esto.
        Float3 camToTarget = Camera.Target.Position - Camera.Transform.Position;
        camToTarget.Y = 0;
        Float3 flatF = Float3.Normalize(camToTarget);

        // Right = cross(Up, Forward) para obtener +X cuando flatF = +Z.
        Float3 flatR = Float3.Normalize(Float3.Cross(Float3.UnitY, flatF));

        Float3 moveDir = flatF * inputY + flatR * inputX;
        float mag = Float3.Length(moveDir);

        // Guardar si estaba grounded ANTES de este move
        bool wasGrounded = Controller.IsGrounded;

        if (wasGrounded)
        {
            // Pegado al suelo con una velocidad muy pequena (evita penetrar)
            if (_verticalVelocity < 0f)
                _verticalVelocity = -0.5f;
        }
        else
        {
            _verticalVelocity += Gravity * Time.DeltaTime;
            if (_verticalVelocity < -50f)
                _verticalVelocity = -50f;
        }

        Float3 targetVelocity = Float3.Zero;
        if (mag > MovementThreshold)
        {
            moveDir = Float3.Normalize(moveDir);
            targetVelocity = moveDir * WalkSpeed;
        }

        float dtMove = Time.DeltaTime;
        float accelRate = targetVelocity == Float3.Zero ? Deceleration : Acceleration;
        float tVel = 1f - MathF.Exp(-accelRate * dtMove);
        _currentHorizontalVelocity = new Float3(
            Maths.Lerp(_currentHorizontalVelocity.X, targetVelocity.X, tVel),
            Maths.Lerp(_currentHorizontalVelocity.Y, targetVelocity.Y, tVel),
            Maths.Lerp(_currentHorizontalVelocity.Z, targetVelocity.Z, tVel)
        );

        Float3 horizontalMotion = _currentHorizontalVelocity * dtMove;

        // Clampear el desplazamiento vertical para evitar penetracion
        float verticalStep = _verticalVelocity * Time.DeltaTime;
        float maxVerticalStep = 0.5f;
        if (verticalStep < -maxVerticalStep) verticalStep = -maxVerticalStep;
        if (verticalStep > maxVerticalStep) verticalStep = maxVerticalStep;
        Float3 verticalMotion = new Float3(0, verticalStep, 0);


        Controller.Move(horizontalMotion + verticalMotion);

        // Solo rotar el modelo cuando hay componente forward en el input.
        // Puro A/D (strafe) o puro S (backpedal) no cambian la orientacion.
        // W, W+A, W+D, W+S(no aplica) si rotan.
        bool shouldRotate;
        if (StrafeMode)
        {
            // En Strafe el personaje siempre mira hacia donde mira la camara
            shouldRotate = true;
        }
        else
        {
            // En FaceMovement solo rotar si hay input forward o diagonales con forward
            shouldRotate = inputY > 0.01f;
        }

        if (shouldRotate)
        {
            Float3 lookDir = StrafeMode ? flatF : moveDir;

            Quaternion targetRotation = Quaternion.LookRotation(lookDir, Float3.UnitY);

            float offsetDeg = Facing switch
            {
                ModelFacing.Forward_Z => 0f,
                ModelFacing.Back_NegativeZ => 180f,
                ModelFacing.Right_X => 90f,
                ModelFacing.Left_NegativeX => -90f,
                _ => 0f
            };
            targetRotation = targetRotation * Quaternion.FromEuler(new Float3(0, offsetDeg, 0));

            Transform model = ModelRoot != null ? ModelRoot : Transform;
            model.Rotation = Quaternion.Slerp(
                model.Rotation, targetRotation, TurnSpeed * Time.DeltaTime);
        }
    }

    public override void DrawGizmos()
    {
        Transform model = ModelRoot != null ? ModelRoot : Transform;
        Float3 pos = model.Position;
        Float3 forward = model.Rotation * Float3.UnitZ;
        Float3 right = model.Rotation * Float3.UnitX;
        Float3 up = model.Rotation * Float3.UnitY;
        Debug.DrawLine(pos, pos + forward * 1.5f, Color.Blue);   // frente
        Debug.DrawLine(pos, pos + right * 1.5f, Color.Red);      // derecha
        Debug.DrawLine(pos, pos + up * 1.5f, Color.Green);       // arriba
    }
}
