using SadPSX.Core.Controllers;
using SDL3;

namespace SadPSX.Frontend.Input;

internal sealed class SdlGamepadInput : IDisposable
{
    private const short TriggerThreshold = 16_000;

    private readonly ControllerInputState _inputState;
    private readonly HashSet<uint> _knownGamepads = [];

    private nint _gamepad;
    private uint _gamepadId;
    private bool _leftTriggerPressed;
    private bool _rightTriggerPressed;

    public SdlGamepadInput(ControllerInputState inputState)
    {
        _inputState = inputState ??
            throw new ArgumentNullException(nameof(inputState));
    }

    public void HandleDeviceAdded(uint instanceId)
    {
        _knownGamepads.Add(instanceId);
        if (_gamepad == 0)
            OpenGamepad(instanceId);
    }

    public void HandleDeviceRemoved(uint instanceId)
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

    public void HandleButton(
        uint instanceId,
        SDL.GamepadButton button,
        bool pressed)
    {
        if (instanceId != _gamepadId)
            return;

        ControllerButton? mappedButton = button switch
        {
            SDL.GamepadButton.South => ControllerButton.Cross,
            SDL.GamepadButton.East => ControllerButton.Circle,
            SDL.GamepadButton.West => ControllerButton.Square,
            SDL.GamepadButton.North => ControllerButton.Triangle,
            SDL.GamepadButton.Back => ControllerButton.Select,
            SDL.GamepadButton.Start => ControllerButton.Start,
            SDL.GamepadButton.LeftStick => ControllerButton.L3,
            SDL.GamepadButton.RightStick => ControllerButton.R3,
            SDL.GamepadButton.LeftShoulder => ControllerButton.L1,
            SDL.GamepadButton.RightShoulder => ControllerButton.R1,
            SDL.GamepadButton.DPadUp => ControllerButton.Up,
            SDL.GamepadButton.DPadRight => ControllerButton.Right,
            SDL.GamepadButton.DPadDown => ControllerButton.Down,
            SDL.GamepadButton.DPadLeft => ControllerButton.Left,
            _ => null,
        };

        if (mappedButton is ControllerButton controllerButton)
            _inputState.SetGamepadButton(controllerButton, pressed);
    }

    public void HandleAxis(
        uint instanceId,
        SDL.GamepadAxis axis,
        short value)
    {
        if (instanceId != _gamepadId)
            return;

        switch (axis)
        {
            case SDL.GamepadAxis.LeftTrigger:
                SetTrigger(
                    ControllerButton.L2,
                    value >= TriggerThreshold,
                    ref _leftTriggerPressed);
                break;

            case SDL.GamepadAxis.RightTrigger:
                SetTrigger(
                    ControllerButton.R2,
                    value >= TriggerThreshold,
                    ref _rightTriggerPressed);
                break;
        }
    }

    public void Dispose()
    {
        CloseCurrentGamepad();
    }

    private bool OpenGamepad(uint instanceId)
    {
        nint gamepad = SDL.OpenGamepad(instanceId);
        if (gamepad == 0)
        {
            Console.Error.WriteLine(
                $"Falha ao abrir gamepad {instanceId}: {SDL.GetError()}");
            return false;
        }

        _gamepad = gamepad;
        _gamepadId = instanceId;
        Console.WriteLine(
            $"Gamepad conectado: {SDL.GetGamepadName(gamepad)}");
        return true;
    }

    private void CloseCurrentGamepad()
    {
        if (_gamepad == 0)
            return;

        SDL.CloseGamepad(_gamepad);
        _gamepad = 0;
        _gamepadId = 0;
        _leftTriggerPressed = false;
        _rightTriggerPressed = false;
        _inputState.ReleaseGamepad();
        Console.WriteLine("Gamepad desconectado.");
    }

    private void SetTrigger(
        ControllerButton button,
        bool pressed,
        ref bool currentState)
    {
        if (pressed == currentState)
            return;

        currentState = pressed;
        _inputState.SetGamepadButton(button, pressed);
    }
}
