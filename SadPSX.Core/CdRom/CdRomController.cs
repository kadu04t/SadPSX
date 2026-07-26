using SadPSX.Core.Bus;
using SadPSX.Core.CdRom.Media;
using SadPSX.Core.Interrupts;

namespace SadPSX.Core.CdRom;

public sealed class CdRomController : IMmioDevice, IClockedDevice
{
    public const uint BaseAddress = 0x1F80_1800;
    public const uint EndAddress = 0x1F80_1803;

    private const int FifoCapacity = 16;
    private const uint CommandDelayCycles = 400;
    private const byte NoDiscStatus = 1 << 4;

    private readonly InterruptController _interruptController;
    private readonly Queue<byte> _parameters = new(FifoCapacity);
    private readonly Queue<byte> _results = new(FifoCapacity);
    private readonly Queue<byte> _data = new();
    private readonly Queue<Response> _responses = new();
    private readonly byte[] _sector = new byte[DiscImage.RawSectorSize];

    private byte _index;
    private byte _interruptEnable;
    private byte _interruptFlags;
    private byte _mode;
    private byte _filterFile;
    private byte _filterChannel;
    private bool _commandBusy;
    private uint _commandCyclesRemaining;
    private DiscImage? _disc;
    private int _pendingLogicalBlockAddress;
    private bool _sectorAvailable;

    public CdRomController(InterruptController interruptController)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        Reset();
    }

    public byte Index => _index;
    public byte InterruptEnable => _interruptEnable;
    public byte InterruptFlags => _interruptFlags;
    public byte Mode => _mode;
    public byte LastCommand { get; private set; }
    public int ParameterCount => _parameters.Count;
    public int ResultCount => _results.Count;
    public bool CommandBusy => _commandBusy;
    public bool HasDisc => _disc is not null;
    public int DataCount => _data.Count;

    public void LoadDisc(string path)
    {
        LoadDisc(DiscImage.Open(path));
    }

    public void LoadDisc(DiscImage disc)
    {
        ArgumentNullException.ThrowIfNull(disc);
        _disc?.Dispose();
        _disc = disc;
        _sectorAvailable = false;
        _data.Clear();
    }

    public void EjectDisc()
    {
        _disc?.Dispose();
        _disc = null;
        _sectorAvailable = false;
        _data.Clear();
    }

    public void Reset()
    {
        _parameters.Clear();
        _results.Clear();
        _data.Clear();
        _responses.Clear();
        _index = 0;
        _interruptEnable = 0;
        _interruptFlags = 0;
        _mode = 0;
        _filterFile = 0;
        _filterChannel = 0;
        _commandBusy = false;
        _commandCyclesRemaining = 0;
        _pendingLogicalBlockAddress = 0;
        _sectorAvailable = false;
        LastCommand = 0;
    }

    public void Tick(uint cycles)
    {
        if (!_commandBusy)
            return;

        if (cycles < _commandCyclesRemaining)
        {
            _commandCyclesRemaining -= cycles;
            return;
        }

        _commandCyclesRemaining = 0;
        _commandBusy = false;
        ExecuteCommand(LastCommand);
        TryPresentResponse();
    }

    public bool Handles(uint address) =>
        address is >= BaseAddress and <= EndAddress;

    public byte Read8(uint address)
    {
        ValidateAddress(address);
        return (address - BaseAddress) switch
        {
            0 => ReadStatus(),
            1 => ReadResult(),
            2 => ReadDataByte(),
            3 => _index is 0 or 2
                ? (byte)(0xE0 | _interruptEnable)
                : (byte)(0xE0 | _interruptFlags),
            _ => 0,
        };
    }

    public ushort Read16(uint address)
    {
        EnsureAligned(address, 2, "Leitura");
        return (ushort)(Read8(address) | Read8(address + 1) << 8);
    }

    public uint Read32(uint address)
    {
        EnsureAligned(address, 4, "Leitura");
        return (uint)(Read8(address) |
            Read8(address + 1) << 8 |
            Read8(address + 2) << 16 |
            Read8(address + 3) << 24);
    }

    public void Write8(uint address, byte value)
    {
        ValidateAddress(address);
        switch (address - BaseAddress)
        {
            case 0:
                _index = (byte)(value & 3);
                break;

            case 1 when _index == 0:
                StartCommand(value);
                break;

            case 2 when _index == 0:
                if (_parameters.Count < FifoCapacity)
                    _parameters.Enqueue(value);
                break;

            case 2 when _index == 1:
                WriteInterruptEnable(value);
                break;

            case 3 when _index == 1:
                AcknowledgeInterrupt(value);
                break;

            case 3 when _index == 0:
                WriteRequest(value);
                break;
        }
    }

    public void Write16(uint address, ushort value)
    {
        EnsureAligned(address, 2, "Escrita");
        Write8(address, (byte)value);
        Write8(address + 1, (byte)(value >> 8));
    }

    public void Write32(uint address, uint value)
    {
        EnsureAligned(address, 4, "Escrita");
        Write8(address, (byte)value);
        Write8(address + 1, (byte)(value >> 8));
        Write8(address + 2, (byte)(value >> 16));
        Write8(address + 3, (byte)(value >> 24));
    }

    public string GetRegisterName(uint address)
    {
        ValidateAddress(address);
        return (address - BaseAddress) switch
        {
            0 => "CDROM_INDEX_STATUS",
            1 => _index == 0 ? "CDROM_COMMAND_RESULT" : "CDROM_RESPONSE",
            2 => _index == 0 ? "CDROM_PARAMETER_DATA" : "CDROM_IRQ_ENABLE",
            3 => _index == 1 ? "CDROM_IRQ_FLAGS" : "CDROM_REQUEST",
            _ => "CDROM_UNKNOWN",
        };
    }

    private byte ReadStatus()
    {
        byte value = _index;
        if (_parameters.Count == 0)
            value |= 1 << 3;
        if (_parameters.Count < FifoCapacity)
            value |= 1 << 4;
        if (_results.Count > 0)
            value |= 1 << 5;
        if (_commandBusy)
            value |= 1 << 7;
        if (_data.Count > 0)
            value |= 1 << 6;
        return value;
    }

    private byte ReadResult() =>
        _results.TryDequeue(out byte value) ? value : (byte)0;

    private void StartCommand(byte command)
    {
        if (_commandBusy)
            return;

        LastCommand = command;
        _commandBusy = true;
        _commandCyclesRemaining = CommandDelayCycles;
    }

    private void ExecuteCommand(byte command)
    {
        byte[] parameters = DrainParameters();
        byte status = GetStatus();
        switch (command)
        {
            case 0x01:
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x02:
                SetLocation(parameters);
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x03:
            case 0x07:
            case 0x08:
            case 0x09:
            case 0x0B:
            case 0x0C:
            case 0x12:
            case 0x15:
            case 0x16:
            case 0x1C:
            case 0x1E:
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x06:
            case 0x1B:
                StartRead();
                break;

            case 0x0A:
                _mode = 0;
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                QueueResponse(CdRomInterruptType.Complete, status);
                break;

            case 0x0D:
                _filterFile = ParameterAt(parameters, 0);
                _filterChannel = ParameterAt(parameters, 1);
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x0E:
                _mode = ParameterAt(parameters, 0);
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x0F:
                QueueResponse(
                    CdRomInterruptType.Acknowledge,
                    status,
                    _mode,
                    0,
                    _filterFile,
                    _filterChannel);
                break;

            case 0x13:
                QueueResponse(_disc is null
                    ? CdRomInterruptType.DiskError
                    : CdRomInterruptType.Acknowledge,
                    (byte)(status | (_disc is null ? 1 : 0)),
                    1,
                    1);
                break;

            case 0x14:
                GetTrackDuration(parameters, status);
                break;

            case 0x19:
                ExecuteTest(parameters);
                break;

            case 0x1A:
                GetId(status);
                break;

            default:
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(NoDiscStatus | 1),
                    0x40);
                break;
        }
    }

    public uint ReadDmaWord()
    {
        uint value = 0;
        for (int index = 0; index < 4; index++)
            value |= (uint)ReadDataByte() << (index * 8);
        return value;
    }

    private void SetLocation(byte[] parameters)
    {
        int minutes = FromBcd(ParameterAt(parameters, 0));
        int seconds = FromBcd(ParameterAt(parameters, 1));
        int frames = FromBcd(ParameterAt(parameters, 2));
        _pendingLogicalBlockAddress =
            Math.Max(0, (minutes * 60 + seconds) * 75 + frames - 150);
    }

    private void StartRead()
    {
        byte status = GetStatus();
        if (_disc is null ||
            (uint)_pendingLogicalBlockAddress >= _disc.SectorCount)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        _disc.ReadSector(_pendingLogicalBlockAddress, _sector);
        _pendingLogicalBlockAddress++;
        _sectorAvailable = true;
        QueueResponse(CdRomInterruptType.Acknowledge, status);
        QueueResponse(
            CdRomInterruptType.DataReady,
            (byte)(status | (1 << 5)));
    }

    private void WriteRequest(byte value)
    {
        if ((value & 0x80) == 0)
        {
            _data.Clear();
            return;
        }

        if (!_sectorAvailable || _disc is null)
            return;

        _data.Clear();
        bool rawSector = (_mode & (1 << 5)) != 0;
        int offset = rawSector
            ? 12
            : _disc.TrackMode == DiscTrackMode.Mode1 ? 16 : 24;
        int length = rawSector ? 2340 : 2048;
        for (int index = 0; index < length; index++)
            _data.Enqueue(_sector[offset + index]);
    }

    private byte ReadDataByte() =>
        _data.TryDequeue(out byte value) ? value : (byte)0;

    private void GetTrackDuration(byte[] parameters, byte status)
    {
        if (_disc is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0,
                0);
            return;
        }

        int absoluteFrames = _disc.SectorCount + 150;
        QueueResponse(
            CdRomInterruptType.Acknowledge,
            status,
            ToBcd(absoluteFrames / (60 * 75)),
            ToBcd(absoluteFrames / 75 % 60));
    }

    private void GetId(byte status)
    {
        QueueResponse(CdRomInterruptType.Acknowledge, status);
        if (_disc is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                0x08,
                0x40,
                0,
                0,
                0,
                0,
                0,
                0);
            return;
        }

        QueueResponse(
            CdRomInterruptType.Complete,
            status,
            0,
            0x20,
            0,
            (byte)'S',
            (byte)'C',
            (byte)'E',
            (byte)'A');
    }

    private void ExecuteTest(byte[] parameters)
    {
        switch (ParameterAt(parameters, 0))
        {
            case 0x20:
                QueueResponse(
                    CdRomInterruptType.Acknowledge,
                    0x98,
                    0x06,
                    0x10,
                    0xC3);
                break;

            default:
                QueueResponse(CdRomInterruptType.Acknowledge, GetStatus());
                break;
        }
    }

    private void QueueResponse(
        CdRomInterruptType interrupt,
        params byte[] result)
    {
        _responses.Enqueue(new Response(interrupt, result));
    }

    private void TryPresentResponse()
    {
        if (_interruptFlags != 0 || _responses.Count == 0)
            return;

        Response response = _responses.Dequeue();
        _results.Clear();
        foreach (byte value in response.Result)
        {
            if (_results.Count < FifoCapacity)
                _results.Enqueue(value);
        }

        _interruptFlags = (byte)response.Interrupt;
        RequestInterruptIfEnabled();
    }

    private void WriteInterruptEnable(byte value)
    {
        _interruptEnable = (byte)(value & 0x1F);
        RequestInterruptIfEnabled();
    }

    private void AcknowledgeInterrupt(byte value)
    {
        _interruptFlags &= (byte)~(value & 0x1F);
        if ((value & 0x40) != 0)
            _parameters.Clear();
        if (_interruptFlags == 0)
            TryPresentResponse();
        RequestInterruptIfEnabled();
    }

    private void RequestInterruptIfEnabled()
    {
        if ((_interruptEnable & _interruptFlags) != 0)
            _interruptController.Request(InterruptSource.CdRom);
    }

    private byte[] DrainParameters()
    {
        byte[] values = _parameters.ToArray();
        _parameters.Clear();
        return values;
    }

    private static byte ParameterAt(byte[] parameters, int index) =>
        index < parameters.Length ? parameters[index] : (byte)0;

    private byte GetStatus() => _disc is null ? NoDiscStatus : (byte)(1 << 1);

    private static int FromBcd(byte value) =>
        (value >> 4) * 10 + (value & 0x0F);

    private static byte ToBcd(int value) =>
        (byte)(((value / 10) << 4) | (value % 10));

    private static void EnsureAligned(uint address, uint width, string operation)
    {
        if ((address & (width - 1)) != 0)
        {
            throw new InvalidOperationException(
                $"{operation} de {width * 8} bits desalinhada no CD-ROM: 0x{address:X8}.");
        }
    }

    private static void ValidateAddress(uint address)
    {
        if (address is < BaseAddress or > EndAddress)
        {
            throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence ao CD-ROM.");
        }
    }

    private sealed record Response(
        CdRomInterruptType Interrupt,
        byte[] Result);
}
