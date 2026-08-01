using System.Numerics;

namespace SadPSX.Core.Gte;

public sealed class Gte
{
    private const uint ErrorMask = 0x7F87_E000;
    private const long MacMinimum = -(1L << 43);
    private const long MacMaximum = (1L << 43) - 1;
    private const long MacMask = (1L << 44) - 1;

    private readonly uint[] _data = new uint[32];
    private readonly uint[] _control = new uint[32];
    private readonly long[] _mac = new long[4];

    private uint _flags;

    public void Reset()
    {
        Array.Clear(_data);
        Array.Clear(_control);
        Array.Clear(_mac);
        _flags = 0;
    }

    public uint ReadDataRegister(int index)
    {
        ValidateRegister(index);

        return index switch
        {
            1 or 3 or 5 or 8 or 9 or 10 or 11 =>
                unchecked((uint)(int)(short)_data[index]),
            7 or 16 or 17 or 18 or 19 => (ushort)_data[index],
            15 => _data[14],
            28 or 29 => PackIrColor(),
            31 => _data[31],
            _ => _data[index],
        };
    }

    public void WriteDataRegister(int index, uint value)
    {
        ValidateRegister(index);

        switch (index)
        {
            case 15:
                PushScreenCoordinate(value);
                break;

            case 28:
                _data[28] = value & 0x7FFF;
                _data[9] = (value & 0x1F) << 7;
                _data[10] = ((value >> 5) & 0x1F) << 7;
                _data[11] = ((value >> 10) & 0x1F) << 7;
                break;

            case 29:
                break;

            case 30:
                _data[30] = value;
                _data[31] = value == 0
                    ? 32u
                    : value > 0x7FFF_FFFF
                        ? (uint)BitOperations.LeadingZeroCount(~value)
                        : (uint)BitOperations.LeadingZeroCount(value);
                break;

            case 31:
                break;

            default:
                _data[index] = value;
                if (index is >= 24 and <= 27)
                    _mac[index - 24] = unchecked((int)value);
                break;
        }
    }

    public uint ReadControlRegister(int index)
    {
        ValidateRegister(index);

        if (index == 31)
            return _flags;

        return index is 4 or 12 or 20 or 26 or 27 or 29 or 30
            ? unchecked((uint)(int)(short)_control[index])
            : _control[index];
    }

    public void WriteControlRegister(int index, uint value)
    {
        ValidateRegister(index);

        if (index == 31)
        {
            _flags = value & 0x7FFF_F000;
            UpdateErrorFlag();
            return;
        }

        _control[index] = value;
    }

    public bool ExecuteCommand(uint command)
    {
        _flags = 0;

        bool handled = (command & 0x3F) switch
        {
            0x01 => ExecuteRtps(command),
            0x06 => ExecuteNclip(),
            0x0C => ExecuteOuterProduct(command),
            0x10 => ExecuteDepthCueColor(command),
            0x11 => ExecuteInterpolate(command),
            0x12 => ExecuteMatrixVectorMultiply(command),
            0x13 => ExecuteNormalColor(
                command,
                vectorCount: 1,
                multiplyColor: true,
                depthCue: true),
            0x14 => ExecuteColorDepthCue(command),
            0x16 => ExecuteNormalColor(
                command,
                vectorCount: 3,
                multiplyColor: true,
                depthCue: true),
            0x1B => ExecuteNormalColor(
                command,
                vectorCount: 1,
                multiplyColor: true,
                depthCue: false),
            0x1C => ExecuteColorColor(command),
            0x1E => ExecuteNormalColor(
                command,
                vectorCount: 1,
                multiplyColor: false,
                depthCue: false),
            0x20 => ExecuteNormalColor(
                command,
                vectorCount: 3,
                multiplyColor: false,
                depthCue: false),
            0x28 => ExecuteSquare(command),
            0x29 => ExecuteDepthCueLight(command),
            0x2A => ExecuteDepthCueColors(command),
            0x2D => ExecuteAverageDepth(3),
            0x2E => ExecuteAverageDepth(4),
            0x30 => ExecuteRtpt(command),
            0x3D => ExecuteGeneralPurpose(command, accumulate: false),
            0x3E => ExecuteGeneralPurpose(command, accumulate: true),
            0x3F => ExecuteNormalColor(
                command,
                vectorCount: 3,
                multiplyColor: true,
                depthCue: false),
            _ => false,
        };

        UpdateErrorFlag();
        return handled;
    }

    private bool ExecuteRtps(uint command)
    {
        TransformAndProject(0, command, updateIr0: true);
        return true;
    }

    private bool ExecuteRtpt(uint command)
    {
        TransformAndProject(0, command, updateIr0: false);
        TransformAndProject(1, command, updateIr0: false);
        TransformAndProject(2, command, updateIr0: true);
        return true;
    }

    private void TransformAndProject(int vectorIndex, uint command, bool updateIr0)
    {
        (int x, int y, int z) = ReadVector(vectorIndex);
        long rawX = TransformMatrixRow(
            row: 0,
            x,
            y,
            z,
            ControlSigned(5));
        long rawY = TransformMatrixRow(
            row: 1,
            x,
            y,
            z,
            ControlSigned(6));
        long rawZ = TransformMatrixRow(
            row: 2,
            x,
            y,
            z,
            ControlSigned(7));

        int shift = CommandShift(command);
        bool limitMode = CommandLimitMode(command);
        SetMacAndIr(1, rawX, shift, limitMode);
        SetMacAndIr(2, rawY, shift, limitMode);
        SetMac(3, rawZ, shift);
        ClampIr(3, rawZ >> 12, limitMode: false);
        _data[11] = unchecked((uint)ClampIrWithoutFlag(_mac[3], limitMode));

        PushDepth(ClampDepth(rawZ >> 12));

        long factor = Divide((ushort)_control[26], (ushort)_data[19]);
        long projectedX = factor * DataSigned16(9) + ControlSigned(24);
        long projectedY = factor * DataSigned16(10) + ControlSigned(25);
        SetMac0(projectedX);
        int screenX = ClampScreen(projectedX >> 16, isY: false);
        SetMac0(projectedY);
        int screenY = ClampScreen(projectedY >> 16, isY: true);
        PushScreenCoordinate((uint)(ushort)screenX | ((uint)(ushort)screenY << 16));

        if (updateIr0)
        {
            long depthCue = factor * ControlSigned16(27) + ControlSigned(28);
            SetMac0(depthCue);
            _data[8] = (uint)ClampIr0(depthCue >> 12);
        }
    }

    private bool ExecuteNclip()
    {
        (int sx0, int sy0) = ReadScreenCoordinate(12);
        (int sx1, int sy1) = ReadScreenCoordinate(13);
        (int sx2, int sy2) = ReadScreenCoordinate(14);
        long result = (long)sx0 * sy1 + (long)sx1 * sy2 + (long)sx2 * sy0 -
                      (long)sx0 * sy2 - (long)sx1 * sy0 - (long)sx2 * sy1;
        SetMac0(result);
        return true;
    }

    private bool ExecuteOuterProduct(uint command)
    {
        int shift = (command & (1u << 19)) != 0 ? 12 : 0;
        bool limitMode = (command & (1u << 10)) != 0;
        int ir1 = DataSigned16(9);
        int ir2 = DataSigned16(10);
        int ir3 = DataSigned16(11);
        int d1 = MatrixElement(0, 0, 0);
        int d2 = MatrixElement(0, 1, 1);
        int d3 = MatrixElement(0, 2, 2);

        SetMacAndIr(1, (long)ir3 * d2 - (long)ir2 * d3, shift, limitMode);
        SetMacAndIr(2, (long)ir1 * d3 - (long)ir3 * d1, shift, limitMode);
        SetMacAndIr(3, (long)ir2 * d1 - (long)ir1 * d2, shift, limitMode);
        return true;
    }

    private bool ExecuteMatrixVectorMultiply(uint command)
    {
        int matrix = (int)((command >> 17) & 3);
        int vector = (int)((command >> 15) & 3);
        int translation = (int)((command >> 13) & 3);
        int shift = (command & (1u << 19)) != 0 ? 12 : 0;
        bool limitMode = (command & (1u << 10)) != 0;

        (int x, int y, int z) = vector == 3
            ? (DataSigned16(9), DataSigned16(10), DataSigned16(11))
            : ReadVector(vector);

        for (int row = 0; row < 3; row++)
        {
            int index = row + 1;
            if (translation == 2)
            {
                long ignoredTranslation = SignExtendMacResult(
                    index,
                    (long)TranslationElement(translation, row) * 0x1000 +
                    (long)MatrixElement(matrix, row, 0) * x);
                ClampIr(
                    index,
                    unchecked((int)(ignoredTranslation >> shift)),
                    limitMode: false);

                long value = SignExtendMacResult(
                    index,
                    (long)MatrixElement(matrix, row, 1) * y);
                value += (long)MatrixElement(matrix, row, 2) * z;
                SetMacAndIr(index, value, shift, limitMode);
                continue;
            }

            long result = translation == 3
                ? MultiplyMatrixRow(matrix, row, x, y, z)
                : MultiplyMatrixRow(
                    matrix,
                    row,
                    x,
                    y,
                    z,
                    TranslationElement(translation, row));
            SetMacAndIr(index, result, shift, limitMode);
        }

        return true;
    }

    private bool ExecuteDepthCueColor(uint command)
    {
        FinishDepthCue(
            command,
            ColorComponentValue(_data[6], 1, 16),
            ColorComponentValue(_data[6], 2, 16),
            ColorComponentValue(_data[6], 3, 16));
        return true;
    }

    private bool ExecuteInterpolate(uint command)
    {
        FinishDepthCue(
            command,
            (long)DataSigned16(9) << 12,
            (long)DataSigned16(10) << 12,
            (long)DataSigned16(11) << 12);
        return true;
    }

    private bool ExecuteColorDepthCue(uint command)
    {
        int shift = CommandShift(command);
        bool limitMode = CommandLimitMode(command);
        ApplyColorMatrix(shift, limitMode);
        ApplyDepthCue(
            shift,
            limitMode,
            PrimaryColorValue(1),
            PrimaryColorValue(2),
            PrimaryColorValue(3));
        PushColor();
        return true;
    }

    private bool ExecuteColorColor(uint command)
    {
        int shift = CommandShift(command);
        bool limitMode = CommandLimitMode(command);
        ApplyColorMatrix(shift, limitMode);
        for (int index = 1; index <= 3; index++)
            SetMacAndIr(index, PrimaryColorValue(index), shift, limitMode);
        PushColor();
        return true;
    }

    private bool ExecuteDepthCueLight(uint command)
    {
        FinishDepthCue(
            command,
            PrimaryColorValue(1),
            PrimaryColorValue(2),
            PrimaryColorValue(3));
        return true;
    }

    private bool ExecuteDepthCueColors(uint command)
    {
        for (int iteration = 0; iteration < 3; iteration++)
        {
            uint color = _data[20];
            FinishDepthCue(
                command,
                ColorComponentValue(color, 1, 16),
                ColorComponentValue(color, 2, 16),
                ColorComponentValue(color, 3, 16));
        }

        return true;
    }

    private bool ExecuteGeneralPurpose(uint command, bool accumulate)
    {
        int shift = CommandShift(command);
        bool limitMode = CommandLimitMode(command);
        int interpolation = DataSigned16(8);

        for (int index = 1; index <= 3; index++)
        {
            long accumulator = accumulate ? _mac[index] << shift : 0;
            long value = accumulator +
                         (long)DataSigned16(8 + index) * interpolation;
            SetMacAndIr(index, value, shift, limitMode);
        }

        PushColor();
        return true;
    }

    private static long ColorComponentValue(uint color, int index, int shift)
    {
        int component = (byte)(color >> ((index - 1) * 8));
        return (long)component << shift;
    }

    private long PrimaryColorValue(int index)
    {
        int component = (byte)(_data[6] >> ((index - 1) * 8));
        return (long)component * DataSigned16(8 + index) << 4;
    }

    private void FinishDepthCue(
        uint command,
        long mac1,
        long mac2,
        long mac3)
    {
        int shift = CommandShift(command);
        bool limitMode = CommandLimitMode(command);
        ApplyDepthCue(shift, limitMode, mac1, mac2, mac3);
        PushColor();
    }

    private bool ExecuteNormalColor(
        uint command,
        int vectorCount,
        bool multiplyColor,
        bool depthCue)
    {
        int shift = CommandShift(command);
        bool limitMode = CommandLimitMode(command);

        for (int vectorIndex = 0;
             vectorIndex < vectorCount;
             vectorIndex++)
        {
            ApplyLightMatrix(vectorIndex, shift, limitMode);
            ApplyColorMatrix(shift, limitMode);

            if (depthCue)
            {
                ApplyDepthCue(
                    shift,
                    limitMode,
                    PrimaryColorValue(1),
                    PrimaryColorValue(2),
                    PrimaryColorValue(3));
            }
            else if (multiplyColor)
            {
                for (int index = 1; index <= 3; index++)
                    SetMacAndIr(
                        index,
                        PrimaryColorValue(index),
                        shift,
                        limitMode);
            }

            PushColor();
        }

        return true;
    }

    private void ApplyLightMatrix(
        int vectorIndex,
        int shift,
        bool limitMode)
    {
        (int x, int y, int z) = ReadVector(vectorIndex);
        for (int row = 0; row < 3; row++)
        {
            long value = MultiplyMatrixRow(1, row, x, y, z);
            SetMacAndIr(row + 1, value, shift, limitMode);
        }
    }

    private void ApplyColorMatrix(int shift, bool limitMode)
    {
        int ir1 = DataSigned16(9);
        int ir2 = DataSigned16(10);
        int ir3 = DataSigned16(11);
        for (int row = 0; row < 3; row++)
        {
            long value = MultiplyMatrixRow(
                2,
                row,
                ir1,
                ir2,
                ir3,
                ControlSigned(13 + row));
            SetMacAndIr(row + 1, value, shift, limitMode);
        }
    }

    private void ApplyDepthCue(
        int shift,
        bool limitMode,
        long mac1,
        long mac2,
        long mac3)
    {
        int interpolation = DataSigned16(8);
        for (int index = 1; index <= 3; index++)
        {
            long current = index switch
            {
                1 => mac1,
                2 => mac2,
                _ => mac3,
            };
            long difference =
                ((long)ControlSigned(20 + index) << 12) - current;
            int differenceIr = SetMacAndIr(
                index,
                difference,
                shift,
                limitMode: false);
            long value = current + (long)differenceIr * interpolation;
            SetMacAndIr(index, value, shift, limitMode);
        }
    }

    private void PushColor()
    {
        uint sourceColor = _data[6];
        uint red = (uint)ClampColor(
            _mac[1] >> 4,
            0);
        uint green = (uint)ClampColor(
            _mac[2] >> 4,
            1);
        uint blue = (uint)ClampColor(
            _mac[3] >> 4,
            2);
        uint color =
            red |
            (green << 8) |
            (blue << 16) |
            (sourceColor & 0xFF00_0000);

        _data[20] = _data[21];
        _data[21] = _data[22];
        _data[22] = color;
    }

    private int ClampColor(long value, int component)
    {
        if (value < 0)
        {
            _flags |= 1u << (21 - component);
            return 0;
        }

        if (value > byte.MaxValue)
        {
            _flags |= 1u << (21 - component);
            return byte.MaxValue;
        }

        return (int)value;
    }

    private bool ExecuteSquare(uint command)
    {
        int shift = CommandShift(command);
        bool limitMode = CommandLimitMode(command);

        for (int index = 1; index <= 3; index++)
        {
            int value = DataSigned16(8 + index);
            SetMacAndIr(index, (long)value * value, shift, limitMode);
        }

        return true;
    }

    private bool ExecuteAverageDepth(int count)
    {
        int firstDepth = count == 3 ? 17 : 16;
        long sum = 0;
        for (int index = firstDepth; index <= 19; index++)
            sum += (ushort)_data[index];

        int scale = ControlSigned16(count == 3 ? 29 : 30);
        long result = sum * scale;
        SetMac0(result);
        _data[7] = (uint)ClampDepth(result >> 12);
        return true;
    }

    private (int X, int Y, int Z) ReadVector(int index)
    {
        int xyRegister = index * 2;
        uint xy = _data[xyRegister];
        return ((short)xy, (short)(xy >> 16), DataSigned16(xyRegister + 1));
    }

    private (int X, int Y) ReadScreenCoordinate(int index)
    {
        uint value = _data[index];
        return ((short)value, (short)(value >> 16));
    }

    private int MatrixElement(int matrix, int row, int column)
    {
        if (matrix == 3)
        {
            int red = (byte)_data[6];
            return (row, column) switch
            {
                (0, 0) => unchecked((short)(-(red << 4))),
                (0, 1) => unchecked((short)(red << 4)),
                (0, 2) => DataSigned16(8),
                (1, _) => MatrixElement(0, 0, 2),
                (2, _) => MatrixElement(0, 1, 1),
                _ => 0,
            };
        }

        int registerBase = matrix switch
        {
            0 => 0,
            1 => 8,
            2 => 16,
            _ => 0,
        };
        int element = row * 3 + column;
        uint packed = _control[registerBase + element / 2];
        return (short)((element & 1) == 0 ? packed : packed >> 16);
    }

    private int TranslationElement(int translation, int row)
    {
        int registerBase = translation switch
        {
            0 => 5,
            1 => 13,
            2 => 21,
            _ => -1,
        };
        return registerBase < 0 ? 0 : ControlSigned(registerBase + row);
    }

    private long MultiplyMatrixRow(
        int matrix,
        int row,
        int x,
        int y,
        int z)
    {
        int index = row + 1;
        long value = SignExtendMacResult(
            index,
            (long)MatrixElement(matrix, row, 0) * x +
            (long)MatrixElement(matrix, row, 1) * y);
        return value + (long)MatrixElement(matrix, row, 2) * z;
    }

    private long MultiplyMatrixRow(
        int matrix,
        int row,
        int x,
        int y,
        int z,
        int translation)
    {
        int index = row + 1;
        long value = SignExtendMacResult(
            index,
            (long)translation * 0x1000 +
            (long)MatrixElement(matrix, row, 0) * x);
        value = SignExtendMacResult(
            index,
            value + (long)MatrixElement(matrix, row, 1) * y);
        return value + (long)MatrixElement(matrix, row, 2) * z;
    }

    private long TransformMatrixRow(
        int row,
        int x,
        int y,
        int z,
        int translation)
    {
        return SignExtendMacResult(
            row + 1,
            MultiplyMatrixRow(
                matrix: 0,
                row,
                x,
                y,
                z,
                translation));
    }

    private int SetMacAndIr(
        int index,
        long value,
        int shift,
        bool limitMode)
    {
        int mac = StoreMacValue(index, value, shift);
        int ir = ClampIr(index, mac, limitMode);
        _data[8 + index] = unchecked((uint)ir);
        return ir;
    }

    private void SetMac(int index, long value, int shift = 0)
    {
        StoreMacValue(index, value, shift);
    }

    private int StoreMacValue(int index, long value, int shift)
    {
        CheckMacOverflow(index, value);
        int truncated = unchecked((int)(value >> shift));
        _mac[index] = truncated;
        _data[24 + index] = unchecked((uint)truncated);
        return truncated;
    }

    private long SignExtendMacResult(int index, long value)
    {
        CheckMacOverflow(index, value);
        long truncated = value & MacMask;
        return (truncated & (1L << 43)) != 0
            ? truncated | ~MacMask
            : truncated;
    }

    private void CheckMacOverflow(int index, long value)
    {
        if (value > MacMaximum)
            _flags |= 1u << (31 - index);
        if (value < MacMinimum)
            _flags |= 1u << (28 - index);
    }

    private void SetMac0(long value)
    {
        if (value > int.MaxValue)
            _flags |= 1u << 16;
        if (value < int.MinValue)
            _flags |= 1u << 15;

        _mac[0] = unchecked((int)value);
        _data[24] = unchecked((uint)value);
    }

    private int ClampIr(int index, long value, bool limitMode)
    {
        int minimum = limitMode ? 0 : short.MinValue;
        if (value < minimum)
        {
            _flags |= 1u << (25 - index);
            return minimum;
        }

        if (value > short.MaxValue)
        {
            _flags |= 1u << (25 - index);
            return short.MaxValue;
        }

        return (int)value;
    }

    private static int ClampIrWithoutFlag(long value, bool limitMode)
    {
        int minimum = limitMode ? 0 : short.MinValue;
        return (int)Math.Clamp(value, minimum, short.MaxValue);
    }

    private int ClampIr0(long value)
    {
        if (value < 0)
        {
            _flags |= 1u << 12;
            return 0;
        }

        if (value > 0x1000)
        {
            _flags |= 1u << 12;
            return 0x1000;
        }

        return (int)value;
    }

    private int ClampDepth(long value)
    {
        if (value < 0)
        {
            _flags |= 1u << 18;
            return 0;
        }

        if (value > ushort.MaxValue)
        {
            _flags |= 1u << 18;
            return ushort.MaxValue;
        }

        return (int)value;
    }

    private int ClampScreen(long value, bool isY)
    {
        if (value < -0x400)
        {
            _flags |= 1u << (isY ? 13 : 14);
            return -0x400;
        }

        if (value > 0x3FF)
        {
            _flags |= 1u << (isY ? 13 : 14);
            return 0x3FF;
        }

        return (int)value;
    }

    private long Divide(ushort h, ushort depth)
    {
        if ((uint)h >= (uint)depth * 2)
        {
            _flags |= 1u << 17;
            return 0x1FFFF;
        }

        int shift = BitOperations.LeadingZeroCount(depth) - 16;
        uint numerator = (uint)h << shift;
        uint denominator = (uint)depth << shift;
        int tableIndex = (int)((denominator - 0x7FC0) >> 7);
        long reciprocal = Math.Max(
            0,
            ((0x40000 / (tableIndex + 0x100) + 1) / 2) - 0x101) +
            0x101;
        long refined = (0x0200_0080L - denominator * reciprocal) >> 8;
        refined = (0x80 + refined * reciprocal) >> 8;
        return Math.Min(
            0x1FFFF,
            (numerator * refined + 0x8000) >> 16);
    }

    private void PushDepth(int value)
    {
        _data[16] = _data[17];
        _data[17] = _data[18];
        _data[18] = _data[19];
        _data[19] = (uint)value;
    }

    private void PushScreenCoordinate(uint value)
    {
        _data[12] = _data[13];
        _data[13] = _data[14];
        _data[14] = value;
        _data[15] = value;
    }

    private uint PackIrColor()
    {
        uint red = (uint)Math.Clamp(DataSigned16(9) >> 7, 0, 31);
        uint green = (uint)Math.Clamp(DataSigned16(10) >> 7, 0, 31);
        uint blue = (uint)Math.Clamp(DataSigned16(11) >> 7, 0, 31);
        return red | (green << 5) | (blue << 10);
    }

    private int DataSigned16(int index) => (short)_data[index];

    private int ControlSigned16(int index) => (short)_control[index];

    private int ControlSigned(int index) => unchecked((int)_control[index]);

    private static int CommandShift(uint command) =>
        (command & (1u << 19)) != 0 ? 12 : 0;

    private static bool CommandLimitMode(uint command) =>
        (command & (1u << 10)) != 0;

    private void UpdateErrorFlag()
    {
        _flags &= 0x7FFF_FFFF;
        if ((_flags & ErrorMask) != 0)
            _flags |= 0x8000_0000;
    }

    private static void ValidateRegister(int index)
    {
        if ((uint)index >= 32)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
