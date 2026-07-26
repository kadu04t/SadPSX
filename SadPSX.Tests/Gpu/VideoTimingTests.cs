using SadPSX.Core.Interrupts;
using SadPSX.Core.Timers;
using Xunit;
using GpuDevice = SadPSX.Core.Gpu.Gpu;
using VideoTimingDevice = SadPSX.Core.Gpu.VideoTiming;

namespace SadPSX.Tests.Gpu;

public sealed class VideoTimingTests
{
    [Fact]
    public void NtscTimingRaisesVBlankInterrupt()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);

        timing.Tick(520_000);

        Assert.True(timing.InVBlank);
        Assert.InRange(timing.CurrentScanline, 240u, 262u);
        Assert.NotEqual(
            0,
            interrupts.Status & (1 << (int)InterruptSource.VBlank));
    }

    [Fact]
    public void PalModeUsesPalFrameGeometry()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0800_0008);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);

        timing.Tick(690_000);

        Assert.Equal(VideoTimingDevice.PalScanlinesPerFrame, timing.ScanlinesPerFrame);
        Assert.Equal(VideoTimingDevice.PalVideoCyclesPerScanline, timing.VideoCyclesPerScanline);
        Assert.Equal(1ul, timing.FrameCount);
    }

    [Fact]
    public void DotClockFeedsTimerZero()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);
        timers.Write16(RootCounters.Timer0BaseAddress + 4, 1 << 8);
        timers.Tick(2);

        timing.Tick(100);

        Assert.Equal(15, timers.GetCounter(0));
    }

    [Fact]
    public void NtscDotClockDiscardsFractionAtEachScanline()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);
        timers.Write16(RootCounters.Timer0BaseAddress + 4, 1 << 8);
        timers.Tick(2);

        timing.Tick(2_153);

        Assert.Equal(1u, timing.CurrentScanline);
        Assert.Equal(341, timers.GetCounter(0));
    }

    [Fact]
    public void Pal320DotClockRoundsFractionUp()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0800_0009);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);
        timers.Write16(RootCounters.Timer0BaseAddress + 4, 1 << 8);
        timers.Tick(2);

        timing.Tick(2_169);

        Assert.Equal(1u, timing.CurrentScanline);
        Assert.Equal(426, timers.GetCounter(0));
    }

    [Fact]
    public void HBlankFeedsTimerOneOncePerScanline()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);
        timers.Write16(RootCounters.Timer1BaseAddress + 4, 1 << 8);
        timers.Tick(2);

        timing.Tick(2_200);

        Assert.Equal(1, timers.GetCounter(1));
    }

    [Fact]
    public void ResetResynchronizesBlankSignalsWithTimers()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);
        timers.Reset();

        timing.Reset();
        timers.Write16(RootCounters.Timer0BaseAddress + 4, 1);
        timers.Tick(3);

        Assert.Equal(0, timers.GetCounter(0));
    }

    [Fact]
    public void GpuStatOddLineClearsDuringVBlank()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        var gpu = new GpuDevice(interrupts);
        var timing = new VideoTimingDevice(gpu, timers, interrupts);

        timing.Tick(36_600);

        Assert.Equal(17u, timing.CurrentScanline);
        Assert.NotEqual(0u, gpu.Status & (1u << 31));

        timing.Tick(485_000);

        Assert.True(timing.InVBlank);
        Assert.Equal(0u, gpu.Status & (1u << 31));
    }
}
