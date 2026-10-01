// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for <see cref="InputAction"/>: the phase lifecycle, enable/disable, value-type
/// validation, binding management and the read-before-enable edges. All input comes from a
/// <see cref="FakeInputHandler"/> driven directly through the internal
/// <c>UpdateState(handler, time)</c> — no window, no device, no global <see cref="Input"/>.
/// </summary>
public class InputActionTests
{
    private static List<InputActionPhase> CapturePhases(InputAction action)
    {
        var log = new List<InputActionPhase>();
        action.Started += _ => log.Add(InputActionPhase.Started);
        action.Performed += _ => log.Add(InputActionPhase.Performed);
        action.Canceled += _ => log.Add(InputActionPhase.Canceled);
        return log;
    }

    /// <summary>Disabled → Waiting → Started → Performed → (re-performs while held) → Canceled → Waiting.</summary>
    [Fact]
    public void Button_LifecycleSequence()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Jump", InputActionType.Button);
        action.AddBinding(KeyCode.Space);
        var log = CapturePhases(action);

        Assert.Equal(InputActionPhase.Disabled, action.Phase);
        Assert.False(action.Enabled);

        action.Enable();
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
        Assert.True(action.Enabled);

        // Press: Waiting → Started → Performed within a single update.
        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.0f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
        Assert.Equal(InputActionPhase.Performed, action.Phase);
        Assert.Equal(1.0f, action.ReadValue<float>());
        Assert.True(action.IsPressed());
        Assert.True(action.WasPressedThisFrame());
        Assert.False(action.WasReleasedThisFrame());

        // Documented behavior: a Button with the Default interaction re-performs every frame it is held.
        log.Clear();
        action.UpdateState(handler, 0.1f);
        Assert.Equal(new[] { InputActionPhase.Performed }, log);
        Assert.Equal(InputActionPhase.Performed, action.Phase);

        // Release → Canceled, back to Waiting.
        log.Clear();
        handler.ReleaseKey(KeyCode.Space);
        action.UpdateState(handler, 0.2f);
        Assert.Equal(new[] { InputActionPhase.Canceled }, log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
        Assert.Equal(0.0f, action.ReadValue<float>());
        Assert.True(action.WasReleasedThisFrame());
        Assert.False(action.IsPressed());

        // Idle frame: nothing happens.
        log.Clear();
        action.UpdateState(handler, 0.3f);
        Assert.Empty(log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
    }

    /// <summary>Callbacks receive a context carrying the action, phase, time and duration.</summary>
    [Fact]
    public void Callbacks_ReceiveContext()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Fire", InputActionType.Button);
        action.AddBinding(KeyCode.Space);

        InputActionContext started = default, performed = default, canceled = default;
        action.Started += ctx => started = ctx;
        action.Performed += ctx => performed = ctx;
        action.Canceled += ctx => canceled = ctx;

        action.Enable();

        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 1.0f);

        Assert.Same(action, started.Action);
        Assert.Equal(InputActionPhase.Started, started.Phase);
        Assert.True(started.IsStarted);
        Assert.False(started.IsPerformed);
        Assert.False(started.IsCanceled);
        Assert.Equal(1.0f, started.Time);
        Assert.Equal(0.0f, started.Duration);
        Assert.Equal(1.0f, started.ReadValue<float>());
        Assert.Equal(1.0f, started.ReadRawValue<float>()); // raw == value today (InputAction.InvokeCallback)
        Assert.Equal(1.0f, (float)started.ReadValueAsObject());

        Assert.Same(action, performed.Action);
        Assert.True(performed.IsPerformed);
        Assert.Equal(1.0f, performed.Time);
        Assert.Equal(0.0f, performed.Duration); // started and performed in the same update

        handler.ReleaseKey(KeyCode.Space);
        action.UpdateState(handler, 2.0f);

        Assert.Same(action, canceled.Action);
        Assert.True(canceled.IsCanceled);
        Assert.Equal(2.0f, canceled.Time);
        Assert.Equal(1.0f, canceled.Duration); // performed from t=1.0 until canceled at t=2.0
    }

    [Fact]
    public void Enable_IsIdempotent_AndStartsInWaiting()
    {
        var action = new InputAction("Anything");

        Assert.Equal(InputActionPhase.Disabled, action.Phase);
        Assert.False(action.Enabled);

        action.Enable();
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
        Assert.True(action.Enabled);

        action.Enable(); // already enabled → no-op
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
        Assert.True(action.Enabled);
    }

    [Fact]
    public void Disable_ResetsPhaseAndValues_ReEnableRestartsFromWaiting()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Jump");
        action.AddBinding(KeyCode.Space);
        var log = CapturePhases(action);
        action.Enable();

        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.5f);
        Assert.Equal(InputActionPhase.Performed, action.Phase);
        Assert.Equal(1.0f, action.ReadValue<float>());
        Assert.True(action.WasPressedThisFrame());
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);

        action.Disable();
        Assert.Equal(InputActionPhase.Disabled, action.Phase);
        Assert.False(action.Enabled);
        Assert.Equal(0.0f, action.ReadValue<float>());
        Assert.Equal(0.0f, (float)action.ReadValueAsObject());
        Assert.False(action.IsPressed());
        Assert.False(action.WasPressedThisFrame());

        log.Clear();
        action.UpdateState(handler, 0.6f); // disabled → update ignored entirely
        Assert.Empty(log);
        Assert.Equal(InputActionPhase.Disabled, action.Phase);

        action.Enable();
        Assert.Equal(InputActionPhase.Waiting, action.Phase);

        // Key was never released: starts the cycle again from scratch.
        action.UpdateState(handler, 0.7f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
        Assert.True(action.WasPressedThisFrame());
    }

    [Fact]
    public void ExpectedValueType_RejectsOtherTypes()
    {
        var action = new InputAction("Move");
        Assert.Equal(typeof(float), action.ExpectedValueType);

        Assert.Throws<ArgumentException>(() => action.ExpectedValueType = typeof(double));
        Assert.Throws<ArgumentException>(() => action.ExpectedValueType = typeof(int));
        Assert.Throws<ArgumentException>(() => action.ExpectedValueType = typeof(Float3));

        Assert.Equal(typeof(float), action.ExpectedValueType); // rejected sets change nothing

        action.ExpectedValueType = typeof(Float2);
        Assert.Equal(typeof(Float2), action.ExpectedValueType);
        action.ExpectedValueType = typeof(float);
        Assert.Equal(typeof(float), action.ExpectedValueType);
    }

    [Fact]
    public void ExpectedValueType_SwitchToFloat2_ResetsCurrentAndDefaults()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Move", InputActionType.Value);
        action.AddBinding(KeyCode.Space);
        action.Enable();

        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.0f);
        Assert.Equal(1.0f, (float)action.ReadValueAsObject());

        action.ExpectedValueType = typeof(Float2);

        Assert.Equal(typeof(Float2), action.ExpectedValueType);
        Assert.Equal(Float2.Zero, action.ReadValue<Float2>());
        Assert.Equal(Float2.Zero, (Float2)action.ReadValueAsObject());

        // A fresh action starts on the float default.
        var fresh = new InputAction("Fresh");
        Assert.Equal(typeof(float), fresh.ExpectedValueType);
        Assert.Equal(0.0f, (float)fresh.ReadValueAsObject());
    }

    [Fact]
    public void AddBinding_IsFluent_SupportsDuplicates_AndRemoval()
    {
        var action = new InputAction("Shoot");

        var returned = action.AddBinding(KeyCode.Space);
        Assert.Same(action, returned); // fluent

        action.AddBinding(KeyCode.Space); // duplicates are not deduplicated
        action.AddBinding(MouseButton.Left);
        action.AddBinding(GamepadButton.A, deviceIndex: 1);

        Assert.Equal(4, action.Bindings.Count);
        Assert.Equal(2, action.Bindings.Count(b => b.BindingType == InputBindingType.Key && b.Key == KeyCode.Space));
        Assert.Equal(MouseButton.Left, action.Bindings[2].MouseButton);
        Assert.Equal(GamepadButton.A, action.Bindings[3].GamepadButton);
        Assert.Equal(1, action.Bindings[3].RequiredDeviceIndex);

        Assert.True(action.Bindings.Remove(action.Bindings[1]));
        Assert.Equal(3, action.Bindings.Count);
        Assert.Equal(1, action.Bindings.Count(b => b.BindingType == InputBindingType.Key && b.Key == KeyCode.Space));

        // Composites live in their own list, with index-based removal.
        var composite = new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W),
            InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A),
            InputBinding.CreateKeyBinding(KeyCode.D));
        action.AddBinding(composite);
        Assert.Single(action.CompositeBindings);

        action.RemoveCompositeAt(0);
        Assert.Empty(action.CompositeBindings);
        Assert.Throws<ArgumentOutOfRangeException>(() => action.RemoveCompositeAt(0));
    }

    [Fact]
    public void ActionWithoutBindings_UpdatesWithoutTriggering()
    {
        var handler = new FakeInputHandler();
        handler.PressKey(KeyCode.Space); // input exists, but nothing listens

        var action = new InputAction("Empty");
        var log = CapturePhases(action);
        action.Enable();

        action.UpdateState(handler, 0.0f);
        action.UpdateState(handler, 0.1f);

        Assert.Empty(action.Bindings);
        Assert.Empty(action.CompositeBindings);
        Assert.Empty(log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
        Assert.Equal(0.0f, action.ReadValue<float>());
    }

    [Fact]
    public void ReadValueBeforeEnable_ReturnsDefault_AndUpdateStateIsNoOp()
    {
        var handler = new FakeInputHandler();
        handler.PressKey(KeyCode.Space);

        var action = new InputAction("Jump");
        action.AddBinding(KeyCode.Space);

        Assert.False(action.Enabled);
        Assert.Equal(InputActionPhase.Disabled, action.Phase);
        Assert.Equal(0.0f, action.ReadValue<float>());
        Assert.Equal(0.0f, (float)action.ReadValueAsObject());
        Assert.False(action.IsPressed());
        Assert.False(action.WasPressedThisFrame());
        Assert.False(action.WasReleasedThisFrame());

        action.UpdateState(handler, 0.0f); // disabled → ignored entirely

        Assert.Equal(InputActionPhase.Disabled, action.Phase);
        Assert.Equal(0.0f, (float)action.ReadValueAsObject());
    }

    [Fact]
    public void ExpectedValueType_AfterBindings_ResetsValueButKeepsPhase()
    {
        var handler = new FakeInputHandler();
        handler.PressKey(KeyCode.Space);

        var action = new InputAction("Move", InputActionType.Value);
        action.AddBinding(KeyCode.Space);
        action.Enable();
        action.UpdateState(handler, 0.0f);

        Assert.Equal(1.0f, (float)action.ReadValueAsObject());
        Assert.Equal(InputActionPhase.Performed, action.Phase);

        action.ExpectedValueType = typeof(Float2);

        Assert.Equal(Float2.Zero, (Float2)action.ReadValueAsObject());
        Assert.Equal(InputActionPhase.Performed, action.Phase); // the setter does not touch the phase
        Assert.True(action.Enabled);
    }

    /// <summary>
    /// Value actions trigger on change rather than on actuation: holding still cancels the very
    /// next frame, and releasing to zero is itself a change that starts and performs again.
    /// </summary>
    [Fact]
    public void ValueAction_PerformsOnEveryChange_CancelsWhenStable()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Move", InputActionType.Value);
        action.AddBinding(KeyCode.W);
        var log = CapturePhases(action);
        action.Enable();

        handler.PressKey(KeyCode.W);
        action.UpdateState(handler, 0.0f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
        Assert.Equal(InputActionPhase.Performed, action.Phase);
        Assert.Equal(1.0f, action.ReadValue<float>());

        // Still held: the value did not change → a Value action cancels instead of re-performing.
        log.Clear();
        action.UpdateState(handler, 0.1f);
        Assert.Equal(new[] { InputActionPhase.Canceled }, log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
        Assert.Equal(1.0f, action.ReadValue<float>()); // the value persists even though the phase reset

        log.Clear();
        action.UpdateState(handler, 0.2f);
        Assert.Empty(log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);

        // Releasing is a change (1 → 0): the cycle starts again — with the zero value.
        log.Clear();
        handler.ReleaseKey(KeyCode.W);
        action.UpdateState(handler, 0.3f);
        Assert.Equal(new[] { InputActionPhase.Started, InputActionPhase.Performed }, log);
        Assert.Equal(InputActionPhase.Performed, action.Phase);
        Assert.Equal(0.0f, action.ReadValue<float>());

        // Stable at zero → Canceled, then Waiting.
        log.Clear();
        action.UpdateState(handler, 0.4f);
        Assert.Equal(new[] { InputActionPhase.Canceled }, log);
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
    }
}
