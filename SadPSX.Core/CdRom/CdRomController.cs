using SadPSX.Core.Bus;
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
    private readonly Queue<Response> _responses = new();

    private byte _index;
    private byte _interruptEnable;
    private byte _interruptFlags;
    private byte _mode;
    private byte _filterFile;
    private byte _filterChannel;
    private bool _commandBusy;
    private uint _commandCyclesRemaining;

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

    public void Reset()
    {
        _parameters.Clear();
        _results.Clear();
        _responses.Clear();
        _index = 0;
        _interruptEnable = 0;
        _interruptFlags = 0;
        _mode = 0;
        _filterFile = 0;
        _filterChannel = 0;
        _commandBusy = false;
        _commandCyclesRemaining = 0;
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
            2 => 0,
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
        switch (command)
        {
            case 0x01:
                QueueResponse(CdRomInterruptType.Acknowledge, NoDiscStatus);
                break;

            case 0x02:
            case 0x03:
            case 0x06:
            case 0x07:
            case 0x08:
            case 0x09:
            case 0x0B:
            case 0x0C:
            case 0x12:
            case 0x15:
            case 0x16:
            case 0x1B:
            case 0x1C:
            case 0x1E:
                QueueResponse(CdRomInterruptType.Acknowledge, NoDiscStatus);
                break;

            case 0x0A:
                _mode = 0;
                QueueResponse(CdRomInterruptType.Acknowledge, NoDiscStatus);
                QueueResponse(CdRomInterruptType.Complete, NoDiscStatus);
                break;

            case 0x0D:
                _filterFile = ParameterAt(parameters, 0);
                _filterChannel = ParameterAt(parameters, 1);
                QueueResponse(CdRomInterruptType.Acknowledge, NoDiscStatus);
                break;

            case 0x0E:
                _mode = ParameterAt(parameters, 0);
                QueueResponse(CdRomInterruptType.Acknowledge, NoDiscStatus);
                break;

            case 0x0F:
                QueueResponse(
                    CdRomInterruptType.Acknowledge,
                    NoDiscStatus,
                    _mode,
                    0,
                    _filterFile,
                    _filterChannel);
                break;

            case 0x13:
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(NoDiscStatus | 1),
                    1,
                    1);
                break;

            case 0x14:
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(NoDiscStatus | 1),
                    0,
                    0);
                break;

            case 0x19:
                ExecuteTest(parameters);
                break;

            case 0x1A:
                QueueResponse(CdRomInterruptType.Acknowledge, NoDiscStatus);
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
                break;

            default:
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(NoDiscStatus | 1),
                    0x40);
                break;
        }
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
                QueueResponse(CdRomInterruptType.Acknowledge, NoDiscStatus);
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
