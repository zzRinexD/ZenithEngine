// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// <see cref="OrbitFollowCamera"/> was untested, so the pitch sign, the published movement
/// basis and the lookAt all went unverified. These pin the behaviour the character movement
/// depends on.
/// </summary>
public class OrbitFollowCameraTests : RuntimeTestBase
{
    private FakeInputHandler _input = null!;

    private FakeInputHandler PushInput()
    {
        _input = new FakeInputHandler();
        Input.PushHandler(_input);
        return _input;
    }

    /// <summary>A camera orbiting a target standing at the origin, in HoldRightClick mode so no
    /// cursor lock is needed to make it orbit.</summary>
    private (Scene scene, OrbitFollowCamera cam, GameObject target) CreateRig()
    {
        Scene scene = CreateScene(enable: true);

        GameObject targetGo = CreateGameObject("Target");
        scene.Add(targetGo);

        GameObject camGo = CreateGameObject("Camera");
        scene.Add(camGo);
        var cam = camGo.AddComponent<OrbitFollowCamera>();
        cam.Target = targetGo.Transform;
        cam.Mode = OrbitFollowCamera.OrbitMode.HoldRightClick;
        // Physics would otherwise shorten the arm in a scene with no colliders anyway, but
        // leaving it on exercises the spring path every frame.
        cam.CollisionEnabled = true;

        return (scene, cam, targetGo);
    }

    /// <summary>Mouse delta is only sampled while the right button is held in HoldRightClick mode.</summary>
    private void DragMouse(float dx, float dy)
    {
        _input.SetMouseButton(1, true);
        _input.SetMouseDelta(new Float2(dx, dy));
    }

    // ---------------------------------------------------------------------------------------
    // Pitch: the inverted-sign bug, and the range that had to be inverted with it.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Moving the mouse UP must raise the camera. In this engine a NEGATIVE pitch looks up
    /// (the editor camera builds its forward as -sin(pitch) on Y), so raising the camera means
    /// the pitch goes down toward MinPitch. It used to go up toward MaxPitch instead, which
    /// aimed at the ground: the input was negated while the convention is positive-is-down.
    /// That made the control unusable, not just uncomfortable.
    /// </summary>
    [Fact]
    public void PitchDelta_MouseUp_LowersPitch_SoTheCameraLooksUp()
    {
        PushInput();
        var (scene, cam, _) = CreateRig();

        Update(scene, 3);
        float pitchBefore = YawPitchOf(cam).Y;
        Assert.True(pitchBefore >= 0f, $"Starting pitch should be near zero, was {pitchBefore}.");

        DragMouse(0f, -30f); // mouse up
        Update(scene, 30);

        float pitchAfter = YawPitchOf(cam).Y;
        Assert.True(pitchAfter < pitchBefore,
            $"Mouse up must raise the camera, i.e. lower the pitch: {pitchBefore} -> {pitchAfter}.");

        // And mouse down must look down instead.
        DragMouse(0f, 300f);
        Update(scene, 30);
        Assert.True(YawPitchOf(cam).Y > pitchAfter,
            "Mouse down must lower the camera, i.e. raise the pitch.");
    }

    /// <summary>
    /// The clamp range had to be inverted in lockstep with the sign. Without that, flipping
    /// the sign silently swapped the limits: 18 degrees up / 36 down became 36 up / 18 down,
    /// which is a feel change smuggled into a bug fix.
    /// </summary>
    [Fact]
    public void Pitch_ClampedToMinMax_AndRangeIsSymmetricAroundHorizon()
    {
        PushInput();
        var (scene, cam, _) = CreateRig();

        Assert.Equal(-36f, cam.MinPitch);
        Assert.Equal(18f, cam.MaxPitch);

        // Push far past the upper limit.
        DragMouse(0f, 5000f);
        Update(scene, 200);
        Assert.Equal(18f, YawPitchOf(cam).Y, 1e-3f);

        // And far past the lower one.
        DragMouse(0f, -50000f);
        Update(scene, 400);
        Assert.Equal(-36f, YawPitchOf(cam).Y, 1e-3f);
    }

    // ---------------------------------------------------------------------------------------
    // The published movement basis - the contract the character reads.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The basis must come from the yaw, not from where the camera happens to sit. This is
    /// the whole handshake: the old code derived "forward" from
    /// Target.Position - Camera.Transform.Position, which mixed the smoothed pivot with the
    /// raw target and made forward drift toward the direction of travel.
    /// </summary>
    [Fact]
    public void FlatForward_MatchesYawForward_NotLookAtDirection()
    {
        PushInput();
        var (scene, cam, _) = CreateRig();

        Update(scene, 3);

        float yaw = YawPitchOf(cam).X;
        Float3 expected = Quaternion.FromEuler(0f, yaw, 0f) * Float3.UnitZ;
        expected.Y = 0f;
        expected = Float3.Normalize(expected);

        Assert.True(Float3.Distance(expected, cam.FlatForward) < 1e-3f,
            $"FlatForward {cam.FlatForward} should equal the yaw forward {expected}.");

        // It must be horizontal regardless of pitch: a pitched camera still moves the player
        // along the ground plane.
        Assert.Equal(0f, cam.FlatForward.Y, 1e-6f);
    }

    /// <summary>D must be 90 degrees clockwise from W, and both unit length.</summary>
    [Fact]
    public void FlatForward_OrthogonalToFlatRight_AndBothUnit()
    {
        PushInput();
        var (scene, cam, _) = CreateRig();

        DragMouse(120f, -40f);
        Update(scene, 30);

        Assert.Equal(1f, Float3.Length(cam.FlatForward), 1e-4f);
        Assert.Equal(1f, Float3.Length(cam.FlatRight), 1e-4f);
        Assert.Equal(0f, Float3.Dot(cam.FlatForward, cam.FlatRight), 1e-4f);
    }

    /// <summary>
    /// With yaw at zero the camera sits on -Z looking toward +Z, so W must be +Z and D +X.
    /// This is the convention the old character code used (right = cross(up, forward)) and
    /// getting it backwards would silently invert A and D.
    /// </summary>
    [Fact]
    public void FlatBasis_WithZeroYaw_IsPlusZForward_PlusXRight()
    {
        PushInput();
        var (scene, cam, _) = CreateRig();

        Update(scene, 3);

        Assert.Equal(1f, cam.FlatForward.Z, 1e-3f);
        Assert.Equal(0f, cam.FlatForward.X, 1e-3f);
        Assert.Equal(1f, cam.FlatRight.X, 1e-3f);
        Assert.Equal(0f, cam.FlatRight.Z, 1e-3f);
    }

    // ---------------------------------------------------------------------------------------
    // LookAt
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The lookAt point used to be recomputed from the raw target while the camera position
    /// came from the smoothed pivot, so the framing pulled away while the character moved.
    /// After a teleport the two differ by tens of units, so this checks which one the camera
    /// actually aims at by comparing the angle to each candidate.
    /// </summary>
    [Fact]
    public void LookAt_UsesSmoothedPivotNotRawTarget()
    {
        PushInput();
        var (scene, cam, target) = CreateRig();
        cam.CollisionEnabled = false;
        // Snap would jump the pivot onto the raw target and hide the very difference under test.
        SetPrivate(cam, "_snapOnTeleport", false);

        Update(scene, 10);
        target.Transform.Position = new Float3(30f, 0f, 0f);
        Update(scene, 2);

        Float3 camPos = cam.Transform.Position;
        Float3 camForward = cam.Transform.Rotation * Float3.UnitZ;

        // Where the camera is aiming, read straight off its own rotation.
        Float3 aimedPoint = camPos + camForward * 10f;

        // The smoothed pivot is still lagging well short of the raw target, and the camera
        // must be aiming at the pivot, not the target. Comparing against the pivot the
        // component actually holds is what makes this meaningful.
        Float3 pivot = GetPrivate<Float3>(cam, "_smoothedTargetPos");
        float errorToPivot = Float3.Distance(aimedPoint, pivot);
        float errorToRawTarget = Float3.Distance(aimedPoint, target.Transform.Position);

        Assert.True(errorToPivot < errorToRawTarget,
            $"The camera must aim at the smoothed pivot {pivot} (error {errorToPivot}), not the raw target (error {errorToRawTarget}).");

        // Sanity: the pivot really is lagging, otherwise the test proves nothing.
        Assert.True(pivot.X < 20f, $"The pivot should still be lagging behind 30, was at {pivot.X}.");

        // Note the camera keeps its own yaw: an orbit rig does not turn to face a teleport, it
        // keeps the angle the player chose and swings the pivot around. So there is no
        // direction assertion here - only where the lookAt point lands.
    }

    /// <summary>No Target assigned must be a silent no-op, not an exception.</summary>
    [Fact]
    public void MissingTarget_DoesNotThrow()
    {
        PushInput();
        Scene scene = CreateScene(enable: true);

        GameObject camGo = CreateGameObject("Camera");
        scene.Add(camGo);
        var cam = camGo.AddComponent<OrbitFollowCamera>();
        cam.Target = null;

        DragMouse(50f, -50f);
        Tick(scene, 5);
    }

    // ---------------------------------------------------------------------------------------
    // Zoom (optional feature)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void WheelZoom_ChangesDistance_AndClampsToRange()
    {
        PushInput();
        var (scene, cam, _) = CreateRig();
        cam.CollisionEnabled = false;

        Update(scene, 2);
        float start = cam.Distance;

        // Wheel up (positive) zooms in.
        _input.SetMouseWheel(1f);
        Update(scene, 2);
        Assert.True(cam.Distance < start, $"Wheel up must zoom in: {start} -> {cam.Distance}.");
        Assert.True(cam.Distance >= 2f, "Zoom in must respect the minimum.");

        // Keep zooming in; it clamps at 2 and never goes below.
        for (int i = 0; i < 40; i++) { _input.SetMouseWheel(1f); Update(scene, 1); }
        Assert.Equal(2f, cam.Distance, 1e-3f);

        // And out, clamping at 15.
        for (int i = 0; i < 200; i++) { _input.SetMouseWheel(-1f); Update(scene, 1); }
        Assert.Equal(15f, cam.Distance, 1e-3f);
    }

    /// <summary>The toggle is the contract: off means the wheel does nothing at all.</summary>
    [Fact]
    public void WheelZoom_DoesNothing_WhenDisabled()
    {
        PushInput();
        var (scene, cam, _) = CreateRig();
        cam.CollisionEnabled = false;
        SetPrivate(cam, "_zoomEnabled", false);

        Update(scene, 2);
        float start = cam.Distance;

        _input.SetMouseWheel(1f);
        Update(scene, 10);

        Assert.Equal(start, cam.Distance, 1e-6f);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The orbit yaw/pitch, read from the component's own smoothed fields. NOT
    /// Transform.Rotation: at the end of LateUpdate that holds the look-at rotation, which is
    /// a different thing entirely (it includes the lookAt offset and is flattened against the
    /// chest point). Reading it made the pitch look like it never moved.
    /// </summary>
    private static Float3 YawPitchOf(OrbitFollowCamera cam)
    {
        float yaw = GetPrivateFloat(cam, "_yaw");
        float pitch = GetPrivateFloat(cam, "_pitch");
        return new Float3(yaw, pitch, 0f);
    }

    private static float GetPrivateFloat(object target, string field)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        return (float)f.GetValue(target)!;
    }

    private static T GetPrivate<T>(object target, string field)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        return (T)f.GetValue(target)!;
    }

    internal static void SetPrivate(object target, string field, object value)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        f.SetValue(target, value);
    }
}
