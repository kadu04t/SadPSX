namespace SadPSX.Core.Controllers;

public sealed class MemoryCard : ISioPeripheral
{
    public const int SectorSize = 128;
    public const int SectorCount = 1024;
    public const int ImageSize = SectorSize * SectorCount;

    private readonly byte[] _data;
    private readonly byte[] _writeBuffer = new byte[SectorSize];

    private int _transferIndex;
    private byte _command;
    private byte _addressMsb;
    private byte _addressLsb;
    private byte _previousByte;
    private byte _writeChecksum;
    private byte _writeStatus;
    private bool _selected;

    private MemoryCard(byte[] data, string? backingPath)
    {
        _data = data;
        BackingPath = backingPath;
    }

    public string? BackingPath { get; private set; }
    public bool IsDirty { get; private set; }
    public string? LastPersistenceError { get; private set; }
    public byte Flag { get; private set; } = 0x08;

    public static MemoryCard CreateFormatted(string? backingPath = null)
    {
        byte[] data = new byte[ImageSize];
        Format(data);
        return new MemoryCard(
            data,
            backingPath is null ? null : Path.GetFullPath(backingPath));
    }

    public static MemoryCard Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        byte[] data = File.ReadAllBytes(fullPath);
        if (data.Length != ImageSize)
        {
            throw new InvalidDataException(
                $"Raw memory card images must contain exactly {ImageSize} bytes.");
        }

        return new MemoryCard(data, fullPath);
    }

    public static MemoryCard LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            return Load(fullPath);

        var memoryCard = CreateFormatted(fullPath);
        memoryCard.IsDirty = true;
        memoryCard.Save();
        return memoryCard;
    }

    public byte[] ExportImage() => [.. _data];

    public void Save(string? path = null)
    {
        string targetPath = path is null
            ? BackingPath ??
              throw new InvalidOperationException(
                  "No backing path is configured for this memory card.")
            : Path.GetFullPath(path);

        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = targetPath + ".tmp";
        File.WriteAllBytes(temporaryPath, _data);
        File.Move(temporaryPath, targetPath, overwrite: true);
        BackingPath = targetPath;
        IsDirty = false;
        LastPersistenceError = null;
    }

    public void ResetTransfer()
    {
        _transferIndex = 0;
        _command = 0;
        _addressMsb = 0;
        _addressLsb = 0;
        _previousByte = 0;
        _writeChecksum = 0;
        _writeStatus = 0xFF;
        _selected = false;
    }

    public ControllerTransferResult Transfer(byte value)
    {
        if (_transferIndex == 0)
        {
            _selected = value == 0x81;
            _transferIndex = _selected ? 1 : 0;
            _previousByte = value;
            return new ControllerTransferResult(0xFF, _selected);
        }

        if (_transferIndex == 1)
        {
            _command = value;
            bool accepted = value is 0x52 or 0x53 or 0x57;
            _transferIndex = accepted ? 2 : 0;
            _previousByte = value;
            return new ControllerTransferResult(Flag, accepted);
        }

        ControllerTransferResult result = _command switch
        {
            0x52 => TransferRead(value),
            0x53 => TransferGetId(),
            0x57 => TransferWrite(value),
            _ => new ControllerTransferResult(0xFF, false),
        };
        _previousByte = value;
        if (!result.Acknowledge)
            ResetTransfer();
        return result;
    }

    private ControllerTransferResult TransferRead(byte value)
    {
        byte response;
        bool acknowledge = true;

        switch (_transferIndex)
        {
            case 2:
                response = 0x5A;
                break;
            case 3:
                response = 0x5D;
                break;
            case 4:
                response = 0;
                _addressMsb = value;
                break;
            case 5:
                response = _addressMsb;
                _addressLsb = value;
                break;
            case 6:
                response = 0x5C;
                break;
            case 7:
                response = 0x5D;
                break;
            case 8:
                response = IsAddressValid() ? _addressMsb : (byte)0xFF;
                break;
            case 9:
                response = IsAddressValid() ? _addressLsb : (byte)0xFF;
                acknowledge = IsAddressValid();
                break;
            case >= 10 and < 10 + SectorSize:
                response = ReadSectorByte(_transferIndex - 10);
                break;
            case 10 + SectorSize:
                response = CalculateSectorChecksum();
                break;
            default:
                response = 0x47;
                acknowledge = false;
                break;
        }

        if (acknowledge)
            _transferIndex++;
        return new ControllerTransferResult(response, acknowledge);
    }

    private ControllerTransferResult TransferWrite(byte value)
    {
        byte response;
        bool acknowledge = true;

        switch (_transferIndex)
        {
            case 2:
                response = 0x5A;
                break;
            case 3:
                response = 0x5D;
                break;
            case 4:
                response = 0;
                _addressMsb = value;
                break;
            case 5:
                response = _addressMsb;
                _addressLsb = value;
                _writeChecksum = (byte)(_addressMsb ^ _addressLsb);
                break;
            case >= 6 and < 6 + SectorSize:
                response = _previousByte;
                int bufferIndex = _transferIndex - 6;
                _writeBuffer[bufferIndex] = value;
                _writeChecksum ^= value;
                break;
            case 6 + SectorSize:
                response = _previousByte;
                _writeStatus = CommitWrite(value);
                break;
            case 7 + SectorSize:
                response = 0x5C;
                break;
            case 8 + SectorSize:
                response = 0x5D;
                break;
            default:
                response = _writeStatus;
                acknowledge = false;
                break;
        }

        if (acknowledge)
            _transferIndex++;
        return new ControllerTransferResult(response, acknowledge);
    }

    private ControllerTransferResult TransferGetId()
    {
        ReadOnlySpan<byte> response =
            [0x5A, 0x5D, 0x5C, 0x5D, 0x04, 0x00, 0x00, 0x80];
        int responseIndex = _transferIndex - 2;
        bool acknowledge = responseIndex < response.Length - 1;
        byte value = response[responseIndex];
        if (acknowledge)
            _transferIndex++;
        return new ControllerTransferResult(value, acknowledge);
    }

    private byte CommitWrite(byte receivedChecksum)
    {
        if (!IsAddressValid())
            return 0xFF;
        if (receivedChecksum != _writeChecksum)
            return 0x4E;

        Buffer.BlockCopy(
            _writeBuffer,
            0,
            _data,
            SectorAddress() * SectorSize,
            SectorSize);
        Flag &= 0xF7;
        IsDirty = true;
        TryPersist();
        return 0x47;
    }

    private void TryPersist()
    {
        if (BackingPath is null)
            return;

        try
        {
            Save();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            LastPersistenceError = exception.Message;
        }
    }

    private byte ReadSectorByte(int offset) =>
        _data[SectorAddress() * SectorSize + offset];

    private byte CalculateSectorChecksum()
    {
        byte checksum = (byte)(_addressMsb ^ _addressLsb);
        int start = SectorAddress() * SectorSize;
        for (int offset = 0; offset < SectorSize; offset++)
            checksum ^= _data[start + offset];
        return checksum;
    }

    private bool IsAddressValid() => SectorAddress() < SectorCount;

    private int SectorAddress() => (_addressMsb << 8) | _addressLsb;

    private static void Format(byte[] data)
    {
        data[0] = (byte)'M';
        data[1] = (byte)'C';
        WriteChecksum(data.AsSpan(0, SectorSize));

        for (int sector = 1; sector <= 15; sector++)
        {
            Span<byte> frame = data.AsSpan(
                sector * SectorSize,
                SectorSize);
            frame[0] = 0xA0;
            frame[8] = 0xFF;
            frame[9] = 0xFF;
            WriteChecksum(frame);
        }

        for (int sector = 16; sector <= 35; sector++)
        {
            Span<byte> frame = data.AsSpan(
                sector * SectorSize,
                SectorSize);
            frame[..4].Fill(0xFF);
            frame[8] = 0xFF;
            frame[9] = 0xFF;
            WriteChecksum(frame);
        }

        data.AsSpan(36 * SectorSize, 27 * SectorSize).Fill(0xFF);
        data.AsSpan(0, SectorSize).CopyTo(
            data.AsSpan(63 * SectorSize, SectorSize));
    }

    private static void WriteChecksum(Span<byte> frame)
    {
        byte checksum = 0;
        for (int index = 0; index < SectorSize - 1; index++)
            checksum ^= frame[index];
        frame[^1] = checksum;
    }
}
