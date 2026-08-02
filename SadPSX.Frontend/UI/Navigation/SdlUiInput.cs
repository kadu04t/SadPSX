using SDL3;

namespace SadPSX.Frontend.UI.Navigation;

internal sealed class SdlUiInput : IDisposable
{
    private const short AxisThreshold = 16_000;

    private readonly HashSet<uint> _knownGamepads = [];

    private nint _gamepad;
    private uint _gamepadId;
    private int _horizontalDirection;
    private int _verticalDirection;

    public SdlUiInput()
    {
        OpenConnectedGamepads();
    }

    public bool TryMapEvent(in SDL.Event currentEvent, out UiAction action)
    {
        action = default;
        switch ((SDL.EventType)currentEvent.Type)
        {
            case SDL.EventType.KeyDown when !currentEvent.Key.Repeat:
                return TryMapKey(currentEvent.Key.Scancode, out action);

            case SDL.EventType.GamepadAdded:
                HandleDeviceAdded(currentEvent.GDevice.Which);
                return false;

            case SDL.EventType.GamepadRemoved:
                HandleDeviceRemoved(currentEvent.GDevice.Which);
                return false;

            case SDL.EventType.GamepadButtonDown
                when currentEvent.GButton.Which == _gamepadId:
                return TryMapButton(
                    (SDL.GamepadButton)currentEvent.GButton.Button,
                    out action);

            case SDL.EventType.GamepadAxisMotion
                when currentEvent.GAxis.Which == _gamepadId:
                return TryMapAxis(
                    (SDL.GamepadAxis)currentEvent.GAxis.Axis,
                    currentEvent.GAxis.Value,
                    out action);

            default:
                return false;
        }
    }

    public void Dispose()
    {
        CloseCurrentGamepad();
    }

    private void HandleDeviceAdded(uint instanceId)
    {
        _knownGamepads.Add(instanceId);
        if (_gamepad == nint.Zero)
            OpenGamepad(instanceId);
    }

    private void HandleDeviceRemoved(uint instanceId)
    {
        _knownGamepads.Remove(instanceId);
        if (instanceId != _gamepadId)
            return;

        CloseCurrentGamepad();
        foreach (uint availableId in _knownGamepads)
        {
            if (OpenGamepad(availableId))
                break;
        }
    }

    private bool TryMapAxis(
        SDL.GamepadAxis axis,
        short value,
        out UiAction action)
    {
        action = default;
        int direction = value switch
        {
            <= -AxisThreshold => -1,
            >= AxisThreshold => 1,
            _ => 0,
        };

        if (axis == SDL.GamepadAxis.LeftX)
        {
            if (direction == 0)
            {
                _horizontalDirection = 0;
                return false;
            }

            if (direction == _horizontalDirection)
                return false;

            _horizontalDirection = direction;
            action = direction < 0 ? UiAction.Left : UiAction.Right;
            return true;
        }

        if (axis == SDL.GamepadAxis.LeftY)
        {
            if (direction == 0)
            {
                _verticalDirection = 0;
                return false;
            }

            if (direction == _verticalDirection)
                return false;

            _verticalDirection = direction;
            action = direction < 0 ? UiAction.Up : UiAction.Down;
            return true;
        }

        return false;
    }

    private bool OpenGamepad(uint instanceId)
    {
        nint gamepad = SDL.OpenGamepad(instanceId);
        if (gamepad == nint.Zero)
            return false;

        _gamepad = gamepad;
        _gamepadId = instanceId;
        return true;
    }

    private void OpenConnectedGamepads()
    {
        uint[]? gamepads = SDL.GetGamepads(out int gamepadCount);
        if (gamepads is null)
            return;

        int availableCount = Math.Min(gamepadCount, gamepads.Length);
        for (int index = 0; index < availableCount; index++)
        {
            uint instanceId = gamepads[index];
            _knownGamepads.Add(instanceId);
            if (_gamepad == nint.Zero)
                OpenGamepad(instanceId);
        }
    }

    private void CloseCurrentGamepad()
    {
        if (_gamepad == nint.Zero)
            return;

        SDL.CloseGamepad(_gamepad);
        _gamepad = nint.Zero;
        _gamepadId = 0;
        _horizontalDirection = 0;
        _verticalDirection = 0;
    }

    private static bool TryMapKey(
        SDL.Scancode scancode,
        out UiAction action)
    {
        action = scancode switch
        {
            SDL.Scancode.Up or SDL.Scancode.W => UiAction.Up,
            SDL.Scancode.Right or SDL.Scancode.D => UiAction.Right,
            SDL.Scancode.Down or SDL.Scancode.S => UiAction.Down,
            SDL.Scancode.Left or SDL.Scancode.A => UiAction.Left,
            SDL.Scancode.Return or SDL.Scancode.Space or SDL.Scancode.Z =>
                UiAction.Confirm,
            SDL.Scancode.Escape or SDL.Scancode.Backspace or SDL.Scancode.X =>
                UiAction.Back,
            _ => default,
        };

        return scancode is
            SDL.Scancode.Up or SDL.Scancode.W or
            SDL.Scancode.Right or SDL.Scancode.D or
            SDL.Scancode.Down or SDL.Scancode.S or
            SDL.Scancode.Left or SDL.Scancode.A or
            SDL.Scancode.Return or SDL.Scancode.Space or SDL.Scancode.Z or
            SDL.Scancode.Escape or SDL.Scancode.Backspace or SDL.Scancode.X;
    }

    private static bool TryMapButton(
        SDL.GamepadButton button,
        out UiAction action)
    {
        action = button switch
        {
            SDL.GamepadButton.DPadUp => UiAction.Up,
            SDL.GamepadButton.DPadRight => UiAction.Right,
            SDL.GamepadButton.DPadDown => UiAction.Down,
            SDL.GamepadButton.DPadLeft => UiAction.Left,
            SDL.GamepadButton.South => UiAction.Confirm,
            SDL.GamepadButton.East or SDL.GamepadButton.Back => UiAction.Back,
            SDL.GamepadButton.Start => UiAction.Menu,
            _ => default,
        };

        return button is
            SDL.GamepadButton.DPadUp or SDL.GamepadButton.DPadRight or
            SDL.GamepadButton.DPadDown or SDL.GamepadButton.DPadLeft or
            SDL.GamepadButton.South or SDL.GamepadButton.East or
            SDL.GamepadButton.Back or SDL.GamepadButton.Start;
    }
}
