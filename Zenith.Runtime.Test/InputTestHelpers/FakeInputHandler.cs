// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Runtime.Test;

/// <summary>
/// A pure, fully controllable <see cref="IInputHandler"/> for tests: no window, no Silk.NET,
/// no device polling — every query reads state the test sets directly
/// (<see cref="PressKey"/>, <see cref="SetMouseButton"/>, <see cref="SetGamepadTrigger"/>, ...).
/// Events are never raised (nothing raises them); everything else starts at the same safe
/// defaults <see cref="NullInputHandler"/> reports.
/// </summary>
internal sealed class FakeInputHandler : IInputHandler
{
    private readonly HashSet<KeyCode> _keys = [];
    private readonly HashSet<KeyCode> _keysDown = [];
    private readonly HashSet<KeyCode> _keysUp = [];

    private readonly HashSet<int> _mouseButtons = [];
    private readonly HashSet<int> _mouseButtonsDown = [];
    private readonly HashSet<int> _mouseButtonsUp = [];

    private readonly HashSet<(int Gamepad, GamepadButton Button)> _gamepadButtons = [];
    private readonly Dictionary<(int Gamepad, int Trigger), float> _triggers = [];
    private readonly Dictionary<(int Gamepad, int Axis), Float2> _axes = [];

    public string Clipboard { get; set; } = string.Empty;
    public bool IsAnyKeyDown => _keys.Count > 0;
    public Float2 MouseDelta { get; set; } = Float2.Zero;
    public Int2 MousePosition { get; set; } = Int2.Zero;
    public float MouseWheelDelta { get; set; }
    public Int2 PrevMousePosition => Int2.Zero;
    public int GamepadCount { get; set; } = 1;

    public event Action<KeyCode, bool> OnKeyEvent { add { } remove { } }
    public event Action<MouseButton, float, float, bool, bool> OnMouseEvent { add { } remove { } }

    public char? GetPressedChar() => null;
    public string InputString => string.Empty;

    // ====================================================================
    // Keyboard
    // ====================================================================

    public void PressKey(KeyCode key)
    {
        _keys.Add(key);
        _keysDown.Add(key);
        _keysUp.Remove(key);
    }

    public void ReleaseKey(KeyCode key)
    {
        _keys.Remove(key);
        _keysUp.Add(key);
        _keysDown.Remove(key);
    }

    public void ClearKeyTransitions()
    {
        _keysDown.Clear();
        _keysUp.Clear();
    }

    public bool GetKey(KeyCode key) => _keys.Contains(key);
    public bool GetKeyDown(KeyCode key) => _keysDown.Contains(key);
    public bool GetKeyUp(KeyCode key) => _keysUp.Contains(key);

    // ====================================================================
    // Mouse
    // ====================================================================

    public void SetMouseButton(int button, bool pressed)
    {
        if (pressed)
        {
            _mouseButtons.Add(button);
            _mouseButtonsDown.Add(button);
            _mouseButtonsUp.Remove(button);
        }
        else
        {
            _mouseButtons.Remove(button);
            _mouseButtonsUp.Add(button);
            _mouseButtonsDown.Remove(button);
        }
    }

    public void SetMouseDelta(Float2 delta) => MouseDelta = delta;
    public void SetMouseWheel(float delta) => MouseWheelDelta = delta;

    // KNOWN ISSUE: sin esta limpieza los bordes "down/up" duran indefinidamente y un botón
    // mantenido re-dispara OnPointerDown cada frame - igual que ClearKeyTransitions para teclas.
    // See docs/PLAN_10_DE_10.md Fase 6. (H-UI-9)
    public void ClearMouseButtonTransitions()
    {
        _mouseButtonsDown.Clear();
        _mouseButtonsUp.Clear();
    }

    public bool GetMouseButton(int button) => _mouseButtons.Contains(button);
    public bool GetMouseButtonDown(int button) => _mouseButtonsDown.Contains(button);
    public bool GetMouseButtonUp(int button) => _mouseButtonsUp.Contains(button);

    public void ApplyCursorState(bool visible, CursorLockMode mode) { }
    public void SetCursorShape(PaperCursor shape, int miceIndex = 0) { }

    // ====================================================================
    // Gamepad
    // ====================================================================

    public void SetGamepadButton(int gamepadIndex, GamepadButton button, bool pressed)
    {
        if (pressed)
            _gamepadButtons.Add((gamepadIndex, button));
        else
            _gamepadButtons.Remove((gamepadIndex, button));
    }

    public void SetGamepadTrigger(int gamepadIndex, int triggerIndex, float value) =>
        _triggers[(gamepadIndex, triggerIndex)] = value;

    public void SetGamepadAxis(int gamepadIndex, int axisIndex, Float2 value) =>
        _axes[(gamepadIndex, axisIndex)] = value;

    public int GetGamepadCount() => GamepadCount;
    public int GetGamepadSlotCount() => GamepadCount;
    public bool IsGamepadConnected(int gamepadIndex) => gamepadIndex >= 0 && gamepadIndex < GamepadCount;

    public bool GetGamepadButton(int gamepadIndex, GamepadButton button) =>
        _gamepadButtons.Contains((gamepadIndex, button));

    // Button-down/up transitions are not tracked per gamepad button: held state is reported for both.
    public bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button) => GetGamepadButton(gamepadIndex, button);
    public bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button) => !GetGamepadButton(gamepadIndex, button);

    public Float2 GetGamepadAxis(int gamepadIndex, int axisIndex) =>
        _axes.TryGetValue((gamepadIndex, axisIndex), out Float2 value) ? value : Float2.Zero;

    public float GetGamepadTrigger(int gamepadIndex, int triggerIndex) =>
        _triggers.TryGetValue((gamepadIndex, triggerIndex), out float value) ? value : 0f;

    public void SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor) { }
}
