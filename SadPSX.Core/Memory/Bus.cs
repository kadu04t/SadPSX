namespace SadPSX.Core.Memory;

/// <summary>
/// Barramento de memória do PS1: traduz endereços virtuais (KUSEG/KSEG0/
/// KSEG1/KSEG2) para endereços físicos e roteia o acesso para a região de
/// memória correspondente (RAM, Scratchpad, BIOS, MMIO, Cache Control).
///
/// Mapa físico implementado (ver psx-spx para referência):
///   0x0000_0000 - 0x001F_FFFF  RAM principal (2 MiB, espelhada até 8 MiB)
///   0x1F00_0000 - 0x1F7F_FFFF  Expansion Region 1 (stub: sem hardware
///                              conectado, leituras retornam 0xFF)
///   0x1F80_0000 - 0x1F8003FF   Scratchpad (1 KiB) — não espelhado em KSEG1
///   0x1F80_1000 - 0x1F80_1FFF  I/O Ports (stub Mmio por enquanto)
///   0x1F80_2000 - 0x1F80_3FFF  Expansion Region 2 (roteado para Mmio)
///   0x1FC0_0000 - 0x1FC7_FFFF  BIOS ROM (512 KiB, somente leitura)
///   0xFFFE_0000 - 0xFFFE_01FF  Cache Control (apenas via KSEG2)
///
/// Expansion Region 3 ainda não é implementada (raramente usada; nenhum
/// jogo comercial comum depende dela); um acesso a ela cai no caso
/// "endereço não mapeado" por enquanto.
/// </summary>
public sealed class Bus
{
    // --- Bases e tamanhos das regiões físicas ---
    private const uint RamSize = Ram.SizeInBytes;
    // A RAM é espelhada 4 vezes dentro da janela de 8 MiB reservada a ela
    // no mapa de memória (comportamento real do hardware).
    private const uint RamMirroredWindowSize = 8 * 1024 * 1024;

    private const uint Expansion1Base = 0x1F00_0000;
    private const uint Expansion1Size = 8 * 1024 * 1024; // 8 MiB (tamanho máximo documentado da região)

    private const uint ScratchpadBase = 0x1F80_0000;
    private const uint ScratchpadSize = Scratchpad.SizeInBytes;

    private const uint IoPortsBase = 0x1F80_1000;
    private const uint IoPortsSize = 0x1000; // 4 KiB (psx-spx atual; fontes mais antigas citam 8K, mas 4K evita sobreposição com Expansion 2)

    private const uint Expansion2Base = 0x1F80_2000;
    private const uint Expansion2Size = 0x2000; // 8 KiB

    private const uint BiosBase = 0x1FC0_0000;
    private const uint BiosSize = BiosRom.SizeInBytes;

    private const uint CacheControlBase = 0xFFFE_0000;
    private const uint CacheControlSize = 0x200;

    public Ram Ram { get; }
    public Scratchpad Scratchpad { get; }
    public BiosRom Bios { get; }
    public Mmio Mmio { get; }
    public ExpansionRegion1 Expansion1 { get; }

    public Bus(Ram ram, Scratchpad scratchpad, BiosRom bios, Mmio mmio, ExpansionRegion1 expansion1)
    {
        Ram = ram ?? throw new ArgumentNullException(nameof(ram));
        Scratchpad = scratchpad ?? throw new ArgumentNullException(nameof(scratchpad));
        Bios = bios ?? throw new ArgumentNullException(nameof(bios));
        Mmio = mmio ?? throw new ArgumentNullException(nameof(mmio));
        Expansion1 = expansion1 ?? throw new ArgumentNullException(nameof(expansion1));
    }

    // Overload de conveniência: cria Scratchpad/BiosRom/Mmio/Expansion1
    // próprios, reaproveitando a RAM fornecida. Mantém compatibilidade com
    // código existente que só se importa em compartilhar a RAM entre CPU
    // e testes.
    public Bus(Ram ram) : this(ram, new Scratchpad(), new BiosRom(), new Mmio(), new ExpansionRegion1())
    {
    }

    public Bus() : this(new Ram(), new Scratchpad(), new BiosRom(), new Mmio(), new ExpansionRegion1())
    {
    }

    public byte Read8(uint address)
    {
        var (region, offset) = Route(address, out _);
        return region switch
        {
            MemoryRegion.Ram => Ram.Read8(offset),
            MemoryRegion.Scratchpad => Scratchpad.Read8(offset),
            MemoryRegion.Mmio => Mmio.Read8(offset + IoPortsBase),
            MemoryRegion.Bios => Bios.Read8(offset),
            MemoryRegion.Expansion1 => Expansion1.Read8(offset),
            MemoryRegion.CacheControl => 0, // Cache Control não tem leitura significativa modelada ainda
            _ => throw UnmappedAddress(address),
        };
    }

    public void Write8(uint address, byte value)
    {
        var (region, offset) = Route(address, out _);
        switch (region)
        {
            case MemoryRegion.Ram: Ram.Write8(offset, value); break;
            case MemoryRegion.Scratchpad: Scratchpad.Write8(offset, value); break;
            case MemoryRegion.Mmio: Mmio.Write8(offset + IoPortsBase, value); break;
            case MemoryRegion.Bios: break; // ROM: escrita é ignorada, como no hardware real
            case MemoryRegion.Expansion1: Expansion1.Write8(offset, value); break;
            case MemoryRegion.CacheControl: break; // ainda não modelado
            default: throw UnmappedAddress(address);
        }
    }

    public ushort Read16(uint address)
    {
        var (region, offset) = Route(address, out _);
        return region switch
        {
            MemoryRegion.Ram => Ram.Read16(offset),
            MemoryRegion.Scratchpad => Scratchpad.Read16(offset),
            MemoryRegion.Mmio => Mmio.Read16(offset + IoPortsBase),
            MemoryRegion.Bios => Bios.Read16(offset),
            MemoryRegion.Expansion1 => Expansion1.Read16(offset),
            MemoryRegion.CacheControl => 0,
            _ => throw UnmappedAddress(address),
        };
    }

    public void Write16(uint address, ushort value)
    {
        var (region, offset) = Route(address, out _);
        switch (region)
        {
            case MemoryRegion.Ram: Ram.Write16(offset, value); break;
            case MemoryRegion.Scratchpad: Scratchpad.Write16(offset, value); break;
            case MemoryRegion.Mmio: Mmio.Write16(offset + IoPortsBase, value); break;
            case MemoryRegion.Bios: break;
            case MemoryRegion.Expansion1: Expansion1.Write16(offset, value); break;
            case MemoryRegion.CacheControl: break;
            default: throw UnmappedAddress(address);
        }
    }

    public uint Read32(uint address)
    {
        var (region, offset) = Route(address, out _);
        return region switch
        {
            MemoryRegion.Ram => Ram.Read32(offset),
            MemoryRegion.Scratchpad => Scratchpad.Read32(offset),
            MemoryRegion.Mmio => Mmio.Read32(offset + IoPortsBase),
            MemoryRegion.Bios => Bios.Read32(offset),
            MemoryRegion.Expansion1 => Expansion1.Read32(offset),
            MemoryRegion.CacheControl => 0,
            _ => throw UnmappedAddress(address),
        };
    }

    public void Write32(uint address, uint value)
    {
        var (region, offset) = Route(address, out _);
        switch (region)
        {
            case MemoryRegion.Ram: Ram.Write32(offset, value); break;
            case MemoryRegion.Scratchpad: Scratchpad.Write32(offset, value); break;
            case MemoryRegion.Mmio: Mmio.Write32(offset + IoPortsBase, value); break;
            case MemoryRegion.Bios: break;
            case MemoryRegion.Expansion1: Expansion1.Write32(offset, value); break;
            case MemoryRegion.CacheControl: break;
            default: throw UnmappedAddress(address);
        }
    }

    /// <summary>
    /// Traduz um endereço virtual para endereço físico, de acordo com o
    /// segmento (KUSEG/KSEG0/KSEG1/KSEG2) indicado pelos 3 bits mais altos.
    /// </summary>
    public static uint TranslateToPhysical(uint virtualAddress, out MemorySegment segment)
    {
        uint topThreeBits = virtualAddress >> 29;

        switch (topThreeBits)
        {
            case 0b000:
            case 0b001:
            case 0b010:
            case 0b011:
                segment = MemorySegment.Kuseg;
                return virtualAddress;

            case 0b100:
                segment = MemorySegment.Kseg0;
                return virtualAddress & 0x1FFF_FFFF;

            case 0b101:
                segment = MemorySegment.Kseg1;
                return virtualAddress & 0x1FFF_FFFF;

            default: // 0b110, 0b111
                segment = MemorySegment.Kseg2;
                return virtualAddress; // KSEG2 não é mascarado; endereços aqui são tratados como especiais (Cache Control)
        }
    }

    private (MemoryRegion Region, uint Offset) Route(uint virtualAddress, out MemorySegment segment)
    {
        uint physical = TranslateToPhysical(virtualAddress, out segment);

        // Cache Control só existe em KSEG2, e não é mascarado pela
        // tradução acima — checamos o endereço virtual original.
        if (segment == MemorySegment.Kseg2)
        {
            if (virtualAddress >= CacheControlBase && virtualAddress < CacheControlBase + CacheControlSize)
                return (MemoryRegion.CacheControl, virtualAddress - CacheControlBase);

            return (MemoryRegion.Unmapped, 0);
        }

        if (physical < RamMirroredWindowSize)
            return (MemoryRegion.Ram, physical % RamSize);

        if (physical >= Expansion1Base && physical < Expansion1Base + Expansion1Size)
            return (MemoryRegion.Expansion1, physical - Expansion1Base);

        if (physical >= ScratchpadBase && physical < ScratchpadBase + ScratchpadSize)
        {
            // O scratchpad não é espelhado em KSEG1 (comportamento real do
            // hardware) — um acesso vindo desse segmento não é válido.
            if (segment == MemorySegment.Kseg1)
                return (MemoryRegion.Unmapped, 0);

            return (MemoryRegion.Scratchpad, physical - ScratchpadBase);
        }

        if (physical >= IoPortsBase && physical < IoPortsBase + IoPortsSize)
            return (MemoryRegion.Mmio, physical - IoPortsBase);

        if (physical >= Expansion2Base && physical < Expansion2Base + Expansion2Size)
        {
            // Roteado para o mesmo stub de I/O que a região anterior.
            // O offset aqui é relativo a IoPortsBase (não a Expansion2Base)
            // de propósito: como Read/Write* fazem "offset + IoPortsBase"
            // ao repassar para Mmio, o resultado final reconstrói o
            // endereço físico original de qualquer forma, então o Mmio
            // sempre recebe o endereço absoluto correto independente de
            // qual das duas regiões originou o acesso.
            return (MemoryRegion.Mmio, physical - IoPortsBase);
        }

        if (physical >= BiosBase && physical < BiosBase + BiosSize)
            return (MemoryRegion.Bios, physical - BiosBase);

        return (MemoryRegion.Unmapped, 0);
    }

    private static InvalidOperationException UnmappedAddress(uint address) =>
        new($"Endereço 0x{address:X8} não corresponde a nenhuma região de memória mapeada.");

    private enum MemoryRegion
    {
        Ram,
        Scratchpad,
        Mmio,
        Bios,
        Expansion1,
        CacheControl,
        Unmapped,
    }
}

/// <summary>
/// Segmentos de endereço virtual do R3000A (não confundir com as regiões
/// físicas de memória — vários segmentos podem apontar para a mesma região
/// física, como acontece com KUSEG/KSEG0/KSEG1 sobre a RAM).
/// </summary>
public enum MemorySegment
{
    Kuseg,
    Kseg0,
    Kseg1,
    Kseg2,
}