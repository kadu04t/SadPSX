namespace SadPSX.Core.Controllers;

public sealed class AnalogController : IController
{
    private static readonly byte[] LedStatusReply =
        [0x01, 0x02, 0x00, 0x02, 0x01, 0x00];
    private static readonly byte[] FixedResponse =
        [0, 0, 2, 0, 1, 0];

    private readonly byte[] _rumbleMapping =
        [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
    private ushort _buttons = ushort.MaxValue;
    private int _transferIndex;
    private byte _command;
    private byte _configurationArgument;
    private bool _selected;
    private bool _configurationMode;
    private bool _pendingConfigurationMode;
    private bool _pendingAnalogMode;
    private bool _pendingAnalogModeLocked;

    public ushort Buttons => _buttons;
    public bool AnalogMode { get; private set; }
    public byte RightX { get; private set; } = 0x80;
    public byte RightY { get; private set; } = 0x80;
    public byte LeftX { get; private set; } = 0x80;
    public byte LeftY { get; private set; } = 0x80;
    public byte SmallMotor { get; private set; }
    public byte LargeMotor { get; private set; }
    public bool AnalogModeLocked { get; private set; }

    public bool IsPressed(ControllerButton button) =>
        (_buttons & (1 << (int)button)) == 0;

    public void SetButton(ControllerButton button, bool pressed)
    {
        ushort mask = (ushort)(1 << (int)button);
        _buttons = pressed
            ? (ushort)(_buttons & ~mask)
            : (ushort)(_buttons | mask);
    }

    public void SetAxis(ControllerAxis axis, byte value)
    {
        switch (axis)
        {
            case ControllerAxis.RightX:
                RightX = value;
                break;
            case ControllerAxis.RightY:
                RightY = value;
                break;
            case ControllerAxis.LeftX:
                LeftX = value;
                break;
            case ControllerAxis.LeftY:
                LeftY = value;
                break;
        }
    }

    public void SetAnalogMode(bool enabled)
    {
        AnalogMode = enabled;
    }

    public void ReleaseAll()
    {
        _buttons = ushort.MaxValue;
        RightX = 0x80;
        RightY = 0x80;
        LeftX = 0x80;
        LeftY = 0x80;
        SmallMotor = 0;
        LargeMotor = 0;
    }

    public void ResetTransfer()
    {
        _transferIndex = 0;
        _command = 0;
        _configurationArgument = 0;
        _selected = false;
        _pendingConfigurationMode = _configurationMode;
        _pendingAnalogMode = AnalogMode;
        _pendingAnalogModeLocked = AnalogModeLocked;
    }

    public ControllerTransferResult Transfer(byte value)
    {
        if (_transferIndex == 0)
        {
            _selected = value == 0x01;
            _transferIndex = _selected ? 1 : 0;
            return new ControllerTransferResult(0xFF, _selected);
        }

        if (_transferIndex == 1)
        {
            _command = value;
            bool accepted = IsCommandAccepted(value);
            if (!accepted)
            {
                ResetTransfer();
                return new ControllerTransferResult(0xFF, false);
            }

            _transferIndex = 2;
            return new ControllerTransferResult(CurrentId(), true);
        }

        byte response = GetResponse(_transferIndex);
        ApplyInput(_transferIndex, value);
        int lastIndex = GetLastIndex();
        bool acknowledge = _transferIndex < lastIndex;

        if (acknowledge)
            _transferIndex++;
        else
        {
            ApplyPendingModeChanges();
            ResetTransfer();
        }

        return new ControllerTransferResult(response, acknowledge);
    }

    private bool IsCommandAccepted(byte command) =>
        _configurationMode
            ? command is >= 0x40 and <= 0x4F
            : command is 0x42 or 0x43;

    private byte CurrentId()
    {
        if (_configurationMode)
            return 0xF3;
        if (AnalogMode)
            return 0x73;

        int responseHalfWords = (GetDigitalResponseLastIndex() - 2) / 2;
        return (byte)(0x40 | responseHalfWords);
    }

    private int GetLastIndex()
    {
        if (_configurationMode)
            return 8;

        return AnalogMode ? 8 : GetDigitalResponseLastIndex();
    }

    private byte GetResponse(int index)
    {
        if (index == 2)
            return 0x5A;

        bool pollResponse =
            _command == 0x42 ||
            (!_configurationMode && _command == 0x43);
        if (pollResponse)
        {
            return index switch
            {
                3 => (byte)_buttons,
                4 => (byte)(_buttons >> 8),
                5 when AnalogMode => RightX,
                6 when AnalogMode => RightY,
                7 when AnalogMode => LeftX,
                8 when AnalogMode => LeftY,
                _ => 0,
            };
        }

        if (_command == 0x45)
        {
            byte value = LedStatusReply[index - 3];
            return index == 5 && AnalogMode ? (byte)0x01 : value;
        }

        return _command switch
        {
            0x46 => GetVariableResponseA(index),
            0x47 => FixedResponse[index - 3],
            0x48 => GetUnknownResponse(index),
            0x4C => GetVariableResponseB(index),
            0x4D => _rumbleMapping[index - 3],
            _ => 0,
        };
    }

    private void ApplyInput(int index, byte value)
    {
        if (_command == 0x42 && !_configurationMode)
        {
            int mappingIndex = index - 3;
            if ((uint)mappingIndex < _rumbleMapping.Length)
            {
                if (_rumbleMapping[mappingIndex] == 0)
                    SmallMotor = (byte)(value & 1);
                else if (_rumbleMapping[mappingIndex] == 1)
                    LargeMotor = value;
            }
        }

        if (_command == 0x43 && index == 3)
            _pendingConfigurationMode = value == 0x01;

        if (!_configurationMode)
            return;

        if (_command == 0x44)
        {
            if (index == 3 && value <= 1)
                _pendingAnalogMode = value == 0x01;
            else if (index == 4)
                _pendingAnalogModeLocked = value == 0x03;
        }

        if (_command is 0x46 or 0x48 or 0x4C && index == 3)
            _configurationArgument = value;

        if (_command == 0x4D)
        {
            int mappingIndex = index - 3;
            if ((uint)mappingIndex < _rumbleMapping.Length)
                _rumbleMapping[mappingIndex] = value;
        }
    }

    private void ApplyPendingModeChanges()
    {
        if (_command == 0x43)
            _configurationMode = _pendingConfigurationMode;
        if (_configurationMode && _command == 0x44)
        {
            AnalogMode = _pendingAnalogMode;
            AnalogModeLocked = _pendingAnalogModeLocked;
        }
    }

    private int GetDigitalResponseLastIndex()
    {
        int lastIndex = 4;
        for (int mappingIndex = 0;
             mappingIndex < _rumbleMapping.Length;
             mappingIndex++)
        {
            if (_rumbleMapping[mappingIndex] is 0 or 1)
                lastIndex = Math.Max(lastIndex, 3 + mappingIndex);
        }

        return Math.Min(8, (lastIndex + 1) & ~1);
    }

    private byte GetVariableResponseA(int index)
    {
        if (index < 5)
            return 0;

        return _configurationArgument switch
        {
            0 => index switch
            {
                5 => 0x01,
                6 => 0x02,
                7 => 0x00,
                8 => 0x0A,
                _ => 0,
            },
            1 => index switch
            {
                5 => 0x01,
                6 => 0x01,
                7 => 0x01,
                8 => 0x14,
                _ => 0,
            },
            _ => 0,
        };
    }

    private byte GetVariableResponseB(int index)
    {
        if (index != 6)
            return 0;

        return _configurationArgument switch
        {
            0 => 0x04,
            1 => 0x07,
            _ => 0,
        };
    }

    private byte GetUnknownResponse(int index)
    {
        if (index != 7)
            return 0;

        return _configurationArgument is 0 or 1 ? (byte)0x01 : (byte)0;
    }
}
