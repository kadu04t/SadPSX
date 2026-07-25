namespace SadPSX.Core.Memory;

/// <summary>
/// Stub para Expansion Region 1 (0x1F00_0000 – 0x1F7F_FFFF), usado por
/// cartuchos de expansão opcionais (raramente presentes em consoles reais).
///
/// Sem hardware conectado, o barramento fica "flutuando" e todas as
/// leituras retornam 0xFF — é assim que o hardware real se comporta, e é
/// o valor que a BIOS espera ao sondar o ROM Header de expansão
/// (0x1F00_0000–0x1F00_00FF) durante o boot para detectar se algum
/// dispositivo está presente. Um valor de leitura diferente (como 0)
/// poderia ser interpretado erroneamente como um cabeçalho de expansão
/// válido.
///
/// Escritas são simplesmente descartadas, já que não há nada real
/// conectado para recebê-las.
/// </summary>
public sealed class ExpansionRegion1
{
    private const byte FloatingBusValue = 0xFF;

    public byte Read8(uint offset) => FloatingBusValue;

    public void Write8(uint offset, byte value)
    {
        // Nada conectado: escrita não tem efeito.
    }

    public ushort Read16(uint offset) => 0xFFFF;

    public void Write16(uint offset, ushort value)
    {
    }

    public uint Read32(uint offset) => 0xFFFF_FFFF;

    public void Write32(uint offset, uint value)
    {
    }
}