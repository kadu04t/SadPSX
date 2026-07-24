namespace SadPSX.Core.Memory;

/// <summary>
/// Barramento de memória do PSX. Por enquanto só conhece a RAM;
/// no futuro vai rotear também para BiosRom e portas de I/O
/// com base no mapa de memória real do console.
/// </summary>
public sealed class Bus
{
    private readonly Ram _ram;

    public Bus(Ram ram)
    {
        _ram = ram ?? throw new ArgumentNullException(nameof(ram));
    }

    public Bus() : this(new Ram())
    {
    }

    public byte Read8(uint address) => _ram.Read8(address);
    public void Write8(uint address, byte value) => _ram.Write8(address, value);

    public ushort Read16(uint address) => _ram.Read16(address);
    public void Write16(uint address, ushort value) => _ram.Write16(address, value);

    public uint Read32(uint address) => _ram.Read32(address);
    public void Write32(uint address, uint value) => _ram.Write32(address, value);
}