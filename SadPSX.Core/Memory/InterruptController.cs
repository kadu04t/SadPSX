namespace SadPSX.Core.Memory;

public sealed class InterruptController : IMmioDevice
{
    public const uint StatusAddress = 0x1F80_1070;
    public const uint MaskAddress = 0x1F80_1074;

    private const ushort ValidBitsMask = 0x07FF;

    private ushort _status;
    private ushort _mask;

    public ushort Status => _status;

    public ushort Mask => _mask;

    public bool IsPending => (_status & _mask) != 0;

    public event Action<bool>? PendingChanged;

    public void Reset()
    {
        bool wasPending = IsPending;
        _status = 0;
        _mask = 0;
        NotifyPendingChange(wasPending);
    }

    public void Request(InterruptSource source)
    {
        int bit = (int)source;
        if ((uint)bit > 10)
            throw new ArgumentOutOfRangeException(nameof(source));

        bool wasPending = IsPending;
        _status |= (ushort)(1 << bit);
        NotifyPendingChange(wasPending);
    }

    public bool Handles(uint address)
    {
        uint registerAddress = address & ~3u;
        return registerAddress is StatusAddress or MaskAddress;
    }

    public byte Read8(uint address)
    {
        ushort value = ReadRegister(address);
        int shift = (int)((address & 3) * 8);
        return shift < 16 ? (byte)(value >> shift) : (byte)0;
    }

    public ushort Read16(uint address)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Leitura de 16 bits desalinhada no controlador de IRQ: 0x{address:X8}.");

        return (address & 2) == 0 ? ReadRegister(address) : (ushort)0;
    }

    public uint Read32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada no controlador de IRQ: 0x{address:X8}.");

        return ReadRegister(address);
    }

    public void Write8(uint address, byte value)
    {
        uint registerAddress = address & ~3u;
        int shift = (int)((address & 3) * 8);
        if (shift >= 16)
            return;

        ushort laneMask = (ushort)(0xFF << shift);
        ushort expandedValue = (ushort)(value << shift);
        WriteRegister(registerAddress, expandedValue, laneMask);
    }

    public void Write16(uint address, ushort value)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Escrita de 16 bits desalinhada no controlador de IRQ: 0x{address:X8}.");

        if ((address & 2) == 0)
            WriteRegister(address & ~3u, value, 0xFFFF);
    }

    public void Write32(uint address, uint value)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Escrita de 32 bits desalinhada no controlador de IRQ: 0x{address:X8}.");

        WriteRegister(address, (ushort)value, 0xFFFF);
    }

    public string GetRegisterName(uint address)
    {
        return (address & ~3u) switch
        {
            StatusAddress => "I_STAT",
            MaskAddress => "I_MASK",
            _ => "UNKNOWN",
        };
    }

    private ushort ReadRegister(uint address)
    {
        return (address & ~3u) switch
        {
            StatusAddress => _status,
            MaskAddress => _mask,
            _ => throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence ao controlador de IRQ."),
        };
    }

    private void WriteRegister(
        uint registerAddress,
        ushort value,
        ushort writeMask)
    {
        bool wasPending = IsPending;

        switch (registerAddress)
        {
            case StatusAddress:
                _status &= (ushort)(value | ~writeMask);
                _status &= ValidBitsMask;
                break;

            case MaskAddress:
                _mask = (ushort)((_mask & ~writeMask) | (value & writeMask));
                _mask &= ValidBitsMask;
                break;

            default:
                throw new InvalidOperationException(
                    $"Endereço 0x{registerAddress:X8} não pertence ao controlador de IRQ.");
        }

        NotifyPendingChange(wasPending);
    }

    private void NotifyPendingChange(bool previousValue)
    {
        bool currentValue = IsPending;
        if (currentValue != previousValue)
            PendingChanged?.Invoke(currentValue);
    }
}

public enum InterruptSource
{
    VBlank = 0,
    Gpu = 1,
    CdRom = 2,
    Dma = 3,
    Timer0 = 4,
    Timer1 = 5,
    Timer2 = 6,
    Controller = 7,
    Sio = 8,
    Spu = 9,
    Expansion = 10,
}
