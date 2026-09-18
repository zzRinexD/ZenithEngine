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

    [Header("Movement")]
    public float WalkSpeed = 5f;
    public float RunSpeed = 8f;
    public float TurnSpeed = 6f;

    [Header("Model")]
    public Transform ModelRoot;
    public ModelFacing Facing = ModelFacing.Forward_Z;

    [Header("Input")]
    public float MovementThreshold = 0.05f;

    [Header("Gravity")]
    public float Gravity = -20f;
    public float JumpForce = 8f;
    private float _verticalVelocity;

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

        // Gravedad: solo acumular cuando NO esta grounded
        if (Controller.IsGrounded)
        {
            // Pegado al suelo: velocidad vertical pequena y negativa
            if (_verticalVelocity < 0f)
                _verticalVelocity = -2f;
        }
        else
        {
            _verticalVelocity += Gravity * Time.DeltaTime;
            // Terminal velocity: limitar la caida para no atravesar el suelo
            if (_verticalVelocity < -50f)
                _verticalVelocity = -50f;
        }

        Float3 horizontalMotion = Float3.Zero;
        if (mag > MovementThreshold)
        {
            moveDir = Float3.Normalize(moveDir);
            horizontalMotion = moveDir * WalkSpeed * Time.DeltaTime;
        }

        Float3 verticalMotion = new Float3(0, _verticalVelocity * Time.DeltaTime, 0);
        Controller.Move(horizontalMotion + verticalMotion);

        if (mag > MovementThreshold)
        {
            Float3 lookDir = moveDir;
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
        if (ModelRoot == null) return;
        Float3 pos = ModelRoot.Position;
        Float3 forward = ModelRoot.Rotation * Float3.UnitZ;
        Debug.DrawLine(pos, pos + forward * 1.5f, Color.Blue);
    }
}
