// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the <see cref="InputInteractionType"/> behaviors evaluated per binding
/// (Press, Hold, Tap) plus the two documented known issues around <c>Disable()</c>.
/// Time is fed explicitly through <c>UpdateState(handler, time)</c>; no real input involved.
/// </summary>
public class InputInteractionTests
{
    private static List<InputActionPhase> CapturePhases(InputAction action)
    {
        var log = new List<InputActionPhase>();
        action.Started += _ => log.Add(InputActionPhase.Started);
        action.Performed += _ => log.Add(InputActionPhase.Performed);
        action.Canceled += _ => log.Add(InputActionPhase.Canceled);
        return log;
    }

    /// <summary>Press fires exactly once per press and re-arms only after a release.</summary>
    [Fact]
    public void Press_Interaction_TriggersOncePerPress()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Fire", InputActionType.Button);
        action.AddBinding(KeyCode.Space, InputInteractionType.Press);
        var log = CapturePhases(action);
        action.Enable();

        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.0f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
        Assert.Equal(1.0f, action.ReadValue<float>());

        // Held: the Press interaction is spent → the action cancels and goes quiet.
        log.Clear();
        action.UpdateState(handler, 0.1f);
        Assert.Equal(new[] { InputActionPhase.Canceled }, log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);

        log.Clear();
        action.UpdateState(handler, 0.2f);
        Assert.Empty(log);

        // Release re-arms it; a second press performs again.
        log.Clear();
        handler.ReleaseKey(KeyCode.Space);
        action.UpdateState(handler, 0.3f);
        Assert.Empty(log);

        log.Clear();
        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.4f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
    }

    /// <summary>Hold performs once the key has been held for HoldDuration (default 0.4s) — once only.</summary>
    [Fact]
    public void Hold_Interaction_TriggersOnceAfterHoldDuration()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Sprint", InputActionType.Button);
        action.AddBinding(KeyCode.Space, InputInteractionType.Hold);
        var log = CapturePhases(action);
        int performedCount = 0;
        action.Performed += _ => performedCount++;
        action.Enable();

        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.0f); // press recorded, duration not met
        action.UpdateState(handler, 0.2f); // still short of 0.4s
        Assert.Empty(log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);

        action.UpdateState(handler, 0.45f); // ≥ 0.4s held → performs
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
        Assert.Equal(1.0f, action.ReadValue<float>());

        // Still held afterwards: no second perform; cancels back to Waiting.
        log.Clear();
        action.UpdateState(handler, 0.55f);
        Assert.Equal(new[] { InputActionPhase.Canceled }, log);

        log.Clear();
        action.UpdateState(handler, 0.65f);
        Assert.Empty(log);

        Assert.Equal(1, performedCount); // the hold fires exactly once
    }

    /// <summary>Tap fires on a release within MaxTapDuration (default 0.2s) and never after a long hold.</summary>
    [Fact]
    public void Tap_Interaction_FiresOnQuickRelease_NotOnLongHold()
    {
        var handler = new FakeInputHandler();

        // (a) Quick press-and-release within the limit → the tap completes on release.
        var quick = new InputAction("QuickTap", InputActionType.Button);
        quick.AddBinding(KeyCode.Space, InputInteractionType.Tap);
        var quickLog = CapturePhases(quick);
        quick.Enable();

        handler.PressKey(KeyCode.Space);
        quick.UpdateState(handler, 0.0f);
        Assert.Empty(quickLog); // a tap only completes on release

        handler.ReleaseKey(KeyCode.Space);
        quick.UpdateState(handler, 0.1f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, quickLog);
        Assert.Equal(1.0f, quick.ReadValue<float>());

        // (b) Held past MaxTapDuration → the tap is void, releasing changes nothing.
        var slow = new InputAction("SlowTap", InputActionType.Button);
        slow.AddBinding(KeyCode.Space, InputInteractionType.Tap);
        var slowLog = CapturePhases(slow);
        slow.Enable();

        handler.PressKey(KeyCode.Space);
        slow.UpdateState(handler, 1.0f);
        slow.UpdateState(handler, 1.15f); // held 0.15s, still under the limit
        slow.UpdateState(handler, 1.3f);  // held 0.30s > 0.2s → tap canceled
        handler.ReleaseKey(KeyCode.Space);
        slow.UpdateState(handler, 1.5f);  // released too late

        Assert.Empty(slowLog);
        Assert.Equal(InputActionPhase.Waiting, slow.Phase);
        Assert.Equal(0.0f, slow.ReadValue<float>());
    }

    // KNOWN ISSUE: Disable() does not clear _interactionStates, so re-enabling with a key
    // still held can trigger a Hold immediately with stale timing. Captured here as
    // documentation of current behavior; see docs/PLAN_10_DE_10.md Fase 6 for the fix.
    [Fact]
    public void Disable_DuringHold_ReEnableWithKeyHeld_TriggersHoldImmediately()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Hold", InputActionType.Button);
        action.AddBinding(KeyCode.Space, InputInteractionType.Hold); // default HoldDuration 0.4s
        var log = CapturePhases(action);
        action.Enable();

        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.0f);
        action.UpdateState(handler, 0.2f);
        Assert.Empty(log); // press recorded, but the hold duration is not met yet

        action.Disable();
        action.Enable(); // _interactionStates survive: PressStartTime is still 0.0

        // The key was never released. With fresh state the hold would restart at t=1.0 and
        // need another 0.4s; the stale PressStartTime=0.0 makes it fire on this very update.
        action.UpdateState(handler, 1.0f);

        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
    }

    // KNOWN ISSUE: Disable() resets phase and values without raising Canceled, so listeners
    // never learn a mid-performance action stopped. Captured here as documentation of current
    // behavior; see docs/PLAN_10_DE_10.md Fase 6 for the fix.
    [Fact]
    public void Disable_WhilePerformed_DoesNotEmitCanceled()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Jump", InputActionType.Button);
        action.AddBinding(KeyCode.Space);
        var log = CapturePhases(action);
        action.Enable();

        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.0f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log); // sanity: it is performing
        log.Clear();

        action.Disable();

        Assert.Empty(log); // ← the known issue: no Canceled event on disable
        Assert.Equal(InputActionPhase.Disabled, action.Phase);
        Assert.Equal(0.0f, action.ReadValue<float>());
    }
}
