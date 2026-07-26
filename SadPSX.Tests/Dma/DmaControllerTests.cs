using SadPSX.Core.Dma;
using SadPSX.Core.Interrupts;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

namespace SadPSX.Tests.Dma;

public sealed class DmaControllerTests
{
    [Fact]
    public void ResetRegistersAreExposedThroughMmio()
    {
        var bus = new Bus();

        Assert.Equal(
            DmaController.ResetControl,
            bus.Read32(DmaController.ControlAddress));
        Assert.Equal(
            2u,
            bus.Read32(ChannelRegister(6, 8)));
        Assert.All(
            bus.Mmio.AccessSummaries,
            summary => Assert.True(summary.Handled));
    }

    [Fact]
    public void OtcBuildsReverseOrderingTable()
    {
        var bus = new Bus();
        EnableChannel(bus, 6);
        bus.Write32(ChannelRegister(6, 0), 0x0000_100C);
        bus.Write32(ChannelRegister(6, 4), 4);

        bus.Write32(ChannelRegister(6, 8), 0x1100_0002);

        Assert.Equal(0x0000_1008u, bus.Ram.Read32(0x100C));
        Assert.Equal(0x0000_1004u, bus.Ram.Read32(0x1008));
        Assert.Equal(0x0000_1000u, bus.Ram.Read32(0x1004));
        Assert.Equal(0x00FF_FFFFu, bus.Ram.Read32(0x1000));
        Assert.Equal(
            0u,
            bus.Dma.GetChannel(6).ChannelControl & (1u << 24));
    }

    [Fact]
    public void GpuBlockTransferSendsRamWordsToGp0()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        bus.Ram.Write32(0x100, 0xE100_0123);
        bus.Ram.Write32(0x104, 0xE600_0003);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0002);

        bus.Write32(ChannelRegister(2, 8), 0x0100_0201);

        Assert.Equal(2ul, bus.Gpu.Gp0CommandCount);
        Assert.Equal(0xE600_0003u, bus.Gpu.LastGp0Command);
        Assert.Equal(0x108u, bus.Dma.GetChannel(2).BaseAddress);
        Assert.Equal(0x0000_0002u, bus.Dma.GetChannel(2).BlockControl);
    }

    [Fact]
    public void GpuLinkedListStopsAtEndMarker()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        bus.Ram.Write32(0x100, 0x0280_0000);
        bus.Ram.Write32(0x104, 0xE100_0555);
        bus.Ram.Write32(0x108, 0xE600_0001);
        bus.Write32(ChannelRegister(2, 0), 0x100);

        bus.Write32(ChannelRegister(2, 8), 0x0100_0401);

        Assert.Equal(2ul, bus.Gpu.Gp0CommandCount);
        Assert.Equal(0x0080_0000u, bus.Dma.GetChannel(2).BaseAddress);
        Assert.Equal(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
    }

    [Fact]
    public void EnabledCompletionRaisesDicrAndDmaInterrupt()
    {
        var bus = new Bus();
        EnableChannel(bus, 6);
        uint interruptEnable = (1u << 23) | (1u << 22);
        bus.Write32(DmaController.InterruptAddress, interruptEnable);
        bus.Write32(ChannelRegister(6, 0), 0x1000);
        bus.Write32(ChannelRegister(6, 4), 1);

        bus.Write32(ChannelRegister(6, 8), 0x1100_0002);

        uint interrupt = bus.Read32(DmaController.InterruptAddress);
        Assert.NotEqual(0u, interrupt & (1u << 30));
        Assert.NotEqual(0u, interrupt & (1u << 31));
        Assert.NotEqual(
            0,
            bus.InterruptController.Status &
            (1 << (int)InterruptSource.Dma));

        bus.Write32(
            DmaController.InterruptAddress,
            interruptEnable | (1u << 30));

        Assert.Equal(
            0u,
            bus.Read32(DmaController.InterruptAddress) &
            ((1u << 30) | (1u << 31)));
        Assert.NotEqual(
            0,
            bus.InterruptController.Status &
            (1 << (int)InterruptSource.Dma));
    }

    [Fact]
    public void OutOfRangeTransferSetsBusErrorAndMasterFlag()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Write32(ChannelRegister(2, 0), 0x0080_0000);
        bus.Write32(ChannelRegister(2, 4), 1);

        bus.Write32(ChannelRegister(2, 8), 0x1100_0001);

        uint interrupt = bus.Read32(DmaController.InterruptAddress);
        Assert.NotEqual(0u, interrupt & (1u << 15));
        Assert.NotEqual(0u, interrupt & (1u << 31));
    }

    private static uint ChannelRegister(int channel, uint offset) =>
        DmaController.ChannelBaseAddress + (uint)channel * 0x10 + offset;

    private static void EnableChannel(Bus bus, int channel)
    {
        uint enableBit = 1u << (channel * 4 + 3);
        bus.Write32(
            DmaController.ControlAddress,
            bus.Dma.Control | enableBit);
    }
}
