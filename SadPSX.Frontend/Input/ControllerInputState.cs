using SadPSX.Core.Controllers;

namespace SadPSX.Frontend.Input;

internal sealed class ControllerInputState
{
    private const byte NegativeDirectionThreshold = 0x50;
    private const byte PositiveDirectionThreshold = 0xB0;

    private IController _controller;
    private readonly HashSet<ControllerButton> _keyboardButtons = [];
    private readonly HashSet<ControllerButton> _gamepadButtons = [];
    private readonly HashSet<ControllerButton> _axisButtons = [];
    private readonly byte[] _gamepadAxes =
        Enumerable.Repeat((byte)0x80, 4).ToArray();

    public ControllerInputState(IController controller)
    {
        _controller = controller ??
            throw new ArgumentNullException(nameof(controller));
    }

    public void SetKeyboardButton(ControllerButton button, bool pressed)
    {
        SetButton(_keyboardButtons, button, pressed);
    }

    public void SetController(IController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller.ReleaseAll();
        _controller = controller;

        foreach (ControllerButton button in Enum.GetValues<ControllerButton>())
            ApplyButton(button);
        foreach (ControllerAxis axis in Enum.GetValues<ControllerAxis>())
            _controller.SetAxis(axis, _gamepadAxes[(int)axis]);
    }

    public void SetGamepadButton(ControllerButton button, bool pressed)
    {
        SetButton(_gamepadButtons, button, pressed);
    }

    public void ReleaseGamepad()
    {
        ControllerButton[] buttons =
            [.. _gamepadButtons.Concat(_axisButtons).Distinct()];
        _gamepadButtons.Clear();
        _axisButtons.Clear();
        foreach (ControllerButton button in buttons)
            ApplyButton(button);

        foreach (ControllerAxis axis in Enum.GetValues<ControllerAxis>())
        {
            _gamepadAxes[(int)axis] = 0x80;
            _controller.SetAxis(axis, 0x80);
        }
    }

    public void SetGamepadAxis(ControllerAxis axis, byte value)
    {
        _gamepadAxes[(int)axis] = value;
        _controller.SetAxis(axis, value);

        switch (axis)
        {
            case ControllerAxis.LeftX:
                SetButton(
                    _axisButtons,
                    ControllerButton.Left,
                    value <= NegativeDirectionThreshold);
                SetButton(
                    _axisButtons,
                    ControllerButton.Right,
                    value >= PositiveDirectionThreshold);
                break;

            case ControllerAxis.LeftY:
                SetButton(
                    _axisButtons,
                    ControllerButton.Up,
                    value <= NegativeDirectionThreshold);
                SetButton(
                    _axisButtons,
                    ControllerButton.Down,
                    value >= PositiveDirectionThreshold);
                break;
        }
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
            _gamepadButtons.Contains(button) ||
            _axisButtons.Contains(button));
    }
}
