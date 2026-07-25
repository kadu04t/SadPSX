namespace SadPSX.Core.Memory;

public sealed class MemoryControl
{
    public const uint Expansion1BaseAddress = 0x1F80_1000;
    public const uint Expansion2BaseAddress = 0x1F80_1004;
    public const uint Expansion1DelayAddress = 0x1F80_1008;
    public const uint Expansion3DelayAddress = 0x1F80_100C;
    public const uint BiosDelayAddress = 0x1F80_1010;
    public const uint SpuDelayAddress = 0x1F80_1014;
    public const uint CdromDelayAddress = 0x1F80_1018;
    public const uint Expansion2DelayAddress = 0x1F80_101C;
    public const uint CommonDelayAddress = 0x1F80_1020;
    public const uint RamSizeAddress = 0x1F80_1060;
    public const uint CacheControlAddress = 0xFFFE_0130;

    private static readonly HashSet<uint> RegisterAddresses =
    [
        Expansion1BaseAddress,
        Expansion2BaseAddress,
        Expansion1DelayAddress,
        Expansion3DelayAddress,
        BiosDelayAddress,
        SpuDelayAddress,
        CdromDelayAddress,
        Expansion2DelayAddress,
        CommonDelayAddress,
        RamSizeAddress,
        CacheControlAddress,
    ];

    private readonly Dictionary<uint, uint> _registers = new();

    public MemoryControl()
    {
        Reset();
    }

    public uint CacheControl => _registers[CacheControlAddress];

    public void Reset()
    {
        _registers.Clear();
        _registers[Expansion1BaseAddress] = 0x1F00_0000;
        _registers[Expansion2BaseAddress] = 0x1F80_2000;
        _registers[Expansion1DelayAddress] = 0x0013_243F;
        _registers[Expansion3DelayAddress] = 0x0000_3022;
        _registers[BiosDelayAddress] = 0x0013_243F;
        _registers[SpuDelayAddress] = 0x2009_31E1;
        _registers[CdromDelayAddress] = 0x0002_0843;
        _registers[Expansion2DelayAddress] = 0x0007_0777;
        _registers[CommonDelayAddress] = 0x0003_1125;
        _registers[RamSizeAddress] = 0x0000_0B88;
        _registers[CacheControlAddress] = 0;
    }

    public bool Handles(uint address)
    {
        return RegisterAddresses.Contains(address & ~0x03u);
    }

    public byte Read8(uint address)
    {
        uint registerAddress = GetRegisterAddress(address);
        int shift = (int)((address & 0x03) * 8);
        return (byte)(_registers[registerAddress] >> shift);
    }

    public ushort Read16(uint address)
    {
        return (ushort)(Read8(address) | (Read8(address + 1) << 8));
    }

    public uint Read32(uint address)
    {
        return (uint)(Read8(address)
            | (Read8(address + 1) << 8)
            | (Read8(address + 2) << 16)
            | (Read8(address + 3) << 24));
    }

    public void Write8(uint address, byte value)
    {
        uint registerAddress = GetRegisterAddress(address);
        int shift = (int)((address & 0x03) * 8);
        uint mask = 0xFFu << shift;
        uint updated = (_registers[registerAddress] & ~mask) | ((uint)value << shift);
        _registers[registerAddress] = ApplyWriteMask(registerAddress, updated);
    }

    public void Write16(uint address, ushort value)
    {
        Write8(address, (byte)value);
        Write8(address + 1, (byte)(value >> 8));
    }

    public void Write32(uint address, uint value)
    {
        uint registerAddress = GetRegisterAddress(address);

        if (address != registerAddress)
            throw new InvalidOperationException(
                $"Escrita de 32 bits desalinhada em Memory Control: 0x{address:X8}.");

        _registers[registerAddress] = ApplyWriteMask(registerAddress, value);
    }

    public static string GetRegisterName(uint address)
    {
        return (address & ~0x03u) switch
        {
            Expansion1BaseAddress => "EXP1_BASE",
            Expansion2BaseAddress => "EXP2_BASE",
            Expansion1DelayAddress => "EXP1_DELAY",
            Expansion3DelayAddress => "EXP3_DELAY",
            BiosDelayAddress => "BIOS_DELAY",
            SpuDelayAddress => "SPU_DELAY",
            CdromDelayAddress => "CDROM_DELAY",
            Expansion2DelayAddress => "EXP2_DELAY",
            CommonDelayAddress => "COMMON_DELAY",
            RamSizeAddress => "RAM_SIZE",
            CacheControlAddress => "CACHE_CONTROL",
            _ => "UNKNOWN",
        };
    }

    private uint GetRegisterAddress(uint address)
    {
        uint registerAddress = address & ~0x03u;

        if (!RegisterAddresses.Contains(registerAddress))
            throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence a Memory Control.");

        return registerAddress;
    }

    private static uint ApplyWriteMask(uint address, uint value)
    {
        return address switch
        {
            Expansion1BaseAddress or Expansion2BaseAddress =>
                0x1F00_0000 | (value & 0x00FF_FFFF),
            CommonDelayAddress => value & 0x0000_FFFF,
            _ => value,
        };
    }
}
