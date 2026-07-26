using SadPSX.Core.Bus;

namespace SadPSX.Core.Mdec;

public sealed class Mdec : IMmioDevice
{
    public const uint DataAddress = 0x1F80_1820;
    public const uint StatusAddress = 0x1F80_1824;

    private const uint CommandMask = 0xE000_0000;
    private const uint DecodeCommand = 0x2000_0000;
    private const uint QuantTableCommand = 0x4000_0000;
    private const uint ScaleTableCommand = 0x6000_0000;

    private static readonly int[] ZigZag =
    [
         0,  1,  5,  6, 14, 15, 27, 28,
         2,  4,  7, 13, 16, 26, 29, 42,
         3,  8, 12, 17, 25, 30, 41, 43,
         9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    ];

    private static readonly double[,] Cosine = CreateCosineTable();

    private readonly byte[] _luminanceQuantTable = new byte[64];
    private readonly byte[] _colorQuantTable = new byte[64];
    private readonly short[] _scaleTable = new short[64];
    private readonly List<uint> _parameters = new();
    private readonly Queue<uint> _output = new();

    private uint _command;
    private int _wordsRemaining;
    private int _outputDepth;
    private bool _outputSigned;
    private bool _outputBit15;
    private bool _dma0Enabled;
    private bool _dma1Enabled;

    public Mdec()
    {
        Reset();
    }

    public uint Status
    {
        get
        {
            uint status = _output.Count == 0 ? 1u << 31 : 0;
            if (_wordsRemaining > 0)
                status |= 1u << 29;
            if (_dma0Enabled && _wordsRemaining > 0)
                status |= 1u << 28;
            if (_dma1Enabled && _output.Count > 0)
                status |= 1u << 27;

            status |= (uint)_outputDepth << 25;
            if (_outputSigned)
                status |= 1u << 24;
            if (_outputBit15)
                status |= 1u << 23;
            status |= 4u << 16;
            if (_wordsRemaining > 0)
                status |= (uint)(_wordsRemaining - 1) & 0xFFFF;

            return status;
        }
    }

    public int OutputWordCount => _output.Count;

    public int WordsRemaining => _wordsRemaining;

    public void Reset()
    {
        _command = 0;
        _wordsRemaining = 0;
        _outputDepth = 0;
        _outputSigned = false;
        _outputBit15 = false;
        _dma0Enabled = false;
        _dma1Enabled = false;
        _parameters.Clear();
        _output.Clear();
    }

    public bool Handles(uint address) =>
        address >= DataAddress && address <= StatusAddress + 3;

    public byte Read8(uint address)
    {
        uint value = ReadRegister(address & ~3u);
        return (byte)(value >> (int)((address & 3) * 8));
    }

    public ushort Read16(uint address)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Leitura de 16 bits desalinhada no MDEC: 0x{address:X8}.");

        uint value = ReadRegister(address & ~3u);
        return (ushort)(value >> (int)((address & 2) * 8));
    }

    public uint Read32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada no MDEC: 0x{address:X8}.");

        return ReadRegister(address);
    }

    public uint Peek32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada no MDEC: 0x{address:X8}.");

        return address == DataAddress
            ? _output.TryPeek(out uint value) ? value : 0
            : Status;
    }

    public void Write8(uint address, byte value)
    {
        if ((address & ~3u) == StatusAddress)
        {
            int shift = (int)((address & 3) * 8);
            WriteControl((uint)value << shift);
        }
    }

    public void Write16(uint address, ushort value)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Escrita de 16 bits desalinhada no MDEC: 0x{address:X8}.");

        if ((address & ~3u) == StatusAddress)
        {
            int shift = (int)((address & 2) * 8);
            WriteControl((uint)value << shift);
        }
    }

    public void Write32(uint address, uint value)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Escrita de 32 bits desalinhada no MDEC: 0x{address:X8}.");

        if (address == DataAddress)
            WriteCommandOrParameter(value);
        else
            WriteControl(value);
    }

    public string GetRegisterName(uint address) =>
        (address & ~3u) == DataAddress ? "MDEC0" : "MDEC1";

    public void WriteDmaWord(uint value) => WriteCommandOrParameter(value);

    public uint ReadDmaWord() =>
        _output.TryDequeue(out uint value) ? value : 0;

    public byte[] GetQuantTable(bool color) =>
        (color ? _colorQuantTable : _luminanceQuantTable).ToArray();

    public short[] GetScaleTable() => _scaleTable.ToArray();

    private uint ReadRegister(uint address) =>
        address == DataAddress ? ReadDmaWord() : Status;

    private void WriteControl(uint value)
    {
        if ((value & (1u << 31)) != 0)
            Reset();

        _dma0Enabled = (value & (1u << 30)) != 0;
        _dma1Enabled = (value & (1u << 29)) != 0;
    }

    private void WriteCommandOrParameter(uint value)
    {
        if (_wordsRemaining == 0)
        {
            BeginCommand(value);
            return;
        }

        _parameters.Add(value);
        _wordsRemaining--;
        if (_wordsRemaining == 0)
            ExecuteCommand();
    }

    private void BeginCommand(uint command)
    {
        _command = command;
        _parameters.Clear();
        _output.Clear();
        if ((command & CommandMask) == DecodeCommand)
        {
            _outputDepth = (int)((command >> 27) & 3);
            _outputSigned = (command & (1u << 26)) != 0;
            _outputBit15 = (command & (1u << 25)) != 0;
        }

        _wordsRemaining = (command & CommandMask) switch
        {
            DecodeCommand => (int)(command & 0xFFFF),
            QuantTableCommand => (command & 1) != 0 ? 32 : 16,
            ScaleTableCommand => 32,
            _ => 0,
        };

        if (_wordsRemaining == 0)
            ExecuteCommand();
    }

    private void ExecuteCommand()
    {
        switch (_command & CommandMask)
        {
            case DecodeCommand:
                DecodeMacroblocks();
                break;
            case QuantTableCommand:
                LoadQuantTables();
                break;
            case ScaleTableCommand:
                LoadScaleTable();
                break;
        }

        _parameters.Clear();
    }

    private void LoadQuantTables()
    {
        byte[] bytes = UnpackBytes(_parameters);
        Array.Copy(bytes, 0, _luminanceQuantTable, 0, 64);
        if ((_command & 1) != 0)
            Array.Copy(bytes, 64, _colorQuantTable, 0, 64);
    }

    private void LoadScaleTable()
    {
        ushort[] values = UnpackHalfwords(_parameters);
        for (int index = 0; index < _scaleTable.Length; index++)
            _scaleTable[index] = unchecked((short)values[index]);
    }

    private void DecodeMacroblocks()
    {
        ushort[] data = UnpackHalfwords(_parameters);
        int dataIndex = 0;
        int depth = _outputDepth;
        bool signed = _outputSigned;
        bool setBit15 = _outputBit15;

        while (dataIndex < data.Length)
        {
            while (dataIndex < data.Length && data[dataIndex] == 0xFE00)
                dataIndex++;
            if (dataIndex >= data.Length)
                break;

            if (depth < 2)
            {
                int[] luminance = DecodeBlock(
                    data,
                    ref dataIndex,
                    _luminanceQuantTable);
                WriteMonochrome(luminance, depth, signed);
                continue;
            }

            int[] cr = DecodeBlock(data, ref dataIndex, _colorQuantTable);
            int[] cb = DecodeBlock(data, ref dataIndex, _colorQuantTable);
            int[][] luminanceBlocks = new int[4][];
            for (int block = 0; block < luminanceBlocks.Length; block++)
                luminanceBlocks[block] = DecodeBlock(
                    data,
                    ref dataIndex,
                    _luminanceQuantTable);

            WriteColor(cr, cb, luminanceBlocks, depth, signed, setBit15);
        }
    }

    private static int[] DecodeBlock(
        ushort[] data,
        ref int dataIndex,
        byte[] quantTable)
    {
        int[] coefficients = new int[64];
        if (dataIndex >= data.Length)
            return coefficients;

        ushort first = data[dataIndex++];
        if (first == 0xFE00)
            return coefficients;

        int quantScale = first >> 10;
        int firstValue = SignExtend10(first);
        coefficients[0] = quantScale == 0
            ? firstValue * 2
            : firstValue * quantTable[0];

        int position = 0;
        while (dataIndex < data.Length)
        {
            ushort encoded = data[dataIndex++];
            if (encoded == 0xFE00)
                break;

            position += (encoded >> 10) + 1;
            if (position >= 64)
                break;

            int value = SignExtend10(encoded);
            int coefficient = quantScale == 0
                ? value * 2
                : (value * quantTable[position] * quantScale + 4) / 8;
            coefficients[ZigZag[position]] = Math.Clamp(
                coefficient,
                -0x400,
                0x3FF);
        }

        return InverseDiscreteCosineTransform(coefficients);
    }

    private static int[] InverseDiscreteCosineTransform(int[] coefficients)
    {
        int[] result = new int[64];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                double sum = 0;
                for (int vertical = 0; vertical < 8; vertical++)
                {
                    double verticalScale =
                        vertical == 0 ? Math.Sqrt(0.5) : 1;
                    for (int horizontal = 0; horizontal < 8; horizontal++)
                    {
                        double horizontalScale =
                            horizontal == 0 ? Math.Sqrt(0.5) : 1;
                        sum += horizontalScale *
                               verticalScale *
                               coefficients[vertical * 8 + horizontal] *
                               Cosine[x, horizontal] *
                               Cosine[y, vertical];
                    }
                }

                result[y * 8 + x] =
                    Math.Clamp((int)Math.Round(sum / 4), -128, 127);
            }
        }

        return result;
    }

    private void WriteMonochrome(int[] luminance, int depth, bool signed)
    {
        var bytes = new List<byte>(depth == 0 ? 32 : 64);
        if (depth == 0)
        {
            for (int pixel = 0; pixel < luminance.Length; pixel += 2)
            {
                byte first = ConvertSample(luminance[pixel], signed);
                byte second = ConvertSample(luminance[pixel + 1], signed);
                bytes.Add((byte)((first >> 4) | (second & 0xF0)));
            }
        }
        else
        {
            foreach (int value in luminance)
                bytes.Add(ConvertSample(value, signed));
        }

        EnqueueBytes(bytes);
    }

    private void WriteColor(
        int[] cr,
        int[] cb,
        int[][] luminanceBlocks,
        int depth,
        bool signed,
        bool setBit15)
    {
        var bytes = new List<byte>(depth == 3 ? 512 : 768);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int block = (y / 8) * 2 + x / 8;
                int luminance = luminanceBlocks[block][
                    (y % 8) * 8 + x % 8];
                int colorIndex = (y / 2) * 8 + x / 2;
                (byte red, byte green, byte blue) = ConvertColor(
                    luminance,
                    cb[colorIndex],
                    cr[colorIndex],
                    signed);

                if (depth == 3)
                {
                    ushort pixel = (ushort)(
                        (red >> 3) |
                        ((green >> 3) << 5) |
                        ((blue >> 3) << 10) |
                        (setBit15 ? 0x8000 : 0));
                    bytes.Add((byte)pixel);
                    bytes.Add((byte)(pixel >> 8));
                }
                else
                {
                    bytes.Add(blue);
                    bytes.Add(green);
                    bytes.Add(red);
                }
            }
        }

        EnqueueBytes(bytes);
    }

    private void EnqueueBytes(IEnumerable<byte> bytes)
    {
        uint word = 0;
        int byteIndex = 0;
        foreach (byte value in bytes)
        {
            word |= (uint)value << (byteIndex * 8);
            byteIndex++;
            if (byteIndex != 4)
                continue;

            _output.Enqueue(word);
            word = 0;
            byteIndex = 0;
        }

        if (byteIndex != 0)
            _output.Enqueue(word);
    }

    private static (byte Red, byte Green, byte Blue) ConvertColor(
        int luminance,
        int cb,
        int cr,
        bool signed)
    {
        int red = Math.Clamp(
            (int)Math.Round(luminance + 1.402 * cr),
            -128,
            127);
        int green = Math.Clamp(
            (int)Math.Round(luminance - 0.3437 * cb - 0.7143 * cr),
            -128,
            127);
        int blue = Math.Clamp(
            (int)Math.Round(luminance + 1.772 * cb),
            -128,
            127);
        return (
            ConvertSample(red, signed),
            ConvertSample(green, signed),
            ConvertSample(blue, signed));
    }

    private static byte ConvertSample(int value, bool signed) =>
        signed
            ? unchecked((byte)(sbyte)value)
            : (byte)(value + 128);

    private static int SignExtend10(ushort value)
    {
        int result = value & 0x3FF;
        return (result & 0x200) != 0 ? result - 0x400 : result;
    }

    private static byte[] UnpackBytes(IReadOnlyList<uint> words)
    {
        byte[] bytes = new byte[words.Count * 4];
        for (int index = 0; index < words.Count; index++)
        {
            uint word = words[index];
            bytes[index * 4] = (byte)word;
            bytes[index * 4 + 1] = (byte)(word >> 8);
            bytes[index * 4 + 2] = (byte)(word >> 16);
            bytes[index * 4 + 3] = (byte)(word >> 24);
        }

        return bytes;
    }

    private static ushort[] UnpackHalfwords(IReadOnlyList<uint> words)
    {
        ushort[] values = new ushort[words.Count * 2];
        for (int index = 0; index < words.Count; index++)
        {
            values[index * 2] = (ushort)words[index];
            values[index * 2 + 1] = (ushort)(words[index] >> 16);
        }

        return values;
    }

    private static double[,] CreateCosineTable()
    {
        var table = new double[8, 8];
        for (int sample = 0; sample < 8; sample++)
        {
            for (int frequency = 0; frequency < 8; frequency++)
            {
                table[sample, frequency] = Math.Cos(
                    (2 * sample + 1) * frequency * Math.PI / 16);
            }
        }

        return table;
    }
}
