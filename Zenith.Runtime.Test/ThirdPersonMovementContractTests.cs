// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Covers the contract between <see cref="OrbitFollowCamera"/> and
/// <see cref="ThirdPersonCharacterMovement"/>.
///
/// The single rule everything here protects: movement is always interpreted in the CAMERA's
/// frame. W goes where the camera looks, S goes the opposite way, and StrafeMode does not
/// change that - it only changes which way the body faces.
/// </summary>
public class ThirdPersonMovementContractTests : RuntimeTestBase
{
    private FakeInputHandler _input = null!;

    private static Float3 Flat(Float3 v)
    {
        v.Y = 0f;
        return Float3.LengthSquared(v) > 1e-8f ? Float3.Normalize(v) : Float3.Zero;
    }

    /// <summary>
    /// Floor the character can actually walk on. A thin BoxCollider puts the capsule in resting
    /// contact, where CharacterController currently refuses to move at all (documented as
    /// PENDIENTE 1 in docs/PLAN_CHARACTER_CAMERA_FIX.md), so a movement test built on one would
    /// measure that bug instead of this component. The mesh floor leaves the capsule clear.
    /// </summary>
    private GameObject CreateWalkableFloor(float topY = 0f, int cells = 8, float size = 60f)
    {
        var verts = new List<Float3>();
        var indices = new List<uint>();
        float step = size / cells, h = size * 0.5f;

        for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
                verts.Add(new Float3(-h + x * step, topY, -h + z * step));

        int Idx(int x, int z) => z * (cells + 1) + x;
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                int a = Idx(x, z), b = Idx(x + 1, z), c = Idx(x + 1, z + 1), d = Idx(x, z + 1);
                indices.Add((uint)a); indices.Add((uint)b); indices.Add((uint)c);
                indices.Add((uint)a); indices.Add((uint)c); indices.Add((uint)d);
            }

        var mesh = new Mesh { Vertices = verts.ToArray() };
        mesh.Indices = indices.ToArray();

        GameObject go = CreateGameObject("Floor");
        var mc = go.AddComponent<MeshCollider>();
        mc.Mesh = mesh;
        mc.Convex = false;
        return go;
    }

    /// <summary>
    /// Character starting clear of the floor so it is genuinely moving, not resting on it.
    /// The camera defaults to yaw 0, which sits it on -Z looking toward +Z.
    /// </summary>
    private (Scene scene, GameObject player, ThirdPersonCharacterMovement move, OrbitFollowCamera cam)
        CreateRig(bool strafe = false)
    {
        _input = new FakeInputHandler();
        Input.PushHandler(_input);

        Scene scene = CreateScene(enable: true);
        scene.Add(CreateWalkableFloor());

        GameObject player = CreateGameObject("Player");
        scene.Add(player);
        var ctrl = player.AddComponent<CharacterController>();
        ctrl.Shape = CharacterController.ColliderShape.Capsule;
        ctrl.Size = new Float3(0.8f, 1.8f, 0.8f);
        player.Transform.Position = new Float3(0f, 1.2f, 0f);

        GameObject camGo = CreateGameObject("Camera");
        scene.Add(camGo);
        var cam = camGo.AddComponent<OrbitFollowCamera>();
        cam.Target = player.Transform;
        cam.Mode = OrbitFollowCamera.OrbitMode.HoldRightClick;
        cam.CollisionEnabled = false;

        var move = player.AddComponent<ThirdPersonCharacterMovement>();
        move.Camera = cam;
        move.Controller = ctrl;
        move.StrafeMode = strafe;

        Tick(scene, 8);
        return (scene, player, move, cam);
    }

    /// <summary>
    /// Drag the mouse and then let the camera come to REST before measuring anything. The yaw
    /// lerps toward its target, so a large constant delta keeps it rotating for many frames
    /// after the drag ends; measuring mid-flight compares two different bases.
    /// </summary>
    private void OrbitCameraToRest(Scene scene, float dx, float dy = 0f)
    {
        _input.SetMouseButton(1, true);
        for (int i = 0; i < 30; i++)
        {
            _input.SetMouseDelta(new Float2(dx, dy));
            Update(scene, 1);
        }
        _input.SetMouseButton(1, false);
        _input.SetMouseDelta(Float2.Zero);
        Update(scene, 200);
    }

    /// <summary>Press a key and return the direction the body actually travelled, flattened.</summary>
    private Float3 TravelWithKey(Scene scene, GameObject player, KeyCode key, int frames = 40)
    {
        Float3 start = player.Transform.Position;
        _input.PressKey(key);
        Tick(scene, frames);
        return Flat(player.Transform.Position - start);
    }

    // ---------------------------------------------------------------------------------------
    // The core rule: movement lives in the camera's frame.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// W goes where the camera looks, S goes the other way. Checked at eight different yaws so
    /// it cannot pass by coincidence at the default one, where the answer is trivially +Z.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(300f)]
    [InlineData(600f)]
    [InlineData(900f)]
    [InlineData(1200f)]
    [InlineData(1500f)]
    [InlineData(2100f)]
    [InlineData(2700f)]
    public void W_GoesWhereCameraLooks_AndS_GoesOpposite(float drag)
    {
        var (scene, player, _, cam) = CreateRig();
        OrbitCameraToRest(scene, drag);

        Float3 camForward = Float3.Normalize(cam.FlatForward);
        Assert.True(Float3.Distance(Flat(cam.Transform.Rotation * Float3.UnitZ), camForward) < 1e-2f,
            $"Precondition: FlatForward must be the camera's own look direction at drag {drag}.");

        Float3 w = TravelWithKey(scene, player, KeyCode.W);
        _input.ReleaseKey(KeyCode.W);
        Float3 s = TravelWithKey(scene, player, KeyCode.S);

        Assert.True(Float3.Distance(w, camForward) < 1e-2f,
            $"W must move along the camera forward {camForward}, moved {w} (drag {drag}).");

        Assert.True(Float3.Distance(s, -camForward) < 1e-2f,
            $"S must move against the camera forward {camForward}, moved {s} (drag {drag}).");
    }

    /// <summary>
    /// StrafeMode must NOT change the direction of travel. It only changes where the body
    /// points. If a future edit makes strafe re-base the movement, this fails.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrafeMode_ChangesWhereTheBodyFaces_NotWhereItGoes(bool strafe)
    {
        var (scene, player, move, cam) = CreateRig(strafe);
        OrbitCameraToRest(scene, 900f);

        Float3 camForward = Float3.Normalize(cam.FlatForward);
        Float3 travelled = TravelWithKey(scene, player, KeyCode.S);

        Assert.True(Float3.Distance(travelled, -camForward) < 1e-2f,
            $"strafe={strafe}: S must travel against the camera forward regardless of mode, " +
            $"moved {travelled} against {-camForward}.");
    }

    /// <summary>D must move along FlatRight, and A against it, at any yaw.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(700f)]
    [InlineData(1400f)]
    public void D_MovesAlongFlatRight_AndA_Opposite(float drag)
    {
        var (scene, player, _, cam) = CreateRig();
        OrbitCameraToRest(scene, drag);

        Float3 right = Float3.Normalize(cam.FlatRight);
        Float3 d = TravelWithKey(scene, player, KeyCode.D);
        _input.ReleaseKey(KeyCode.D);
        Float3 a = TravelWithKey(scene, player, KeyCode.A);

        Assert.True(Float3.Distance(d, right) < 1e-2f,
            $"D must move along FlatRight {right}, moved {d} (drag {drag}).");
        Assert.True(Float3.Distance(a, -right) < 1e-2f,
            $"A must move against FlatRight {right}, moved {a} (drag {drag}).");
    }

    /// <summary>
    /// Diagonals must be normalized: W+D cannot be faster than W alone.
    /// </summary>
    [Fact]
    public void Diagonal_IsNotFasterThanASingleAxis()
    {
        float Run(KeyCode a, KeyCode? b)
        {
            var (scene, player, _, _) = CreateRig();
            _input.PressKey(a);
            if (b.HasValue) _input.PressKey(b.Value);
            Float3 start = player.Transform.Position;
            Tick(scene, 40);
            Float3 d = player.Transform.Position - start;
            d.Y = 0f;
            return Float3.Length(d);
        }

        float w = Run(KeyCode.W, null);
        float wd = Run(KeyCode.W, KeyCode.D);

        Assert.True(MathF.Abs(wd - w) < 0.05f,
            $"A diagonal must travel the same distance as one axis: W={w:F3}, W+D={wd:F3}.");
    }

    // ---------------------------------------------------------------------------------------
    // Orientation: the one thing StrafeMode actually controls.
    // ---------------------------------------------------------------------------------------

    /// <summary>Non-strafe: the body faces where it moves.</summary>
    [Theory]
    [InlineData(KeyCode.W, 0f)]
    [InlineData(KeyCode.S, 180f)]
    [InlineData(KeyCode.D, 90f)]
    [InlineData(KeyCode.A, -90f)]
    public void NonStrafe_BodyFacesWhereItMoves(KeyCode key, float expectedYaw)
    {
        var (scene, player, _, _) = CreateRig(strafe: false);

        // Start off-axis so every quadrant performs a real turn.
        Update(scene, 2);
        player.Transform.LocalRotation = Quaternion.FromEuler(0f, 45f, 0f);

        _input.PressKey(key);
        Tick(scene, 120);

        float yaw = player.Transform.Rotation.EulerAngles.Y;
        Assert.True(MathF.Abs(ShortestAngle(yaw - expectedYaw)) < 5f,
            $"Non-strafe with {key} must turn the body to {expectedYaw}, ended at {yaw}.");
    }

    /// <summary>
    /// Strafe: the body faces the camera's forward no matter which key is held, including
    /// sideways and backwards. This is the whole difference between the two modes.
    /// </summary>
    [Theory]
    [InlineData(KeyCode.W)]
    [InlineData(KeyCode.S)]
    [InlineData(KeyCode.A)]
    [InlineData(KeyCode.D)]
    public void Strafe_BodyAlwaysFacesCameraForward(KeyCode key)
    {
        var (scene, player, _, cam) = CreateRig(strafe: true);
        OrbitCameraToRest(scene, 1100f);

        Update(scene, 2);
        player.Transform.LocalRotation = Quaternion.FromEuler(0f, 200f, 0f);

        _input.PressKey(key);
        Tick(scene, 120);

        Float3 face = Flat(player.Transform.Rotation * Float3.UnitZ);
        Float3 camForward = Flat(cam.FlatForward);

        Assert.True(Float3.Distance(face, camForward) < 1e-2f,
            $"In strafe the body must keep facing the camera forward {camForward} with {key}, " +
            $"but faces {face}.");
    }

    /// <summary>
    /// Without strafe and without input the body must stay put. It is the old "heading freeze"
    /// that made S look like it oscillated: the gate compared signs, so the back-left quadrant
    /// fell through and the mesh kept its last valid angle while the body moved.
    /// </summary>
    [Fact]
    public void NonStrafe_WithoutInput_BodyKeepsItsHeading()
    {
        var (scene, player, _, _) = CreateRig(strafe: false);

        _input.PressKey(KeyCode.W);
        Tick(scene, 90);
        float headingAfterW = player.Transform.Rotation.EulerAngles.Y;

        _input.ReleaseKey(KeyCode.W);
        Tick(scene, 90);

        float headingAfterRelease = player.Transform.Rotation.EulerAngles.Y;
        Assert.True(MathF.Abs(ShortestAngle(headingAfterRelease - headingAfterW)) < 2f,
            $"Releasing every key must leave the heading alone: {headingAfterW} -> {headingAfterRelease}.");

        // And strafe is allowed to keep re-aligning, because there the camera owns the facing.
        var (scene2, player2, _, cam2) = CreateRig(strafe: true);
        _input.PressKey(KeyCode.W);
        Tick(scene2, 90);
        _input.ReleaseKey(KeyCode.W);
        _input.ClearKeyTransitions();
        Tick(scene2, 90);
        Assert.True(Float3.Distance(
            Flat(player2.Transform.Rotation * Float3.UnitZ), Flat(cam2.FlatForward)) < 1e-2f,
            "In strafe the body must still face the camera once input stops.");
    }

    // ---------------------------------------------------------------------------------------
    // Speed
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Shift_ReachesRunSpeed_AndReleasingItFallsBackToWalk()
    {
        var (scene, player, move, _) = CreateRig();

        _input.PressKey(KeyCode.W);
        _input.PressKey(KeyCode.ShiftLeft);
        Tick(scene, 120);
        float running = SpeedOf(move);
        Assert.True(running > move.WalkSpeed + 0.5f && running <= move.RunSpeed + 0.01f,
            $"Shift must exceed WalkSpeed ({move.WalkSpeed}); got {running}.");

        _input.ReleaseKey(KeyCode.ShiftLeft);
        Tick(scene, 120);
        float walking = SpeedOf(move);
        Assert.True(walking < move.WalkSpeed + 0.05f,
            $"Releasing Shift must come back down to WalkSpeed ({move.WalkSpeed}); got {walking}.");
    }

    /// <summary>
    /// Losing speed and gaining it are different rates. The old code chose by "is the target
    /// zero", so dropping Shift (8 -> 5) braked with Acceleration like starting from rest.
    /// </summary>
    [Fact]
    public void LosingSpeed_UsesDeceleration_NotAcceleration()
    {
        var (scene, _, move, _) = CreateRig();

        _input.PressKey(KeyCode.W);
        _input.PressKey(KeyCode.ShiftLeft);
        Tick(scene, 120);
        Assert.True(SpeedOf(move) > move.WalkSpeed, "Precondition: must be running.");

        _input.ReleaseKey(KeyCode.ShiftLeft);

        // One frame: the rate is read every frame, and a single Update is where the two rates
        // differ most sharply.
        const float dt = 1f / 60f;
        float before = SpeedOf(move);
        Update(scene, 1);
        float after = SpeedOf(move);

        // The velocity converges toward the TARGET, not toward zero, so the drop scales with
        // how much speed is being given up (RunSpeed - WalkSpeed), not with the whole speed.
        float gap = before - move.WalkSpeed;
        float accelDrop = before - after;
        float expectedAccel = gap * (1f - MathF.Exp(-move.Acceleration * dt));
        float expectedDecel = gap * (1f - MathF.Exp(-move.Deceleration * dt));

        Assert.True(MathF.Abs(accelDrop - expectedDecel) < 0.05f,
            $"Dropping Shift must brake with Deceleration ({move.Deceleration}); dropped {accelDrop:F3}, " +
            $"expected {expectedDecel:F3} (Acceleration would give {expectedAccel:F3}).");
    }

    [Fact]
    public void WalkSpeed_IsHonouredWithoutShift()
    {
        var (scene, _, move, _) = CreateRig();
        _input.PressKey(KeyCode.W);
        Tick(scene, 120);

        Assert.True(MathF.Abs(SpeedOf(move) - move.WalkSpeed) < 0.05f,
            $"Without Shift the speed must settle at WalkSpeed ({move.WalkSpeed}); got {SpeedOf(move)}.");
    }

    // ---------------------------------------------------------------------------------------
    // Jump
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A held key must not re-trigger the jump. GetKeyDown is edge triggered in the real
    /// handler, so a held Space reports "down" on exactly one frame; reading the live grounded
    /// state instead of the start-of-frame one would allow a second jump on the next frame.
    /// </summary>
    [Fact]
    public void Jump_HeldSpace_ProducesExactlyOneJump()
    {
        var (scene, _, move, _) = CreateRig();
        // Let it land: CreateRig starts the capsule clear of the floor so the walking tests
        // measure real travel, which means it needs a moment to settle before a jump is valid.
        for (int i = 0; i < 60 && !move.Controller.IsGrounded; i++) Tick(scene, 1);
        Assert.True(move.Controller.IsGrounded, "Precondition: the rig must settle onto the floor.");

        _input.PressKey(KeyCode.Space);
        Tick(scene, 1);
        _input.ClearKeyTransitions();

        float launch = VerticalOf(move);
        Assert.True(launch > 0f, $"Space on the ground must launch upward, was {launch}.");

        float previous = launch;
        for (int i = 0; i < 20; i++)
        {
            Tick(scene, 1);
            float v = VerticalOf(move);
            Assert.True(v <= previous + 1e-3f,
                $"Frame {i}: a held key must not re-trigger the jump: {previous} -> {v}.");
            previous = v;
        }
    }

    /// <summary>Gravity is always applied in the air, so the arc comes back down.</summary>
    [Fact]
    public void Jump_ComesBackDownUnderGravity()
    {
        var (scene, _, move, _) = CreateRig();
        for (int i = 0; i < 60 && !move.Controller.IsGrounded; i++) Tick(scene, 1);

        _input.PressKey(KeyCode.Space);
        Tick(scene, 1);
        _input.ClearKeyTransitions();

        float peak = VerticalOf(move);
        for (int i = 0; i < 200; i++)
        {
            Tick(scene, 1);
            peak = MathF.Max(peak, VerticalOf(move));
        }

        Assert.True(peak > 0f, "The jump must actually leave the ground.");
        Assert.True(move.Controller.IsGrounded, $"The character must land again; ended grounded={move.Controller.IsGrounded}.");
    }

    // ---------------------------------------------------------------------------------------
    // Robustness
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A zero-length basis must stop the character rather than poison its velocity: a NaN here
    /// travels into the position and the character is gone for good. The camera republishes every
    /// frame, so the bad value has to be injected and Update driven directly.
    /// </summary>
    [Fact]
    public void DegenerateBasis_ProducesNoNaNAndNoSpeed()
    {
        var (scene, player, move, cam) = CreateRig();

        _input.PressKey(KeyCode.W);
        Tick(scene, 60);
        float before = SpeedOf(move);
        Assert.True(before > 0f, "Precondition: the character must be moving.");

        SetPrivate(cam, "FlatForward", Float3.Zero);
        SetPrivate(cam, "FlatRight", Float3.Zero);

        move.Update();

        Float3 v = GetPrivate<Float3>(move, "_velocity");
        Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z),
            $"A degenerate basis must not produce a non-finite velocity: {v}");

        Float3 pos = player.Transform.Position;
        Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z),
            $"Position must stay finite: {pos}");
    }

    /// <summary>The camera must never publish a zero-length basis, even before any input.</summary>
    [Fact]
    public void Camera_PublishesAUsableBasisBeforeAnyInput()
    {
        var (scene, _, _, cam) = CreateRig();
        Update(scene, 2);

        Assert.True(Float3.LengthSquared(cam.FlatForward) > 1e-6f,
            $"FlatForward must never be zero-length, was {cam.FlatForward}.");
        Assert.True(Float3.LengthSquared(cam.FlatRight) > 1e-6f,
            $"FlatRight must never be zero-length, was {cam.FlatRight}.");

        cam.GetMovementBasis(out Float3 f, out Float3 r);
        Assert.True(Float3.Distance(f, cam.FlatForward) < 1e-6f
                 && Float3.Distance(r, cam.FlatRight) < 1e-6f,
            "GetMovementBasis must agree with the properties.");
    }

    /// <summary>A missing camera or controller is a no-op, not a crash.</summary>
    [Fact]
    public void MissingReferences_DoNotThrow()
    {
        var (scene, _, move, _) = CreateRig();
        move.Camera = null;
        move.Controller = null;
        move.Update();

        var (scene2, _, move2, _) = CreateRig();
        move2.Camera.Target = null;
        Tick(scene2, 5);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static float SpeedOf(ThirdPersonCharacterMovement move) =>
        Float3.Length(GetPrivate<Float3>(move, "_velocity"));

    private static float VerticalOf(ThirdPersonCharacterMovement move) =>
        GetPrivate<float>(move, "_verticalVelocity");

    private static float ShortestAngle(float deg)
    {
        while (deg > 180f) deg -= 360f;
        while (deg < -180f) deg += 360f;
        return deg;
    }

    private static T GetPrivate<T>(object target, string field)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        return (T)f.GetValue(target)!;
    }

    /// <summary>FlatForward and FlatRight are properties, so their storage is a compiler-generated backing field.</summary>
    private static void SetPrivate(object target, string field, object value)
    {
        var t = target.GetType();
        var f = t.GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        f ??= t.GetField($"<{field}>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        f.SetValue(target, value);
    }
}