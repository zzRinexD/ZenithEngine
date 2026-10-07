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

    [Header("Turning")]
    [SerializeField, Tooltip("Permite una tasa de giro distinta de la aceleracion al cambiar de direccion.")]
    private bool _turnRateEnabled = false;

    [SerializeField, Tooltip("Tasa usada al cambiar de sentido. Mayor = gira mas rapido."), EnableIf("_turnRateEnabled")]
    private float _turnRate = 20f;

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

        // Base de movimiento publicada por la camara. Antes se derivaba de
        // Camera.Target.Position - Camera.Transform.Position, lo que mezclaba el pivote
        // suavizado con el target crudo y hacia que "adelante" se retorciera hacia donde
        // nos moviamos, sobre todo con la camara cerca por colision.
        Float3 flatF = Camera.FlatForward;
        Float3 flatR = Camera.FlatRight;

        // Base degenerada: no mover. Un NaN aqui se propaga a la velocidad y a la posicion,
        // y el personaje desaparece sin recuperacion. Un forward de longitud ~0 tampoco
        // significa nada util.
        if (Float3.LengthSquared(flatF) < 1e-6f || Float3.LengthSquared(flatR) < 1e-6f)
            return;

        Float3 moveDir = flatF * inputY + flatR * inputX;
        float mag = Float3.Length(moveDir);

        // Guardar si estaba grounded ANTES de este move
        bool wasGrounded = Controller.IsGrounded;

        // Salto. Se decide con el grounded de PRINCIPIO de frame (la linea de arriba), no
        // con IsGrounded actual: CharacterController.Move lo actualiza al final de su
        // llamada, asi que leerlo aqui permitiria un segundo salto en el mismo frame.
        if (wasGrounded && Input.GetKeyDown(KeyCode.Space))
        {
            _verticalVelocity = JumpForce;
            wasGrounded = false;
        }

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
            bool wantsRun = Input.IsShiftPressed;
            targetVelocity = moveDir * (wantsRun ? RunSpeed : WalkSpeed);
        }

        float dtMove = Time.DeltaTime;

        // Elegir la tasa por si estamos frenando o acelerando, no por si el objetivo es
        // cero. Antes, soltar Shift (8 -> 5) frenaba con Acceleration, igual que arrancar
        // desde el reposo: la misma accion con dos tasas distintas segun cuanto se frena,
        // y sin ningun campo para ajustarlo.
        float currentSpeed = Float3.Length(_currentHorizontalVelocity);
        float targetSpeed = Float3.Length(targetVelocity);
        float accelRate = targetSpeed < currentSpeed ? Deceleration : Acceleration;

        if (_turnRateEnabled
            && currentSpeed > 0.01f
            && Float3.Dot(_currentHorizontalVelocity, targetVelocity) < 0f)
        {
            // Giro de verdad, no un simple cambio de diagonal: el Dot solo se hace
            // negativo cuando el objetivo apunta al lado contrario del movimiento.
            accelRate = _turnRate;
        }

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

        // Solo rotar el modelo cuando hay input de movimiento.
        // En Strafe el personaje siempre mira hacia donde mira la camara.
        // En FaceMovement rota con cualquier componente, para que el puro A/D no produzca
        // un moonwalk lateral (antes solo giraba con input vertical y el personaje se
        // deslizaba de lado mirando al frente). S pura sigue sin rotar: backpedal.
        bool shouldRotate;
        if (StrafeMode)
        {
            shouldRotate = true;
        }
        else
        {
            shouldRotate = inputY > 0.01f || inputX > 0.01f;
        }

        if (shouldRotate)
        {
            Float3 lookDir = StrafeMode ? flatF : moveDir;

            // Normalizar antes de mirar: moveDir solo se normaliza mas arriba cuando supera
            // MovementThreshold, asi que con input analogico (gamepad) puede llegar aqui sin
            // normalizar y LookRotation sobre un vector diminuto no da una orientacion fiable.
            Float3 lookTarget = Float3.Normalize(lookDir);
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget, Float3.UnitY);

            float offsetDeg = Facing switch
            {
                ModelFacing.Forward_Z => 0f,
                ModelFacing.Back_NegativeZ => 180f,
                ModelFacing.Right_X => 90f,
                ModelFacing.Left_NegativeX => -90f,
                _ => 0f
            };
            // Post-multiplicar por un giro sobre el Y local equivale a uno sobre el Y del
            // mundo porque lookTarget siempre tiene Y = 0, o sea que el LookRotation de
            // arriba nunca lleva pitch.
            targetRotation = targetRotation * Quaternion.FromEuler(new Float3(0, offsetDeg, 0));

            Transform model = ModelRoot != null ? ModelRoot : Transform;

            // Damping exponencial: mismo patron que la camara y que la aceleracion horizontal
            // de este componente. TurnSpeed * DeltaTime a secas es una interpolacion lineal,
            // que depende del framerate (0.4 a 30 FPS contra 0.083 a 144 FPS). El clamp
            // cubre un TurnSpeed negativo puesto a mano desde el Inspector: sin el, Slerp
            // extrapolaria en vez de interpolar.
            float tTurn = Maths.Clamp(1f - MathF.Exp(-TurnSpeed * Time.DeltaTime), 0f, 1f);
            model.Rotation = Quaternion.Slerp(model.Rotation, targetRotation, tTurn);
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
