using SadPSX.Core.Gpu;
using SadPSX.Core.Interrupts;
using Xunit;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

namespace SadPSX.Tests.Gpu;

public sealed class GpuDisplayTests
{
    [Fact]
    public void DisplayInfoReflectsGpuModeAndVramStart()
    {
        var gpu = new GpuDevice(new InterruptController());
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0501_940C);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0800_0027);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0300_0000);

        GpuDisplayInfo display = gpu.GetDisplayInfo();

        Assert.Equal(12, display.VramX);
        Assert.Equal(101, display.VramY);
        Assert.Equal(640, display.Width);
        Assert.Equal(480, display.Height);
        Assert.True(display.Enabled);
        Assert.False(display.IsPal);
        Assert.True(display.IsInterlaced);
    }

    [Fact]
    public void CopyDisplayConvertsFifteenBitPixelsToRgba()
    {
        var gpu = new GpuDevice(new InterruptController());
        gpu.Write32(GpuDevice.Gp0Address, 0xA000_0000);
        gpu.Write32(GpuDevice.Gp0Address, 0x0002_0001);
        gpu.Write32(GpuDevice.Gp0Address, 0x0001_0002);
        gpu.Write32(GpuDevice.Gp0Address, 0x03E0_001F);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0500_0801);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0300_0000);
        var pixels = new uint[256 * 240];

        gpu.CopyDisplayRgba(pixels);

        Assert.Equal(0xFF00_00FFu, pixels[0]);
        Assert.Equal(0xFF00_FF00u, pixels[1]);
    }

    [Fact]
    public void DisabledDisplayProducesBlackFrame()
    {
        var gpu = new GpuDevice(new InterruptController());
        var pixels = new uint[256 * 240];
        Array.Fill(pixels, uint.MaxValue);

        gpu.CopyDisplayRgba(pixels);

        Assert.All(pixels, pixel => Assert.Equal(0xFF00_0000u, pixel));
    }

    [Fact]
    public void CopyDisplayDecodesPackedTwentyFourBitPixels()
    {
        var gpu = new GpuDevice(new InterruptController());
        gpu.Write32(GpuDevice.Gp0Address, 0xA000_0000);
        gpu.Write32(GpuDevice.Gp0Address, 0x0000_0000);
        gpu.Write32(GpuDevice.Gp0Address, 0x0001_0003);
        gpu.Write32(GpuDevice.Gp0Address, 0x4433_2211);
        gpu.Write32(GpuDevice.Gp0Address, 0x0000_6655);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0800_0010);
        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0300_0000);
        var pixels = new uint[256 * 240];

        gpu.CopyDisplayRgba(pixels);

        Assert.Equal(0xFF33_2211u, pixels[0]);
        Assert.Equal(0xFF66_5544u, pixels[1]);
    }
}
