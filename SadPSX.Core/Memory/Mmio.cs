namespace SadPSX.Core.Memory;

/// <summary>
/// Stub temporário para a região de I/O Ports (0x1F80_1000 – 0x1F80_2FFF)
/// e Expansion Region 2 (0x1F80_2000 – 0x1F80_3FFF).
///
/// Nenhum periférico real (GPU, SPU, DMA, timers, controllers, CD-ROM etc.)
/// está implementado ainda — este stub existe para que o Bus tenha um
/// destino válido para esses endereços sem quebrar a execução. Leituras
/// não reconhecidas retornam 0; escritas são simplesmente descartadas.
///
/// Cada registrador real será migrado para uma classe dedicada (Gpu, Spu,
/// Dma, Timers, ...) conforme for implementado; até lá, os acessos passam
/// por aqui.
/// </summary>
public sealed class Mmio
{
    // Loga o endereço do último acesso não reconhecido, para facilitar
    // depuração enquanto o hardware real ainda não existe. Não afeta o
    // comportamento da emulação.
    public uint? LastUnhandledReadAddress { get; private set; }
    public uint? LastUnhandledWriteAddress { get; private set; }

    public byte Read8(uint address)
    {
        LastUnhandledReadAddress = address;
        return 0;
    }

    public void Write8(uint address, byte value)
    {
        LastUnhandledWriteAddress = address;
    }

    public ushort Read16(uint address)
    {
        LastUnhandledReadAddress = address;
        return 0;
    }

    public void Write16(uint address, ushort value)
    {
        LastUnhandledWriteAddress = address;
    }

    public uint Read32(uint address)
    {
        LastUnhandledReadAddress = address;
        return 0;
    }

    public void Write32(uint address, uint value)
    {
        LastUnhandledWriteAddress = address;
    }
}