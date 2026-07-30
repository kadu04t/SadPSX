using SadPSX.Core.Bus;
using SadPSX.Core.Interrupts;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

namespace SadPSX.Tests.Gpu;

public sealed class GpuTests
{
    [Fact]
    public void ResetStatusMatchesGpuResetState()
    {
        var gpu = new GpuDevice(new InterruptController());

        Assert.Equal(GpuDevice.ResetStatus, gpu.Status);
    }

    [Fact]
    public void DisplayEnableCommandUpdatesStatus()
    {
        var gpu = new GpuDevice(new InterruptController());

        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0300_0000);
        Assert.Equal(0u, gpu.Status & (1u << 23));

        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0300_0001);

        Assert.NotEqual(0u, gpu.Status & (1u << 23));
    }

    [Fact]
    public void DmaDirectionUpdatesDirectionAndRequestBits()
    {
        var gpu = new GpuDevice(new InterruptController());

        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);

        Assert.Equal(2u, (gpu.Status >> 29) & 3);
        Assert.NotEqual(0u, gpu.Status & (1u << 25));
    }

    [Fact]
    public void DrawModeAndMaskCommandsUpdateStatus()
    {
        var gpu = new GpuDevice(new InterruptController());

        gpu.Write32(GpuDevice.Gp0Address, 0xE100_0355);
        gpu.Write32(GpuDevice.Gp0Address, 0xE600_0003);

        Assert.Equal(0x355u, gpu.Status & 0x7FF);
        Assert.Equal(3u, (gpu.Status >> 11) & 3);
    }

    [Fact]
    public void DrawModeMapsSecondTexturePageYBitToStatus()
    {
        var gpu = new GpuDevice(new InterruptController());

        gpu.Write32(GpuDevice.Gp0Address, 0xE100_0800);

        Assert.NotEqual(0u, gpu.Status & (1u << 15));
        Assert.Equal(0u, gpu.Status & (1u << 11));
    }

    [Fact]
    public void GpuInterruptSetsGpuStatAndInterruptController()
    {
        var interrupts = new InterruptController();
        var gpu = new GpuDevice(interrupts);

        gpu.Write32(GpuDevice.Gp0Address, 0x1F00_0000);

        Assert.NotEqual(0u, gpu.Status & (1u << 24));
        Assert.NotEqual(
            0,
            interrupts.Status & (1 << (int)InterruptSource.Gpu));

        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0200_0000);

        Assert.Equal(0u, gpu.Status & (1u << 24));
        Assert.NotEqual(
            0,
            interrupts.Status & (1 << (int)InterruptSource.Gpu));
    }

    [Fact]
    public void InternalVersionRegisterCanBeReadThroughGpuRead()
    {
        var gpu = new GpuDevice(new InterruptController());
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x1000_0007);

        uint version = gpu.Read32(GpuDevice.Gp0Address);

        Assert.Equal(2u, version);
    }

    [Fact]
    public void DisplayModeUpdatesResolutionAndVideoBits()
    {
        var gpu = new GpuDevice(new InterruptController());

        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0800_003D);

        Assert.Equal(1u, (gpu.Status >> 17) & 3);
        Assert.NotEqual(0u, gpu.Status & (1u << 19));
        Assert.NotEqual(0u, gpu.Status & (1u << 20));
        Assert.NotEqual(0u, gpu.Status & (1u << 21));
        Assert.NotEqual(0u, gpu.Status & (1u << 22));
        Assert.Equal(0u, gpu.Status & (1u << 13));
    }

    [Fact]
    public void GpuPortsAreHandledAndNamedInMmioLog()
    {
        var bus = new Bus();
        bus.Mmio.TraceMode = MmioTraceMode.Full;

        bus.Write32(GpuDevice.Gp0Address, 0xE100_1000);
        bus.Read32(GpuDevice.GpuStatusAddress);

        Assert.All(
            bus.Mmio.AccessSummaries,
            summary => Assert.True(summary.Handled));
        Assert.Contains(
            bus.Mmio.AccessSummaries,
            summary => summary.RegisterName == "GP1/GPUSTAT");
    }

    [Fact]
    public void DmaFifoAppliesBackpressureUntilGpuConsumesAWord()
    {
        var gpu = new GpuDevice(new InterruptController());
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);

        for (int word = 0; word < 16; word++)
            Assert.True(gpu.TryWriteDmaWord(0));

        Assert.False(gpu.TryWriteDmaWord(0));
        Assert.Equal(16, gpu.DmaFifoCount);
        Assert.Equal(0u, gpu.Status & (1u << 28));

        gpu.Tick(1);

        Assert.Equal(15, gpu.DmaFifoCount);
        Assert.NotEqual(0u, gpu.Status & (1u << 28));
    }
}
