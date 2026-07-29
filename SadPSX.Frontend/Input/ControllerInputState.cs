using SadPSX.Core.Controllers;

namespace SadPSX.Frontend.Input;

internal sealed class ControllerInputState
{
    private readonly IController _controller;
    private readonly HashSet<ControllerButton> _keyboardButtons = [];
    private readonly HashSet<ControllerButton> _gamepadButtons = [];

    public ControllerInputState(IController controller)
    {
        _controller = controller ??
            throw new ArgumentNullException(nameof(controller));
    }

    public void SetKeyboardButton(ControllerButton button, bool pressed)
    {
        SetButton(_keyboardButtons, button, pressed);
    }

    public void SetGamepadButton(ControllerButton button, bool pressed)
    {
        SetButton(_gamepadButtons, button, pressed);
    }

    public void ReleaseGamepad()
    {
        ControllerButton[] buttons = [.. _gamepadButtons];
        _gamepadButtons.Clear();
        foreach (ControllerButton button in buttons)
            ApplyButton(button);

        foreach (ControllerAxis axis in Enum.GetValues<ControllerAxis>())
            _controller.SetAxis(axis, 0x80);
    }

    public void SetGamepadAxis(ControllerAxis axis, byte value)
    {
        _controller.SetAxis(axis, value);
    }

    private void SetButton(
        HashSet<ControllerButton> source,
        ControllerButton button,
        bool pressed)
    {
        bool changed = pressed
            ? source.Add(button)
            : source.Remove(button);
        if (changed)
            ApplyButton(button);
    }

    private void ApplyButton(ControllerButton button)
    {
        _controller.SetButton(
            button,
            _keyboardButtons.Contains(button) ||
            _gamepadButtons.Contains(button));
    }
}
