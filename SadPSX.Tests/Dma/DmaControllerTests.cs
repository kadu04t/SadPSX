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
        bus.Dma.Tick(2);
        bus.Gpu.Tick(2);

        Assert.Equal(2ul, bus.Gpu.Gp0CommandCount);
        Assert.Equal(0xE600_0003u, bus.Gpu.LastGp0Command);
        Assert.Equal(0x108u, bus.Dma.GetChannel(2).BaseAddress);
        Assert.Equal(0x0000_0002u, bus.Dma.GetChannel(2).BlockControl);
    }

    [Fact]
    public void GpuBlockTransferUploadsImageToVram()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        bus.Ram.Write32(0x100, 0xA000_0000);
        bus.Ram.Write32(0x104, 0x000C_000B);
        bus.Ram.Write32(0x108, 0x0001_0002);
        bus.Ram.Write32(0x10C, 0x4321_1234);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0004);

        bus.Write32(ChannelRegister(2, 8), 0x0100_0201);
        bus.Dma.Tick(4);
        bus.Gpu.Tick(4);

        Assert.Equal(0x1234u, bus.Gpu.Vram.ReadPixel(11, 12));
        Assert.Equal(0x4321u, bus.Gpu.Vram.ReadPixel(12, 12));
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
        bus.Dma.Tick(3);
        bus.Gpu.Tick(2);

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
    public void GpuDmaWaitsForDirectionAndCompletesIncrementally()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Ram.Write32(0x100, 0xE100_0123);
        bus.Ram.Write32(0x104, 0xE600_0003);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0002);
        bus.Write32(ChannelRegister(2, 8), 0x0100_0201);

        bus.Dma.Tick(4);

        Assert.Equal(0ul, bus.Gpu.Gp0CommandCount);
        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));

        bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        bus.Dma.Tick(1);

        Assert.Equal(1, bus.Gpu.DmaFifoCount);
        Assert.Equal(0ul, bus.Gpu.Gp0CommandCount);
        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));

        bus.Gpu.Tick(1);
        Assert.Equal(1ul, bus.Gpu.Gp0CommandCount);

        bus.Dma.Tick(1);
        bus.Gpu.Tick(1);

        Assert.Equal(2ul, bus.Gpu.Gp0CommandCount);
        Assert.Equal(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
    }

    [Fact]
    public void GpuBurstKeepsBaseAddressAfterCompletion()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Ram.Write32(0x100, 0xE100_0123);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 1);
        bus.Write32(ChannelRegister(2, 8), 0x1100_0001);

        bus.Dma.Tick(1);

        Assert.Equal(0x100u, bus.Dma.GetChannel(2).BaseAddress);
        Assert.Equal(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
    }

    [Fact]
    public void OutOfRangeTransferSetsBusErrorAndMasterFlag()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Write32(ChannelRegister(2, 0), 0x0080_0000);
        bus.Write32(ChannelRegister(2, 4), 1);

        bus.Write32(ChannelRegister(2, 8), 0x1100_0001);
        bus.Dma.Tick(1);

        uint interrupt = bus.Read32(DmaController.InterruptAddress);
        Assert.NotEqual(0u, interrupt & (1u << 15));
        Assert.NotEqual(0u, interrupt & (1u << 31));
    }

    [Fact]
    public void GpuDmaWaitsWhenFifoIsFullAndResumesAfterConsumption()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        for (uint word = 0; word < 17; word++)
            bus.Ram.Write32(0x100 + word * 4, 0);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0011);
        bus.Write32(ChannelRegister(2, 8), 0x0100_0201);

        bus.Dma.Tick(16);
        bus.Dma.Tick(1);

        Assert.Equal(16, bus.Gpu.DmaFifoCount);
        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));

        bus.Gpu.Tick(1);
        bus.Dma.Tick(1);

        Assert.Equal(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
        Assert.Equal(16, bus.Gpu.DmaFifoCount);

        bus.Gpu.Tick(16);

        Assert.Equal(17ul, bus.Gpu.Gp0CommandCount);
    }

    [Fact]
    public void GpuBurstChoppingAlternatesDmaAndCpuWindows()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        for (uint word = 0; word < 4; word++)
            bus.Ram.Write32(0x100 + word * 4, 0);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 4);
        bus.Write32(ChannelRegister(2, 8), 0x1100_0101);

        bus.Dma.Tick(1);

        Assert.Equal(1, bus.Gpu.DmaFifoCount);
        Assert.Equal(0x104u, bus.Dma.GetChannel(2).BaseAddress);
        Assert.Equal(3u, bus.Dma.GetChannel(2).BlockControl & 0xFFFF);

        bus.Dma.Tick(1);

        Assert.Equal(1, bus.Gpu.DmaFifoCount);

        bus.Dma.Tick(1);

        Assert.Equal(2, bus.Gpu.DmaFifoCount);
        Assert.Equal(0x108u, bus.Dma.GetChannel(2).BaseAddress);
        Assert.Equal(2u, bus.Dma.GetChannel(2).BlockControl & 0xFFFF);
    }

    [Fact]
    public void ChoppingInSliceModeKeepsGpuDmaHalted()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        bus.Ram.Write32(0x100, 0);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0001);
        bus.Write32(ChannelRegister(2, 8), 0x0100_0301);

        bus.Dma.Tick(100);

        Assert.Equal(0, bus.Gpu.DmaFifoCount);
        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
    }

    [Fact]
    public void ForcedBurstPauseBitStopsAndResumesGpuDma()
    {
        var bus = new Bus();
        EnableChannel(bus, 2);
        bus.Ram.Write32(0x100, 0);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 1);
        bus.Write32(ChannelRegister(2, 8), 0x3100_0001);

        bus.Dma.Tick(1);

        Assert.Equal(0, bus.Gpu.DmaFifoCount);

        bus.Write32(ChannelRegister(2, 8), 0x1100_0001);
        bus.Dma.Tick(1);

        Assert.Equal(1, bus.Gpu.DmaFifoCount);
        Assert.Equal(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
    }

    [Fact]
    public void DpcrPrioritySelectsGpuBeforeLowerPriorityOtc()
    {
        var bus = new Bus();
        ConfigurePendingGpuAndOtc(bus);
        EnableChannelsWithPriorities(
            bus,
            gpuPriority: 0,
            otcPriority: 7);

        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(6).ChannelControl & (1u << 24));
        Assert.Equal(0ul, bus.Dma.CompletedTransfers);
    }

    [Fact]
    public void DpcrPriorityCompletesOtcBeforeLowerPriorityGpu()
    {
        var bus = new Bus();
        ConfigurePendingGpuAndOtc(bus);
        EnableChannelsWithPriorities(
            bus,
            gpuPriority: 7,
            otcPriority: 0);

        Assert.Equal(
            0u,
            bus.Dma.GetChannel(6).ChannelControl & (1u << 24));
        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
        Assert.Equal(1ul, bus.Dma.CompletedTransfers);
        Assert.Equal(0x00FF_FFFFu, bus.Ram.Read32(0x200));
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

    private static void ConfigurePendingGpuAndOtc(Bus bus)
    {
        bus.Ram.Write32(0x100, 0);
        bus.Write32(ChannelRegister(2, 0), 0x100);
        bus.Write32(ChannelRegister(2, 4), 1);
        bus.Write32(ChannelRegister(2, 8), 0x1100_0001);

        bus.Write32(ChannelRegister(6, 0), 0x200);
        bus.Write32(ChannelRegister(6, 4), 1);
        bus.Write32(ChannelRegister(6, 8), 0x1100_0002);
    }

    private static void EnableChannelsWithPriorities(
        Bus bus,
        uint gpuPriority,
        uint otcPriority)
    {
        uint control = bus.Dma.Control;
        control &= ~((0xFu << 8) | (0xFu << 24));
        control |= ((gpuPriority | 8) << 8) |
                   ((otcPriority | 8) << 24);
        bus.Write32(DmaController.ControlAddress, control);
    }
}
