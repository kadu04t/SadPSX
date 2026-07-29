using SadPSX.Core.Bus;
using SadPSX.Core.Interrupts;

namespace SadPSX.Core.Gpu;

public sealed partial class Gpu : IMmioDevice, IClockedDevice
{
    public const uint Gp0Address = 0x1F80_1810;
    public const uint GpuStatusAddress = 0x1F80_1814;
    public const uint ResetStatus = 0x1480_2000;

    private const uint DisplayDisabledBit = 1u << 23;
    private const uint InterruptRequestBit = 1u << 24;
    private const uint DmaRequestBit = 1u << 25;
    private const uint ReadyForCommandBit = 1u << 26;
    private const uint ReadyForVramReadBit = 1u << 27;
    private const uint ReadyForDmaBlockBit = 1u << 28;
    private const uint DmaDirectionMask = 3u << 29;

    private readonly InterruptController _interruptController;
    private readonly uint[] _internalRegisters = new uint[8];
    private readonly Queue<uint> _dmaFifo = new(16);

    private uint _status;
    private uint _gpuRead;

    public Gpu(InterruptController interruptController)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        Reset();
    }

    public uint Status => _status;

    public uint GpuRead => _gpuRead;

    public Vram Vram { get; } = new();

    public uint LastGp0Command { get; private set; }

    public uint LastGp1Command { get; private set; }

    public ulong Gp0CommandCount { get; private set; }

    public ulong Gp1CommandCount { get; private set; }
    public int DmaFifoCount => _dmaFifo.Count;

    public uint DisplayVramStart { get; private set; }

    public uint HorizontalDisplayRange { get; private set; }

    public uint VerticalDisplayRange { get; private set; }

    public bool IsPalMode => (_status & (1u << 20)) != 0;

    public bool IsInterlaced => (_status & (1u << 22)) != 0;

    internal uint DmaDirection => (_status >> 29) & 3;

    internal bool CanReceiveDmaBlock =>
        (_status & ReadyForDmaBlockBit) != 0;

    internal bool CanSendVramToCpu =>
        (_status & ReadyForVramReadBit) != 0;

    public uint DotClockDivisor
    {
        get
        {
            if ((_status & (1u << 16)) != 0)
                return 7;

            return ((_status >> 17) & 3) switch
            {
                0 => 10,
                1 => 8,
                2 => 5,
                3 => 4,
                _ => 10,
            };
        }
    }

    public void Reset()
    {
        Array.Clear(_internalRegisters);
        _status = ResetStatus;
        _gpuRead = 0;
        ResetGp0CommandBuffer();
        LastGp0Command = 0;
        LastGp1Command = 0;
        Gp0CommandCount = 0;
        Gp1CommandCount = 0;
        DisplayVramStart = 0;
        HorizontalDisplayRange = 0x00C6_0200;
        VerticalDisplayRange = 0x0003_C010;
    }

    public void Tick(uint cycles)
    {
        for (uint cycle = 0;
             cycle < cycles && _dmaFifo.TryDequeue(out uint value);
             cycle++)
        {
            ExecuteGp0(value);
        }

        UpdateGp0ReadyStatus();
    }

    internal bool TryWriteDmaWord(uint value)
    {
        const int fifoCapacity = 16;
        if (_dmaFifo.Count >= fifoCapacity)
            return false;

        _dmaFifo.Enqueue(value);
        UpdateGp0ReadyStatus();
        return true;
    }

    public bool Handles(uint address)
    {
        uint registerAddress = address & ~3u;
        return registerAddress is Gp0Address or GpuStatusAddress;
    }

    public byte Read8(uint address)
    {
        uint value = Read32(address & ~3u);
        int shift = (int)((address & 3) * 8);
        return (byte)(value >> shift);
    }

    public ushort Read16(uint address)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Leitura de 16 bits desalinhada na GPU: 0x{address:X8}.");

        uint value = Read32(address & ~3u);
        int shift = (int)((address & 2) * 8);
        return (ushort)(value >> shift);
    }

    public uint Read32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada na GPU: 0x{address:X8}.");

        return address switch
        {
            Gp0Address => ReadGpuRead(true),
            GpuStatusAddress => _status,
            _ => throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence à GPU."),
        };
    }

    public uint Peek32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada na GPU: 0x{address:X8}.");

        return address switch
        {
            Gp0Address => ReadGpuRead(false),
            GpuStatusAddress => _status,
            _ => throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence à GPU."),
        };
    }

    public void Write8(uint address, byte value)
    {
        Write32(address & ~3u, (uint)value << (int)((address & 3) * 8));
    }

    public void Write16(uint address, ushort value)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Escrita de 16 bits desalinhada na GPU: 0x{address:X8}.");

        Write32(address & ~3u, (uint)value << (int)((address & 2) * 8));
    }

    public void Write32(uint address, uint value)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Escrita de 32 bits desalinhada na GPU: 0x{address:X8}.");

        switch (address)
        {
            case Gp0Address:
                ExecuteGp0(value);
                break;

            case GpuStatusAddress:
                ExecuteGp1(value);
                break;

            default:
                throw new InvalidOperationException(
                    $"Endereço 0x{address:X8} não pertence à GPU.");
        }
    }

    public string GetRegisterName(uint address)
    {
        return (address & ~3u) switch
        {
            Gp0Address => "GP0/GPUREAD",
            GpuStatusAddress => "GP1/GPUSTAT",
            _ => "UNKNOWN",
        };
    }

    private void ExecuteGp1(uint value)
    {
        LastGp1Command = value;
        Gp1CommandCount++;

        byte command = (byte)(value >> 24);
        uint parameter = value & 0x00FF_FFFF;

        switch (command)
        {
            case 0x00:
                Reset();
                break;

            case 0x01:
                ResetGp0CommandBuffer();
                break;

            case 0x02:
                _status &= ~InterruptRequestBit;
                break;

            case 0x03:
                if ((parameter & 1) != 0)
                    _status |= DisplayDisabledBit;
                else
                    _status &= ~DisplayDisabledBit;
                break;

            case 0x04:
                _status = (_status & ~DmaDirectionMask) |
                          ((parameter & 3) << 29);
                break;

            case 0x05:
                DisplayVramStart = parameter & 0x000F_FFFF;
                break;

            case 0x06:
                HorizontalDisplayRange = parameter & 0x00FF_FFFF;
                break;

            case 0x07:
                VerticalDisplayRange = parameter & 0x000F_FFFF;
                break;

            case 0x08:
                ApplyDisplayMode(parameter);
                break;

            case >= 0x10 and <= 0x1F:
                ReadInternalRegister(parameter & 0x0F);
                break;
        }

        UpdateDmaRequest();
    }

    private void ApplyDisplayMode(uint parameter)
    {
        const uint displayModeStatusMask = 0x007F_6000;
        uint statusBits =
            ((parameter & 0x40) << 10) |
            ((parameter & 0x03) << 17) |
            ((parameter & 0x04) << 17) |
            ((parameter & 0x08) << 17) |
            ((parameter & 0x10) << 17) |
            ((parameter & 0x20) << 17) |
            ((parameter & 0x80) << 7);

        _status = (_status & ~displayModeStatusMask) |
                  (statusBits & displayModeStatusMask);

        if ((parameter & 0x20) == 0)
            _status |= 1u << 13;
    }

    private void ReadInternalRegister(uint index)
    {
        _gpuRead = index switch
        {
            2 => _internalRegisters[2],
            3 => _internalRegisters[3],
            4 => _internalRegisters[4],
            5 => _internalRegisters[5],
            7 => 2,
            _ => _gpuRead,
        };
    }

    private void UpdateDmaRequest()
    {
        uint direction = (_status >> 29) & 3;
        bool requested = direction switch
        {
            0 => false,
            1 => _dmaFifo.Count < 16,
            2 => (_status & ReadyForDmaBlockBit) != 0,
            3 => (_status & ReadyForVramReadBit) != 0,
            _ => false,
        };

        if (requested)
            _status |= DmaRequestBit;
        else
            _status &= ~DmaRequestBit;
    }

    private void ApplyTexturePageStatus(uint drawMode)
    {
        const uint texturePageStatusMask = 0x0000_87FF;
        uint texturePageStatus =
            (drawMode & 0x0000_07FF) |
            ((drawMode & (1u << 11)) << 4);
        _status = (_status & ~texturePageStatusMask) |
                  texturePageStatus;
    }

    internal void SetVideoTimingStatus(
        bool inVBlank,
        uint scanline,
        ulong frameCount)
    {
        const uint oddLineBit = 1u << 31;
        bool oddLine = !inVBlank &&
            (((_status & (1u << 19)) != 0 && IsInterlaced)
                ? (frameCount & 1) != 0
                : (scanline & 1) != 0);

        if (oddLine)
            _status |= oddLineBit;
        else
            _status &= ~oddLineBit;
    }
}
