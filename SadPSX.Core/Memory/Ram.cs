namespace SadPSX.Core.Memory;

/// <summary>
/// RAM principal do PlayStation 1: 2 MiB, endereçada a partir de 0x0000_0000.
/// O hardware real é little-endian, então byte 0 é o menos significativo.
/// </summary>
public sealed class Ram
{
    public const uint SizeInBytes = 2 * 1024 * 1024; // 2 MiB

    private readonly byte[] _data = new byte[SizeInBytes];

    public byte Read8(uint address)
    {
        return _data[Mask(address)];
    }

    public void Write8(uint address, byte value)
    {
        _data[Mask(address)] = value;
    }

    public ushort Read16(uint address)
    {
        uint offset = Mask(address);
        return (ushort)(_data[offset] | (_data[offset + 1] << 8));
    }

    public void Write16(uint address, ushort value)
    {
        uint offset = Mask(address);
        _data[offset] = (byte)value;
        _data[offset + 1] = (byte)(value >> 8);
    }

    public uint Read32(uint address)
    {
        uint offset = Mask(address);
        return (uint)(_data[offset]
            | (_data[offset + 1] << 8)
            | (_data[offset + 2] << 16)
            | (_data[offset + 3] << 24));
    }

    public void Write32(uint address, uint value)
    {
        uint offset = Mask(address);
        _data[offset] = (byte)value;
        _data[offset + 1] = (byte)(value >> 8);
        _data[offset + 2] = (byte)(value >> 16);
        _data[offset + 3] = (byte)(value >> 24);
    }

    // Por enquanto, sem mirroring (0x0080_0000 / 0x0100_0000):
    // apenas dobra o endereço dentro do tamanho físico da RAM.
    // Isso será revisitado quando o Bus tratar as regiões do mapa de memória do PS1.
    private static uint Mask(uint address) => address % SizeInBytes;
}