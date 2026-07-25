using SadPSX.Core.Memory;
using Xunit;

namespace SadPSX.Tests.Memory;

public sealed class BusTests
{
    [Theory]
    [InlineData(0x0000_0000u, MemorySegment.Kuseg, 0x0000_0000u)]
    [InlineData(0x001F_FFFFu, MemorySegment.Kuseg, 0x001F_FFFFu)]
    [InlineData(0x8000_0000u, MemorySegment.Kseg0, 0x0000_0000u)]
    [InlineData(0x801F_FFFFu, MemorySegment.Kseg0, 0x001F_FFFFu)]
    [InlineData(0xA000_0000u, MemorySegment.Kseg1, 0x0000_0000u)]
    [InlineData(0xBFC0_0000u, MemorySegment.Kseg1, 0x1FC0_0000u)] // vetor de reset da BIOS
    [InlineData(0x9FC0_0000u, MemorySegment.Kseg0, 0x1FC0_0000u)] // mesmo físico via KSEG0
    [InlineData(0xFFFE_0130u, MemorySegment.Kseg2, 0xFFFE_0130u)] // KSEG2 não é mascarado
    public void TranslateToPhysicalMapsSegmentsCorrectly(
        uint virtualAddress, MemorySegment expectedSegment, uint expectedPhysical)
    {
        uint physical = Bus.TranslateToPhysical(virtualAddress, out var segment);

        Assert.Equal(expectedSegment, segment);
        Assert.Equal(expectedPhysical, physical);
    }

    [Fact]
    public void KusegKseg0AndKseg1AllShareTheSameUnderlyingRam()
    {
        var bus = new Bus();

        // Escreve via KSEG0...
        bus.Write32(0x8000_1000, 0xCAFE_BABE);

        // ...e lê o mesmo dado via KUSEG e KSEG1, já que os três segmentos
        // apontam para o mesmo endereço físico de RAM.
        Assert.Equal(0xCAFE_BABEu, bus.Read32(0x0000_1000));
        Assert.Equal(0xCAFE_BABEu, bus.Read32(0xA000_1000));
    }

    [Fact]
    public void RamIsMirroredWithinTheEightMegabyteWindow()
    {
        var bus = new Bus();

        bus.Write32(0x0000_0100, 0x1111_2222);

        // A RAM física tem 2 MiB, mas a janela reservada a ela no mapa de
        // memória é de 8 MiB — o hardware real espelha o conteúdo 4 vezes
        // dentro dessa janela.
        Assert.Equal(0x1111_2222u, bus.Read32(0x0020_0100)); // +1 espelho
        Assert.Equal(0x1111_2222u, bus.Read32(0x0040_0100)); // +2 espelhos
        Assert.Equal(0x1111_2222u, bus.Read32(0x0060_0100)); // +3 espelhos
    }

    [Fact]
    public void ScratchpadIsAccessibleThroughKusegAndKseg0()
    {
        var bus = new Bus();

        // Via KUSEG
        bus.Write32(0x1F80_0010, 0x1234_5678);
        Assert.Equal(0x1234_5678u, bus.Read32(0x1F80_0010));

        // Via KSEG0 (mesmo endereço físico)
        Assert.Equal(0x1234_5678u, bus.Read32(0x9F80_0010));
    }

    [Fact]
    public void ScratchpadIsNotAccessibleThroughKseg1()
    {
        var bus = new Bus();

        // O scratchpad não é espelhado em KSEG1 no hardware real — um
        // acesso por esse segmento deve ser tratado como não mapeado.
        Assert.Throws<InvalidOperationException>(() => bus.Read32(0xBF80_0010));
        Assert.Throws<InvalidOperationException>(() => bus.Write32(0xBF80_0010, 0x1234));
    }

    [Fact]
    public void ScratchpadWritesDoNotLeakIntoRam()
    {
        var bus = new Bus();

        bus.Write32(0x1F80_0000, 0xDEAD_BEEF);

        // Endereços de RAM não devem ser afetados: scratchpad e RAM são
        // regiões físicas completamente separadas.
        Assert.Equal(0u, bus.Read32(0x0000_0000));
    }

    [Fact]
    public void BiosIsReadOnlyWritesAreSilentlyIgnored()
    {
        var bus = new Bus();
        var biosImage = new byte[BiosRom.SizeInBytes];
        biosImage[0] = 0xEF; // marcador simples para detectar mudanças

        bus.Bios.Load(biosImage);

        // Tenta escrever na BIOS através do Bus, via KSEG1 (endereço de boot).
        bus.Write32(0xBFC0_0000, 0xFFFF_FFFF);

        // A escrita deve ter sido ignorada: o conteúdo original permanece.
        Assert.Equal(0xEFu, bus.Read8(0xBFC0_0000));
    }

    [Fact]
    public void BiosIsAccessibleThroughKseg0AndKseg1WithSameContent()
    {
        var bus = new Bus();
        var biosImage = new byte[BiosRom.SizeInBytes];
        biosImage[0] = 0x11;
        biosImage[1] = 0x22;
        biosImage[2] = 0x33;
        biosImage[3] = 0x44;

        bus.Bios.Load(biosImage);

        uint viaKseg1 = bus.Read32(0xBFC0_0000);
        uint viaKseg0 = bus.Read32(0x9FC0_0000);

        Assert.Equal(viaKseg1, viaKseg0);
        Assert.Equal(0x4433_2211u, viaKseg1); // little-endian
    }

    [Fact]
    public void MmioReadReturnsZeroForUnimplementedRegisters()
    {
        var bus = new Bus();

        // Nenhum periférico real está implementado ainda; leituras da
        // região de I/O devem retornar 0 em vez de lançar exceção.
        uint value = bus.Read32(0x1F80_1070); // endereço de I_STAT no hardware real

        Assert.Equal(0u, value);
    }

    [Fact]
    public void MmioWriteDoesNotThrow()
    {
        var bus = new Bus();

        var exception = Record.Exception(() => bus.Write32(0x1F80_1070, 0xFFFF_FFFF));

        Assert.Null(exception);
    }

    [Fact]
    public void CacheControlIsOnlyAccessibleThroughKseg2()
    {
        var bus = new Bus();

        // Endereço de Cache Control só é válido através de KSEG2
        // (0xFFFE_0000 já está fisicamente nessa faixa).
        var exception = Record.Exception(() => bus.Read32(0xFFFE_0130));

        Assert.Null(exception); // não deve lançar; deve rotear para CacheControl
    }

    [Fact]
    public void CompletelyUnmappedAddressThrows()
    {
        var bus = new Bus();

        // Expansion Region 3 (0x1FA0_0000+) ainda não é implementada —
        // continua sendo um caso genuíno de "endereço não mapeado".
        Assert.Throws<InvalidOperationException>(() => bus.Read32(0x1FA0_0000));
    }

    [Fact]
    public void Expansion1ReturnsFloatingBusValueWhenReadAsByte()
    {
        var bus = new Bus();

        // Mesmo endereço observado na prática durante o boot real da BIOS:
        // ela sonda o ROM Header de expansão em busca de um cartucho
        // conectado. Sem nada conectado, o barramento retorna 0xFF.
        byte value = bus.Read8(0x1F00_0084);

        Assert.Equal(0xFF, value);
    }

    [Fact]
    public void Expansion1WritesAreSilentlyIgnored()
    {
        var bus = new Bus();

        var exception = Record.Exception(() => bus.Write32(0x1F00_0000, 0x1234_5678));

        Assert.Null(exception);
        Assert.Equal(0xFFFF_FFFFu, bus.Read32(0x1F00_0000)); // valor não mudou
    }

    [Fact]
    public void Expansion1IsAccessibleThroughKuseg0AndKseg1()
    {
        var bus = new Bus();

        Assert.Equal(0xFFu, bus.Read8(0x1F00_0000));   // KUSEG
        Assert.Equal(0xFFu, bus.Read8(0x9F00_0000));   // KSEG0
        Assert.Equal(0xFFu, bus.Read8(0xBF00_0000));   // KSEG1
    }

    [Fact]
    public void Expansion1DoesNotOverlapWithScratchpad()
    {
        var bus = new Bus();

        // O último byte de Expansion1 (0x1F7F_FFFF) não deve afetar o
        // scratchpad, que começa logo em seguida (0x1F80_0000).
        bus.Write32(0x1F80_0000, 0xCAFE_BABE);

        Assert.Equal(0xFFFF_FFFFu, bus.Read32(0x1F7F_FFFC)); // ainda dentro de Expansion1
        Assert.Equal(0xCAFE_BABEu, bus.Read32(0x1F80_0000)); // scratchpad intacto
    }
}