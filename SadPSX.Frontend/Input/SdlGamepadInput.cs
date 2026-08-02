using SadPSX.Core.Controllers;
using SDL3;

namespace SadPSX.Frontend.Input;

internal sealed class SdlGamepadInput : IDisposable
{
    private const short TriggerThreshold = 16_000;

    private readonly ControllerInputState _inputState;
    private readonly GamepadMapping _mapping;
    private readonly HashSet<uint> _knownGamepads = [];

    private nint _gamepad;
    private uint _gamepadId;

    public SdlGamepadInput(
        ControllerInputState inputState,
        GamepadMapping? mapping = null)
    {
        _inputState = inputState ??
            throw new ArgumentNullException(nameof(inputState));
        _mapping = mapping ?? GamepadMapping.Default;
        OpenConnectedGamepads();
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

        foreach (ControllerButton target in Enum.GetValues<ControllerButton>())
        {
            if (GamepadMapping.TryGetButton(
                    _mapping.GetBinding(target),
                    out SDL.GamepadButton source) &&
                source == button)
            {
                _inputState.SetGamepadButton(target, pressed);
            }
        }
    }

    public void Poll()
    {
        if (_gamepad == 0)
            return;

        foreach (ControllerButton target in Enum.GetValues<ControllerButton>())
        {
            _inputState.SetGamepadButton(
                target,
                IsBindingPressed(_mapping.GetBinding(target)));
        }

        PollAxis(SDL.GamepadAxis.LeftX, ControllerAxis.LeftX);
        PollAxis(SDL.GamepadAxis.LeftY, ControllerAxis.LeftY);
        PollAxis(SDL.GamepadAxis.RightX, ControllerAxis.RightX);
        PollAxis(SDL.GamepadAxis.RightY, ControllerAxis.RightY);
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
            case SDL.GamepadAxis.LeftX:
                _inputState.SetGamepadAxis(
                    ControllerAxis.LeftX,
                    ConvertAxis(value));
                break;
            case SDL.GamepadAxis.LeftY:
                _inputState.SetGamepadAxis(
                    ControllerAxis.LeftY,
                    ConvertAxis(value));
                break;
            case SDL.GamepadAxis.RightX:
                _inputState.SetGamepadAxis(
                    ControllerAxis.RightX,
                    ConvertAxis(value));
                break;
            case SDL.GamepadAxis.RightY:
                _inputState.SetGamepadAxis(
                    ControllerAxis.RightY,
                    ConvertAxis(value));
                break;
        }

        if (axis is not (SDL.GamepadAxis.LeftTrigger or
            SDL.GamepadAxis.RightTrigger))
        {
            return;
        }

        foreach (ControllerButton target in Enum.GetValues<ControllerButton>())
        {
            if (GamepadMapping.TryGetTrigger(
                    _mapping.GetBinding(target),
                    out SDL.GamepadAxis source) &&
                source == axis)
            {
                _inputState.SetGamepadButton(
                    target,
                    value >= TriggerThreshold);
            }
        }
    }

    public void Dispose()
    {
        CloseCurrentGamepad();
    }

    private bool IsBindingPressed(GamepadBinding binding)
    {
        if (GamepadMapping.TryGetButton(
                binding,
                out SDL.GamepadButton button))
        {
            return SDL.GetGamepadButton(_gamepad, button);
        }

        return GamepadMapping.TryGetTrigger(binding, out SDL.GamepadAxis axis) &&
               SDL.GetGamepadAxis(_gamepad, axis) >= TriggerThreshold;
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
            if (_gamepad == 0)
                OpenGamepad(instanceId);
        }
    }

    private void CloseCurrentGamepad()
    {
        if (_gamepad == 0)
            return;

        SDL.CloseGamepad(_gamepad);
        _gamepad = 0;
        _gamepadId = 0;
        _inputState.ReleaseGamepad();
        Console.WriteLine("Gamepad desconectado.");
    }

    private void PollAxis(SDL.GamepadAxis source, ControllerAxis target)
    {
        short value = SDL.GetGamepadAxis(_gamepad, source);
        _inputState.SetGamepadAxis(target, ConvertAxis(value));
    }

    private static byte ConvertAxis(short value) =>
        (byte)(((int)value - short.MinValue + 128) / 257);
}
