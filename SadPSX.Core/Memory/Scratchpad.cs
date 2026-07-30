namespace SadPSX.Core.Memory;

/// <summary>
/// Scratchpad do PS1: 1 KiB de RAM rápida (fisicamente é o Data Cache do
/// R3000A, reaproveitado pelo hardware como memória endereçável comum).
///
/// Importante: ao contrário da RAM principal, o scratchpad NÃO é espelhado
/// em KSEG1 — só é acessível através de KUSEG e KSEG0. O <see cref="Bus"/>
/// é responsável por impor essa restrição; esta classe apenas implementa
/// o armazenamento em si.
/// </summary>
public sealed class Scratchpad
{
    public const uint SizeInBytes = 1024; // 1 KiB

    private readonly byte[] _data = new byte[SizeInBytes];

    public byte Read8(uint offset)
    {
        return _data[Mask(offset)];
    }

    public void Write8(uint offset, byte value)
    {
        _data[Mask(offset)] = value;
    }

    public ushort Read16(uint offset)
    {
        uint o = Mask(offset);
        return (ushort)(_data[o] | (_data[o + 1] << 8));
    }

    public void Write16(uint offset, ushort value)
    {
        uint o = Mask(offset);
        _data[o] = (byte)value;
        _data[o + 1] = (byte)(value >> 8);
    }

    public uint Read32(uint offset)
    {
        uint o = Mask(offset);
        return (uint)(_data[o]
            | (_data[o + 1] << 8)
            | (_data[o + 2] << 16)
            | (_data[o + 3] << 24));
    }

    public void Write32(uint offset, uint value)
    {
        uint o = Mask(offset);
        _data[o] = (byte)value;
        _data[o + 1] = (byte)(value >> 8);
        _data[o + 2] = (byte)(value >> 16);
        _data[o + 3] = (byte)(value >> 24);
    }

    // Sem espelhamento: um acesso fora dos 1024 bytes é um erro de mapa de
    // memória de quem chamou (o Bus nunca deveria repassar um offset fora
    // desse intervalo). Usamos módulo mesmo assim por defesa, igual à RAM.
    private static uint Mask(uint offset) => offset & (SizeInBytes - 1);
}
