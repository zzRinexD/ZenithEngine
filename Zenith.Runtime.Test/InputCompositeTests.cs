// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the composite bindings (<see cref="Vector2CompositeBinding"/>,
/// <see cref="AxisCompositeBinding"/>, <see cref="DualAxisCompositeBinding"/>) both read
/// standalone and as the value source of an action.
/// </summary>
public class InputCompositeTests
{
    private static Vector2CompositeBinding CreateWasd() => new(
        InputBinding.CreateKeyBinding(KeyCode.W),
        InputBinding.CreateKeyBinding(KeyCode.S),
        InputBinding.CreateKeyBinding(KeyCode.A),
        InputBinding.CreateKeyBinding(KeyCode.D));

    [Theory]
    [InlineData(false, false, false, false, 0f, 0f)]                    // idle
    [InlineData(true, false, false, false, 0f, 1f)]                    // up
    [InlineData(false, false, false, true, 1f, 0f)]                    // right
    [InlineData(true, false, false, true, 0.70710677f, 0.70710677f)]   // diagonal, normalized to magnitude 1
    [InlineData(true, true, false, false, 0f, 0f)]                      // up+down cancel out
    public void Vector2Composite_ReadsDirectionsAndNormalizesDiagonal(
        bool up, bool down, bool left, bool right, float expectedX, float expectedY)
    {
        var composite = CreateWasd();
        Assert.All(composite.Parts, part => Assert.Equal(part.Key, part.Value.CompositePartName));

        var handler = new FakeInputHandler();
        if (up) handler.PressKey(KeyCode.W);
        if (down) handler.PressKey(KeyCode.S);
        if (left) handler.PressKey(KeyCode.A);
        if (right) handler.PressKey(KeyCode.D);

        var value = (Float2)composite.ReadValue(handler);

        Assert.Equal(expectedX, value.X, 5);
        Assert.Equal(expectedY, value.Y, 5);
    }

    [Fact]
    public void Vector2Composite_WithoutNormalize_KeepsDiagonalMagnitude()
    {
        var composite = new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W),
            InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A),
            InputBinding.CreateKeyBinding(KeyCode.D),
            normalize: false);
        Assert.False(composite.Normalize);

        var handler = new FakeInputHandler();
        handler.PressKey(KeyCode.W);
        handler.PressKey(KeyCode.D);

        var value = (Float2)composite.ReadValue(handler);
        Assert.Equal(1f, value.X);
        Assert.Equal(1f, value.Y);
    }

    [Theory]
    [InlineData(true, false, 1f)]
    [InlineData(false, true, -1f)]
    [InlineData(true, true, 0f)]  // both pressed → cancel
    [InlineData(false, false, 0f)]
    public void AxisComposite_CombinesPositiveAndNegative(bool positive, bool negative, float expected)
    {
        var composite = new AxisCompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.D),
            InputBinding.CreateKeyBinding(KeyCode.A));

        var handler = new FakeInputHandler();
        if (positive) handler.PressKey(KeyCode.D);
        if (negative) handler.PressKey(KeyCode.A);

        Assert.Equal(expected, (float)composite.ReadValue(handler));
    }

    [Fact]
    public void DualAxisComposite_ReadsTriggersAndMouseAxes_IgnoresKeys()
    {
        var handler = new FakeInputHandler();

        var triggers = new DualAxisCompositeBinding(
            InputBinding.CreateGamepadTriggerBinding(0),
            InputBinding.CreateGamepadTriggerBinding(1));
        handler.SetGamepadTrigger(0, 0, 0.5f);
        handler.SetGamepadTrigger(0, 1, 0.25f);
        var triggerValue = (Float2)triggers.ReadValue(handler);
        Assert.Equal(0.5f, triggerValue.X);
        Assert.Equal(0.25f, triggerValue.Y);

        var mouse = new DualAxisCompositeBinding(
            InputBinding.CreateMouseAxisBinding(0),
            InputBinding.CreateMouseAxisBinding(1));
        handler.SetMouseDelta(new Float2(0.3f, 0.7f));
        var mouseValue = (Float2)mouse.ReadValue(handler);
        Assert.Equal(0.3f, mouseValue.X);
        Assert.Equal(0.7f, mouseValue.Y);

        // Plain keys are not a supported DualAxis part type → both axes read 0.
        var keys = new DualAxisCompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.D),
            InputBinding.CreateKeyBinding(KeyCode.W));
        handler.PressKey(KeyCode.D);
        handler.PressKey(KeyCode.W);
        Assert.Equal(Float2.Zero, (Float2)keys.ReadValue(handler));
    }

    /// <summary>
    /// The composite wins while it is actuated; once idle the action falls back to the plain
    /// bindings. Note the fallback raw value is a float while the action expects Float2 — the
    /// mixed-type path is H1 debt scheduled in Fase 6, so it is only read as an object here.
    /// </summary>
    [Fact]
    public void Action_UsesCompositeValueWhenActuated_AndFallsBackToBindings()
    {
        var handler = new FakeInputHandler();
        var action = new InputAction("Move", InputActionType.Value);
        action.ExpectedValueType = typeof(Float2);
        action.AddBinding(CreateWasd());
        action.AddBinding(KeyCode.Space);
        action.Enable();

        handler.PressKey(KeyCode.W);
        action.UpdateState(handler, 0.0f);
        Assert.Equal(new Float2(0f, 1f), action.ReadValue<Float2>());
        Assert.Equal(InputActionPhase.Performed, action.Phase);

        handler.ReleaseKey(KeyCode.W);
        handler.PressKey(KeyCode.Space);
        action.UpdateState(handler, 0.1f);
        Assert.Equal(1.0f, (float)action.ReadValueAsObject());
        Assert.Equal(InputActionPhase.Performed, action.Phase);
    }
}
