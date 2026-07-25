namespace SadPSX.Core.Memory;

/// <summary>
/// ROM da BIOS do PlayStation 1: 512 KiB, somente leitura pela CPU.
/// O conteúdo real precisa ser carregado externamente (dump de BIOS);
/// sem isso, a ROM permanece zerada e qualquer tentativa de rodar código
/// dela vai executar instruções inválidas (todas zero = SLL $zero,$zero,0,
/// ou seja, uma sequência infinita de NOPs, não um crash).
/// </summary>
public sealed class BiosRom
{
    public const uint SizeInBytes = 512 * 1024; // 512 KiB

    private readonly byte[] _data = new byte[SizeInBytes];

    /// <summary>
    /// Carrega uma imagem de BIOS a partir de um array de bytes. O array
    /// deve ter exatamente <see cref="SizeInBytes"/> bytes.
    /// </summary>
    public void Load(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Length != SizeInBytes)
        {
            throw new ArgumentException(
                $"A imagem de BIOS deve ter exatamente {SizeInBytes} bytes, mas tem {image.Length}.",
                nameof(image));
        }

        Array.Copy(image, _data, _data.Length);
    }

    public byte Read8(uint offset)
    {
        return _data[Mask(offset)];
    }

    public ushort Read16(uint offset)
    {
        uint o = Mask(offset);
        return (ushort)(_data[o] | (_data[o + 1] << 8));
    }

    public uint Read32(uint offset)
    {
        uint o = Mask(offset);
        return (uint)(_data[o]
            | (_data[o + 1] << 8)
            | (_data[o + 2] << 16)
            | (_data[o + 3] << 24));
    }

    // A BIOS é somente leitura pela CPU: escritas nela são normalmente
    // silenciosamente ignoradas pelo hardware real (a região é mapeada
    // como ROM). Não há métodos Write* propositalmente.

    private static uint Mask(uint offset) => offset % SizeInBytes;
}
