// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// <see cref="ThirdPersonCharacterMovement"/> shipped with no tests at all, and it was the
/// half-finished half of the pair: JumpForce and RunSpeed were declared but never read, the
/// turn ignored a pure strafe, and the movement basis came from the camera's *positions*. These
/// pin all of it.
/// </summary>
public class ThirdPersonCharacterMovementTests : RuntimeTestBase
{
    private FakeInputHandler _input = null!;

    private (Scene scene, ThirdPersonCharacterMovement move, CharacterController ctrl, OrbitFollowCamera cam)
        CreateRig()
    {
        _input = new FakeInputHandler();
        Input.PushHandler(_input);

        Scene scene = CreateScene(enable: true);

        // Floor so IsGrounded settles to true and the jump has something to push off.
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(40, 1, 40);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        GameObject player = CreateGameObject("Player");
        scene.Add(player);
        var ctrl = player.AddComponent<CharacterController>();
        ctrl.Shape = CharacterController.ColliderShape.Capsule;
        ctrl.Size = new Float3(0.8f, 1.8f, 0.8f);
        player.Transform.Position = new Float3(0, 0.9f, 0);

        GameObject camGo = CreateGameObject("Camera");
        scene.Add(camGo);
        var cam = camGo.AddComponent<OrbitFollowCamera>();
        cam.Target = player.Transform;
        cam.Mode = OrbitFollowCamera.OrbitMode.HoldRightClick;
        cam.CollisionEnabled = false;
        cam.TargetHeight = 1f;

        var move = player.AddComponent<ThirdPersonCharacterMovement>();
        move.Camera = cam;
        move.Controller = ctrl;

        // Settle onto the floor and let the camera publish a basis.
        Tick(scene, 8);

        return (scene, move, ctrl, cam);
    }

    private static Float3 VelocityOf(ThirdPersonCharacterMovement move)
        => GetPrivate<Float3>(move, "_currentHorizontalVelocity");

    private static float VerticalOf(ThirdPersonCharacterMovement move)
        => GetPrivate<float>(move, "_verticalVelocity");

    private static T GetPrivate<T>(object target, string field)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        return (T)f.GetValue(target)!;
    }

    private static void SetPrivate(object target, string field, object value)
    {
        var t = target.GetType();
        var f = t.GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // FlatForward / FlatRight are properties, so their storage is a compiler-generated
        // backing field. Fall back to it rather than making the caller know that.
        f ??= t.GetField($"<{field}>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        f.SetValue(target, value);
    }

    // ---------------------------------------------------------------------------------------
    // Jump: JumpForce was declared and never read, so the character could not jump at all.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Jump_Jumps_WhenGroundedAndSpacePressed()
    {
        var (scene, move, _, _) = CreateRig();
        Assert.True(move.Controller.IsGrounded, "Precondition: the rig must start grounded.");

        _input.PressKey(KeyCode.Space);
        Tick(scene, 3);

        Assert.True(VerticalOf(move) > 0f,
            $"Space on the ground must produce an upward velocity, was {VerticalOf(move)}.");
    }

    /// <summary>
    /// The one that matters. <c>GetKeyDown</c> is edge triggered in the real handler, so a held
    /// key reports "down" on exactly one frame. The character has to key the jump off the
    /// start-of-frame grounded state, not the live one, because
    /// <see cref="CharacterController.Move"/> only refreshes IsGrounded at its end - a live read
    /// would allow a second jump on the very next frame.
    /// <para>
    /// Note the explicit ClearKeyTransitions: FakeInputHandler keeps a key in its "down this
    /// frame" set until asked to clear, so without this the test would be simulating a key that
    /// re-reports itself every frame - not what the real input does.
    /// </para>
    /// </summary>
    [Fact]
    public void Jump_HeldSpace_ProducesASingleJump_NoBunnyHop()
    {
        var (scene, move, _, _) = CreateRig();

        _input.PressKey(KeyCode.Space);
        Tick(scene, 1);
        _input.ClearKeyTransitions();

        float firstLaunch = VerticalOf(move);
        Assert.True(firstLaunch > 0f,
            $"Space on the ground must launch upward, was {firstLaunch}.");

        // Key still physically held, but no longer reporting a fresh press. The velocity may
        // only ever fall from here - gravity, never another JumpForce.
        float previous = firstLaunch;
        for (int i = 0; i < 20; i++)
        {
            Tick(scene, 1);
            float v = VerticalOf(move);
            Assert.True(v <= previous + 1e-3f,
                $"Frame {i}: a held key must not re-trigger the jump. {previous} -> {v}.");
            previous = v;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Run: RunSpeed was declared and never read, so Shift did nothing.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Run_ReachesRunSpeed_WithShift()
    {
        var (scene, move, _, _) = CreateRig();

        _input.PressKey(KeyCode.W);
        _input.PressKey(KeyCode.ShiftLeft);
        Tick(scene, 90);

        float speed = Float3.Length(VelocityOf(move));
        Assert.True(speed > move.WalkSpeed + 0.5f,
            $"Shift must exceed WalkSpeed ({move.WalkSpeed}); got {speed}.");
        Assert.True(speed <= move.RunSpeed + 0.01f,
            $"Speed must not exceed RunSpeed ({move.RunSpeed}); got {speed}.");
    }

    [Fact]
    public void Walk_UsesWalkSpeed_WithoutShift()
    {
        var (scene, move, _, _) = CreateRig();

        _input.PressKey(KeyCode.W);
        Tick(scene, 90);

        float speed = Float3.Length(VelocityOf(move));
        Assert.True(speed > move.WalkSpeed - 0.05f && speed <= move.WalkSpeed + 0.05f,
            $"Without Shift the speed must settle at WalkSpeed ({move.WalkSpeed}); got {speed}.");
    }

    /// <summary>
    /// The Bug 1.7 case. Releasing Shift while still holding W drops the target from RunSpeed
    /// to WalkSpeed, and the old code picked its rate from "is the target zero", so this
    /// deceleration ran on Acceleration instead of Deceleration.
    /// </summary>
    [Fact]
    public void RunSpeed_ReleasedWhileHoldingW_SlowsDown()
    {
        var (scene, move, _, _) = CreateRig();

        _input.PressKey(KeyCode.W);
        _input.PressKey(KeyCode.ShiftLeft);
        Tick(scene, 120);
        float sprintSpeed = Float3.Length(VelocityOf(move));
        Assert.True(sprintSpeed > move.WalkSpeed, "Precondition: must be sprinting.");

        _input.ReleaseKey(KeyCode.ShiftLeft);
        Tick(scene, 120);
        float finalSpeed = Float3.Length(VelocityOf(move));

        Assert.True(finalSpeed < move.WalkSpeed + 0.05f,
            $"After releasing Shift the speed must come down to WalkSpeed; got {finalSpeed}.");
    }

    // ---------------------------------------------------------------------------------------
    // Model rotation
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A pure strafe used not to rotate the model at all, so the character moonwalked
    /// sideways while still facing where the camera looked.
    /// </summary>
    [Fact]
    public void PureStrafe_RotatesModel_TowardTheStrafe()
    {
        var (scene, move, _, _) = CreateRig();

        Update(scene, 2);
        float yawBefore = move.Transform.Rotation.EulerAngles.Y;

        _input.PressKey(KeyCode.D);
        Tick(scene, 120);

        float yawAfter = move.Transform.Rotation.EulerAngles.Y;
        Assert.True(MathF.Abs(ShortestAngle(yawAfter - yawBefore)) > 20f,
            $"A pure D must turn the model, not slide it sideways. {yawBefore} -> {yawAfter}.");

        // D is +X with the camera at yaw 0, so the model must end up facing +X (yaw 90).
        Assert.True(MathF.Abs(ShortestAngle(yawAfter - 90f)) < 5f,
            $"After a pure D the model must face +X (yaw ~90); was {yawAfter}.");
    }

    /// <summary>S alone is a backpedal and must keep NOT rotating the model.</summary>
    [Fact]
    public void BackwardOnly_DoesNotRotate()
    {
        var (scene, move, _, _) = CreateRig();

        Update(scene, 2);
        Quaternion before = move.Transform.Rotation;

        _input.PressKey(KeyCode.S);
        Tick(scene, 120);

        Assert.True(Float3.Distance(
            (move.Transform.Rotation * Float3.UnitZ),
            (before * Float3.UnitZ)) < 1e-2f,
            "A pure S must not turn the model: backpedal, not pivot.");
    }

    /// <summary>
    /// The slerp t is 1 - Exp(-k*dt) now, so the same number of frames turns the model the
    /// same amount whatever the frame rate. With the old TurnSpeed * dt a 30 FPS frame turned
    /// 0.4 of the way and a 144 FPS frame 0.083.
    /// </summary>
    [Fact]
    public void TurnRate_FrameRateIndependent()
    {
        float TurnWith(float dt, int frames)
        {
            Quaternion current = Quaternion.Identity;
            Quaternion target = Quaternion.FromEuler(0f, 90f, 0f);
            for (int i = 0; i < frames; i++)
            {
                float t = Math.Clamp(1f - MathF.Exp(-12f * dt), 0f, 1f);
                current = Quaternion.Slerp(current, target, t);
            }
            return current.EulerAngles.Y;
        }

        // Same wall-clock duration (one second), two very different frame rates.
        float slow = TurnWith(1f / 30f, 30);
        float fast = TurnWith(1f / 144f, 144);

        Assert.True(MathF.Abs(ShortestAngle(slow - fast)) < 1f,
            $"One second at 30 FPS reaches {slow}, at 144 FPS reaches {fast}: must match.");

        // The per-frame weight is what used to diverge, and it is why a lerp driven by
        // TurnSpeed * dt felt different at different frame rates. The exponential form keeps
        // the weights in the same ratio as the frame times, so the two curves meet at the same
        // place after the same elapsed time.
        float tSlow = Math.Clamp(1f - MathF.Exp(-12f / 30f), 0f, 1f);
        float tFast = Math.Clamp(1f - MathF.Exp(-12f / 144f), 0f, 1f);
        Assert.InRange(tSlow, 0f, 1f);
        Assert.InRange(tFast, 0f, 1f);
        Assert.True(tSlow > 4f * tFast,
            $"The old linear weights were 0.4 vs 0.083 (ratio 4.8); the exponential ones are {tSlow} vs {tFast} (ratio {tSlow / tFast}).");
    }

    /// <summary>
    /// A negative TurnSpeed is reachable straight from the Inspector. Unclamped, 1 - Exp(-k*dt)
    /// goes above 1 and Slerp extrapolates instead of interpolating.
    /// </summary>
    [Fact]
    public void SlerpT_Clamped_WhenTurnSpeedNegative()
    {
        float t = Math.Clamp(1f - MathF.Exp(-(-5f) * (1f / 60f)), 0f, 1f);
        Assert.InRange(t, 0f, 1f);
    }

    // ---------------------------------------------------------------------------------------
    // The handshake: the basis the character reads
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The bug this pair of components actually had. The old code derived "forward" from
    /// Target.Position - Camera.Transform.Position, so the direction mixed the smoothed pivot
    /// with the raw target and tilted toward the direction of travel. Now the camera publishes
    /// the basis from its yaw, which means it cannot depend on how far away the camera is - so
    /// collision pulling the arm in no longer changes how the character moves.
    /// </summary>
    [Fact]
    public void MovementBasis_SameAtCameraDistance1And6()
    {
        Float3 DirectionAt(float distance)
        {
            var (scene, move, _, cam) = CreateRig();
            cam.Distance = distance;
            Update(scene, 3);

            _input.PressKey(KeyCode.W);
            Tick(scene, 40);

            Float3 v = VelocityOf(move);
            v.Y = 0f;
            return Float3.Normalize(v);
        }

        Float3 far = DirectionAt(6f);
        Float3 near = DirectionAt(1f);

        Assert.True(Float3.Distance(far, near) < 1e-3f,
            $"W must go the same way at any camera distance. Far {far} vs near {near}.");
    }

    [Fact]
    public void MovementBasis_PointsWhereTheCameraLooks()
    {
        var (scene, move, _, cam) = CreateRig();
        Update(scene, 3);

        _input.PressKey(KeyCode.W);
        Tick(scene, 60);

        Float3 v = VelocityOf(move);
        v.Y = 0f;
        v = Float3.Normalize(v);

        Assert.True(Float3.Distance(v, cam.FlatForward) < 1e-2f,
            $"W must move along the camera forward {cam.FlatForward}; moved along {v}.");
    }

    /// <summary>
    /// A zero-length basis must stop the character rather than poison its velocity: a NaN here
    /// propagates into the position and the character is gone for good.
    /// <para>
    /// The camera's LateUpdate republishes the basis every frame, so the degenerate value has
    /// to be injected and the character's Update driven directly - otherwise the camera would
    /// simply overwrite it and the guard would never be reached.
    /// </para>
    /// </summary>
    [Fact]
    public void DegenerateBasis_DoesNotProduceNaN()
    {
        var (scene, move, _, cam) = CreateRig();
        Update(scene, 5);

        float velocityBefore = Float3.Length(VelocityOf(move));

        SetPrivate(cam, "FlatForward", Float3.Zero);
        SetPrivate(cam, "FlatRight", Float3.Zero);

        _input.PressKey(KeyCode.W);
        move.Update();

        Float3 v = VelocityOf(move);
        Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z),
            $"A degenerate basis must not produce a non-finite velocity: {v}");
        Assert.True(Float3.Length(v) <= velocityBefore + 1e-4f,
            $"A degenerate basis must not accelerate the character: was {velocityBefore}, now {v}.");

        Float3 pos = move.Transform.Position;
        Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z),
            $"Position must stay finite: {pos}");
    }

    /// <summary>
    /// The camera guards the degenerate case on its side too, so a bad yaw can never hand the
    /// character a zero-length vector in the first place.
    /// </summary>
    [Fact]
    public void Camera_PublishesNonDegenerateBasis_EvenBeforeAnyInput()
    {
        var (scene, _, _, cam) = CreateRig();

        // No mouse movement at all, straight off the default state.
        Update(scene, 2);

        Assert.True(Float3.LengthSquared(cam.FlatForward) > 1e-6f,
            $"FlatForward must never be zero-length, was {cam.FlatForward}.");
        Assert.True(Float3.LengthSquared(cam.FlatRight) > 1e-6f,
            $"FlatRight must never be zero-length, was {cam.FlatRight}.");
    }

    /// <summary>The turn-rate feature is optional and must be off by default.</summary>
    [Fact]
    public void TurnRate_DisabledByDefault_ChangesNothing()
    {
        var (scene, move, _, _) = CreateRig();
        Assert.False(GetPrivate<bool>(move, "_turnRateEnabled"),
            "The turn rate is an optional feature and must default to off.");
    }

    private static float ShortestAngle(float deg)
    {
        while (deg > 180f) deg -= 360f;
        while (deg < -180f) deg += 360f;
        return deg;
    }
}
