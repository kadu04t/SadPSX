using SadPSX.Core.Interrupts;
using SadPSX.Frontend.Video;
using Xunit;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

namespace SadPSX.Tests.Gpu;

public sealed class VideoFrameBufferTests
{
    [Fact]
    public void CaptureKeepsOnlyTheNewestCompleteFrame()
    {
        GpuDevice gpu = CreateVisibleGpu();
        var frames = new VideoFrameBuffer();

        frames.Capture(gpu);
        frames.Capture(gpu);

        VideoPresentationMetrics metrics = frames.GetMetrics();
        Assert.True(frames.HasFrame);
        Assert.Equal(2ul, metrics.CapturedFrames);
        Assert.Equal(1ul, metrics.DroppedFrames);
        Assert.Equal(1ul, metrics.ConsecutiveDuplicateFrames);

        frames.MarkPresented();

        Assert.False(frames.HasFrame);
        Assert.Equal(1ul, frames.GetMetrics().PresentedFrames);
    }

    [Fact]
    public void DisplaySignatureChangesWithVisibleVram()
    {
        GpuDevice gpu = CreateVisibleGpu();
        var frames = new VideoFrameBuffer();
        frames.Capture(gpu);
        ulong blankSignature = frames.LastSignature;
        frames.MarkPresented();

        gpu.Vram.WritePixel(0, 0, 0x001F);
        frames.Capture(gpu);

        Assert.NotEqual(blankSignature, frames.LastSignature);
        Assert.Equal(0ul, frames.ConsecutiveDuplicateFrames);
    }

    private static GpuDevice CreateVisibleGpu()
    {
        var gpu = new GpuDevice(new InterruptController());
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0300_0000);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0500_0000);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0703_C010);
        return gpu;
    }
}
