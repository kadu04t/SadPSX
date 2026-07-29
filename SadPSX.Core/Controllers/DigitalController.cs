namespace SadPSX.Core.Controllers;

public sealed class DigitalController : IController
{
    private ushort _buttons = ushort.MaxValue;
    private int _transferIndex;
    private bool _polling;

    public ushort Buttons => _buttons;

    public bool IsPressed(ControllerButton button) =>
        (_buttons & (1 << (int)button)) == 0;

    public void SetButton(ControllerButton button, bool pressed)
    {
        ushort mask = (ushort)(1 << (int)button);
        _buttons = pressed
            ? (ushort)(_buttons & ~mask)
            : (ushort)(_buttons | mask);
    }

    public void ReleaseAll()
    {
        _buttons = ushort.MaxValue;
    }

    public void SetAxis(ControllerAxis axis, byte value)
    {
    }

    public void ResetTransfer()
    {
        _transferIndex = 0;
        _polling = false;
    }

    public ControllerTransferResult Transfer(byte value)
    {
        ControllerTransferResult result = _transferIndex switch
        {
            0 when value == 0x01 => new(0xFF, true),
            1 when value == 0x42 => new(0x41, true),
            2 when _polling => new(0x5A, true),
            3 when _polling => new((byte)_buttons, true),
            4 when _polling => new((byte)(_buttons >> 8), false),
            _ => new(0xFF, false),
        };

        if (_transferIndex == 0)
        {
            _polling = value == 0x01;
            _transferIndex = _polling ? 1 : 0;
        }
        else if (_transferIndex == 1)
        {
            _polling &= value == 0x42;
            _transferIndex = _polling ? 2 : 0;
        }
        else if (_polling && _transferIndex < 4)
        {
            _transferIndex++;
        }
        else
        {
            ResetTransfer();
        }

        return result;
    }
}
