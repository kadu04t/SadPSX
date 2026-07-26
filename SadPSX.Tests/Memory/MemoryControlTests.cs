using SadPSX.Core.Bus;
using SadPSX.Core.Memory;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Memory;

public sealed class MemoryControlTests
{
    [Fact]
    public void MemoryControlRegistersRoundTripThroughBus()
    {
        var bus = new Bus();

        bus.Write32(MemoryControl.RamSizeAddress, 0x0000_0888);

        Assert.Equal(0x0000_0888u, bus.Read32(MemoryControl.RamSizeAddress));
        Assert.Equal(
            0x0000_0888u,
            bus.MemoryControl.Read32(MemoryControl.RamSizeAddress));
    }

    [Fact]
    public void CacheControlRoundTripsThroughKseg2()
    {
        var bus = new Bus();

        bus.Write32(MemoryControl.CacheControlAddress, 0x0000_0804);

        Assert.Equal(
            0x0000_0804u,
            bus.Read32(MemoryControl.CacheControlAddress));
        Assert.Equal(0x0000_0804u, bus.MemoryControl.CacheControl);
    }

    [Fact]
    public void PartialWritesPreserveOtherRegisterBytes()
    {
        var bus = new Bus();
        bus.Write32(MemoryControl.RamSizeAddress, 0x1122_3344);

        bus.Write8(MemoryControl.RamSizeAddress + 1, 0xAA);
        bus.Write16(MemoryControl.RamSizeAddress + 2, 0xBEEF);

        Assert.Equal(0xBEEF_AA44u, bus.Read32(MemoryControl.RamSizeAddress));
    }

    [Fact]
    public void ExpansionBaseKeepsFixedHighByte()
    {
        var memoryControl = new MemoryControl();

        memoryControl.Write32(MemoryControl.Expansion1BaseAddress, 0xAB12_3456);

        Assert.Equal(
            0x1F12_3456u,
            memoryControl.Read32(MemoryControl.Expansion1BaseAddress));
    }

    [Fact]
    public void MmioLogKeepsFirstAccessesAndAggregatesCounts()
    {
        var bus = new Bus();

        bus.Write32(MemoryControl.RamSizeAddress, 0x0000_0B88);
        bus.Read32(MemoryControl.RamSizeAddress);
        bus.Read32(0x1F80_1080);
        bus.Read32(0x1F80_1080);

        Assert.Equal(4ul, bus.Mmio.TotalAccessCount);
        Assert.Equal(4, bus.Mmio.AccessLog.Count);

        MmioAccessSummary unhandledReads = Assert.Single(
            bus.Mmio.AccessSummaries,
            summary => summary.Address == 0x1F80_1080);

        Assert.Equal(2ul, unhandledReads.Count);
        Assert.False(unhandledReads.Handled);
        Assert.Equal("UNHANDLED", unhandledReads.RegisterName);
    }

    [Fact]
    public void BusReportsInstructionFetchSeparatelyFromDataReads()
    {
        var bus = new Bus();
        var accesses = new List<MemoryAccess>();
        bus.MemoryAccessed += accesses.Add;

        bus.Write32(0, 0);
        bus.ReadInstruction32(0);
        bus.Read32(0);

        Assert.Contains(
            accesses,
            access => access.Kind == MemoryAccessKind.InstructionFetch);
        Assert.Contains(
            accesses,
            access => access.Kind == MemoryAccessKind.Read);
        Assert.Contains(
            accesses,
            access => access.Kind == MemoryAccessKind.Write);
    }
}
