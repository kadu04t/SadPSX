using SadPSX.Core.Interrupts;
using Xunit;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

namespace SadPSX.Tests.Gpu;

public sealed class Gp0CommandTests
{
    [Fact]
    public void FillRoundsHorizontalBoundsAndWritesFifteenBitColor()
    {
        var gpu = CreateGpu();

        SendGp0(
            gpu,
            0x0200_00FF,
            PackPosition(3, 2),
            PackSize(1, 1));

        Assert.Equal(0x001Fu, gpu.Vram.ReadPixel(0, 2));
        Assert.Equal(0x001Fu, gpu.Vram.ReadPixel(15, 2));
        Assert.Equal(0u, gpu.Vram.ReadPixel(16, 2));
    }

    [Fact]
    public void CpuToVramAcceptsOddPixelCountAndWrapsHorizontally()
    {
        var gpu = CreateGpu();

        SendGp0(
            gpu,
            0xA000_0000,
            PackPosition(1023, 4),
            PackSize(3, 1),
            0x2222_1111,
            0xFFFF_3333);

        Assert.Equal(0x1111u, gpu.Vram.ReadPixel(1023, 4));
        Assert.Equal(0x2222u, gpu.Vram.ReadPixel(0, 4));
        Assert.Equal(0x3333u, gpu.Vram.ReadPixel(1, 4));
    }

    [Fact]
    public void VramToCpuPacksPixelsAndUpdatesReadyStatus()
    {
        var gpu = CreateGpu();
        UploadPixels(gpu, 10, 20, 3, 1, 0x1234_5678, 0xFFFF_9ABC);

        SendGp0(
            gpu,
            0xC000_0000,
            PackPosition(10, 20),
            PackSize(3, 1));

        Assert.NotEqual(0u, gpu.Status & (1u << 27));
        Assert.Equal(0x1234_5678u, gpu.Peek32(GpuDevice.Gp0Address));
        Assert.Equal(0x1234_5678u, gpu.Read32(GpuDevice.Gp0Address));
        Assert.NotEqual(0u, gpu.Status & (1u << 27));
        Assert.Equal(0x0000_9ABCu, gpu.Read32(GpuDevice.Gp0Address));
        Assert.Equal(0u, gpu.Status & (1u << 27));
    }

    [Fact]
    public void VramCopyUsesTemporaryBufferForOverlappingRegions()
    {
        var gpu = CreateGpu();
        UploadPixels(gpu, 0, 0, 4, 1, 0x0002_0001, 0x0004_0003);

        SendGp0(
            gpu,
            0x8000_0000,
            PackPosition(0, 0),
            PackPosition(1, 0),
            PackSize(3, 1));

        Assert.Equal(0x0001u, gpu.Vram.ReadPixel(0, 0));
        Assert.Equal(0x0001u, gpu.Vram.ReadPixel(1, 0));
        Assert.Equal(0x0002u, gpu.Vram.ReadPixel(2, 0));
        Assert.Equal(0x0003u, gpu.Vram.ReadPixel(3, 0));
    }

    [Fact]
    public void MaskRulesApplyToTransfersAndDrawing()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);

        SendGp0(gpu, 0xE600_0001);
        UploadPixels(gpu, 30, 30, 1, 1, 0x0000_001F);
        Assert.Equal(0x801Fu, gpu.Vram.ReadPixel(30, 30));

        SendGp0(gpu, 0xE600_0002);
        SendGp0(
            gpu,
            0x6000_FF00,
            PackPosition(30, 30),
            PackSize(1, 1));

        Assert.Equal(0x801Fu, gpu.Vram.ReadPixel(30, 30));
    }

    [Fact]
    public void VariableRectangleUsesDrawingOffsetAndClipArea()
    {
        var gpu = CreateGpu();
        SendGp0(gpu, 0xE300_100A);
        SendGp0(gpu, 0xE400_140A);
        SendGp0(gpu, 0xE500_0802);

        SendGp0(
            gpu,
            0x6000_FF00,
            PackPosition(7, 3),
            PackSize(3, 2));

        Assert.Equal(0u, gpu.Vram.ReadPixel(9, 4));
        Assert.Equal(0x03E0u, gpu.Vram.ReadPixel(10, 4));
        Assert.Equal(0x03E0u, gpu.Vram.ReadPixel(10, 5));
        Assert.Equal(0u, gpu.Vram.ReadPixel(11, 5));
    }

    [Fact]
    public void RectangleWrapsCoordinateAfterAddingDrawingOffset()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        SendGp0(gpu, 0xE500_0400);

        SendGp0(
            gpu,
            0x6000_00FF,
            PackPosition(-1024, 0),
            PackSize(1, 1));

        Assert.Equal(0x001Fu, gpu.Vram.ReadPixel(0, 0));
    }

    [Fact]
    public void FlatTriangleRasterizesInsideDrawingArea()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);

        SendGp0(
            gpu,
            0x20FF_0000,
            PackPosition(10, 10),
            PackPosition(20, 10),
            PackPosition(10, 20));

        Assert.Equal(0x7C00u, gpu.Vram.ReadPixel(11, 11));
        Assert.Equal(0u, gpu.Vram.ReadPixel(19, 19));
    }

    [Fact]
    public void OversizedPolygonIsRejectedAndReported()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);

        SendGp0(
            gpu,
            0x20FF_FFFF,
            PackPosition(-1024, 0),
            PackPosition(1023, 0),
            PackPosition(0, 100));

        Assert.Equal(1ul, gpu.RejectedPrimitiveCount);
        Assert.Contains("V0=(-1024,0)", gpu.FirstRejectedPrimitive);
        Assert.Contains("V1=(1023,0)", gpu.FirstRejectedPrimitive);
    }

    [Fact]
    public void OversizedQuadHalfDoesNotDiscardValidTriangle()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);

        SendGp0(
            gpu,
            0x28FF_0000,
            PackPosition(10, 10),
            PackPosition(20, 10),
            PackPosition(10, 20),
            PackPosition(20, 600));

        Assert.Equal(0x7C00u, gpu.Vram.ReadPixel(11, 11));
        Assert.Equal(1ul, gpu.RejectedPrimitiveCount);
    }

    [Fact]
    public void FlatPolylineRasterizesUntilTerminator()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);

        SendGp0(
            gpu,
            0x4800_00FF,
            PackPosition(2, 2),
            PackPosition(5, 2),
            PackPosition(5, 5),
            0x5000_5000);

        Assert.Equal(0x001Fu, gpu.Vram.ReadPixel(3, 2));
        Assert.Equal(0x001Fu, gpu.Vram.ReadPixel(5, 4));
    }

    [Fact]
    public void RawFifteenBitTextureRectangleCopiesTexel()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 0, 0, 1, 1, 0x0000_4210);
        SendGp0(gpu, 0xE100_0100);

        SendGp0(
            gpu,
            0x6500_0000,
            PackPosition(40, 40),
            0x0000_0000,
            PackSize(1, 1));

        Assert.Equal(0x4210u, gpu.Vram.ReadPixel(40, 40));
    }

    [Fact]
    public void FourBitTextureUsesColorLookupTable()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 0, 0, 1, 1, 0x0000_0001);
        UploadPixels(gpu, 32, 0, 2, 1, 0x03E0_0000);
        SendGp0(gpu, 0xE100_0000);

        SendGp0(
            gpu,
            0x6500_0000,
            PackPosition(50, 50),
            0x0002_0000,
            PackSize(1, 1));

        Assert.Equal(0x03E0u, gpu.Vram.ReadPixel(50, 50));
    }

    [Fact]
    public void EightBitTextureUsesColorLookupTable()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 0, 1, 1, 1, 0x0000_0001);
        UploadPixels(gpu, 64, 0, 2, 1, 0x7C00_0000);
        SendGp0(gpu, 0xE100_0080);

        SendGp0(
            gpu,
            0x6500_0000,
            PackPosition(60, 60),
            0x0004_0100,
            PackSize(1, 1));

        Assert.Equal(0x7C00u, gpu.Vram.ReadPixel(60, 60));
    }

    [Fact]
    public void ColorLookupCachePersistsUntilExplicitlyCleared()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 0, 0, 1, 1, 0x0000_0001);
        UploadPixels(gpu, 32, 0, 2, 1, 0x03E0_0000);
        SendGp0(gpu, 0xE100_0000);

        DrawRawTextureRectangle(gpu, 50, 50, 0x0002_0000);
        UploadPixels(gpu, 33, 0, 1, 1, 0x0000_7C00);
        DrawRawTextureRectangle(gpu, 51, 50, 0x0002_0000);

        Assert.Equal(0x03E0u, gpu.Vram.ReadPixel(50, 50));
        Assert.Equal(0x03E0u, gpu.Vram.ReadPixel(51, 50));

        SendGp0(gpu, 0x0100_0000);
        DrawRawTextureRectangle(gpu, 52, 50, 0x0002_0000);

        Assert.Equal(0x7C00u, gpu.Vram.ReadPixel(52, 50));
    }

    [Fact]
    public void ChangingColorLookupLocationReloadsTheCache()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 0, 0, 1, 1, 0x0000_0001);
        UploadPixels(gpu, 32, 0, 2, 1, 0x03E0_0000);
        UploadPixels(gpu, 48, 0, 2, 1, 0x7C00_0000);
        SendGp0(gpu, 0xE100_0000);

        DrawRawTextureRectangle(gpu, 50, 50, 0x0002_0000);
        DrawRawTextureRectangle(gpu, 51, 50, 0x0003_0000);

        Assert.Equal(0x03E0u, gpu.Vram.ReadPixel(50, 50));
        Assert.Equal(0x7C00u, gpu.Vram.ReadPixel(51, 50));
    }

    [Fact]
    public void UntexturedDrawingDoesNotPopulateColorLookupCache()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 0, 0, 1, 1, 0x0000_0001);
        UploadPixels(gpu, 32, 0, 2, 1, 0x03E0_0000);
        SendGp0(gpu, 0xE100_0000);

        SendGp0(
            gpu,
            0x6000_00FF,
            PackPosition(40, 40),
            PackSize(1, 1));
        UploadPixels(gpu, 33, 0, 1, 1, 0x0000_7C00);
        DrawRawTextureRectangle(gpu, 50, 50, 0x0002_0000);

        Assert.Equal(0x7C00u, gpu.Vram.ReadPixel(50, 50));
    }

    [Fact]
    public void TextureWindowRemapsTextureCoordinates()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 8, 0, 1, 1, 0x0000_4210);
        SendGp0(gpu, 0xE100_0100);
        SendGp0(gpu, 0xE200_0401);

        SendGp0(
            gpu,
            0x6500_0000,
            PackPosition(70, 70),
            0x0000_0000,
            PackSize(1, 1));

        Assert.Equal(0x4210u, gpu.Vram.ReadPixel(70, 70));
    }

    [Fact]
    public void TexturedRectangleHonorsHorizontalFlip()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 0, 0, 2, 1, 0x03E0_001F);
        SendGp0(gpu, 0xE100_1100);

        SendGp0(
            gpu,
            0x6500_0000,
            PackPosition(80, 80),
            0x0000_0000,
            PackSize(2, 1));

        Assert.Equal(0x03E0u, gpu.Vram.ReadPixel(80, 80));
        Assert.Equal(0x001Fu, gpu.Vram.ReadPixel(81, 80));
    }

    [Fact]
    public void GouraudPolygonUsesFourByFourDitherMatrix()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        SendGp0(gpu, 0xE100_0200);

        SendGp0(
            gpu,
            0x3007_0707,
            PackPosition(0, 0),
            0x0007_0707,
            PackPosition(8, 0),
            0x0007_0707,
            PackPosition(0, 8));

        Assert.Equal(0u, gpu.Vram.ReadPixel(0, 0));
        Assert.Equal(0x0421u, gpu.Vram.ReadPixel(3, 0));
    }

    [Fact]
    public void FlatPolygonDoesNotUseDithering()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        SendGp0(gpu, 0xE100_0200);

        SendGp0(
            gpu,
            0x2007_0707,
            PackPosition(0, 0),
            PackPosition(8, 0),
            PackPosition(0, 8));

        Assert.Equal(0u, gpu.Vram.ReadPixel(3, 0));
    }

    [Fact]
    public void SharedQuadEdgeIsRasterizedOnlyOnce()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        SendGp0(gpu, 0xE100_0020);

        SendGp0(
            gpu,
            0x2A00_0040,
            PackPosition(10, 10),
            PackPosition(14, 10),
            PackPosition(10, 14),
            PackPosition(14, 14));

        Assert.Equal(0x0008u, gpu.Vram.ReadPixel(11, 12));
    }

    [Fact]
    public void OversizedPolygonIsDiscarded()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);

        SendGp0(
            gpu,
            0x2000_00FF,
            PackPosition(-600, 10),
            PackPosition(600, 10),
            PackPosition(0, 100));

        Assert.Equal(0u, gpu.Vram.ReadPixel(0, 20));
    }

    [Fact]
    public void OversizedLineIsDiscarded()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);

        SendGp0(
            gpu,
            0x4000_00FF,
            PackPosition(10, -300),
            PackPosition(10, 300));

        Assert.Equal(0u, gpu.Vram.ReadPixel(10, 10));
    }

    [Fact]
    public void TexturedTransparencyDependsOnTexelMaskBit()
    {
        var gpu = CreateGpu();
        ConfigureFullDrawingArea(gpu);
        UploadPixels(gpu, 64, 0, 2, 1, 0x801F_001F);
        UploadPixels(gpu, 20, 20, 2, 1, 0x03E0_03E0);
        SendGp0(gpu, 0xE100_0101);

        SendGp0(
            gpu,
            0x6700_0000,
            PackPosition(20, 20),
            0x0000_0000,
            PackSize(2, 1));

        Assert.Equal(0x001Fu, gpu.Vram.ReadPixel(20, 20));
        Assert.Equal(0x81EFu, gpu.Vram.ReadPixel(21, 20));
    }

    [Fact]
    public void ResetCommandBufferCancelsIncompletePacket()
    {
        var gpu = CreateGpu();
        SendGp0(gpu, 0xA000_0000);

        gpu.Write32(GpuDevice.GpuStatusAddress, 0x0100_0000);
        SendGp0(gpu, 0xE100_0355);

        Assert.Equal(0x355u, gpu.Status & 0x7FF);
    }

    private static GpuDevice CreateGpu()
    {
        return new GpuDevice(new InterruptController());
    }

    private static void ConfigureFullDrawingArea(GpuDevice gpu)
    {
        SendGp0(gpu, 0xE300_0000);
        SendGp0(gpu, 0xE407_FFFF);
    }

    private static void UploadPixels(
        GpuDevice gpu,
        int pixelX,
        int pixelY,
        int width,
        int height,
        params uint[] words)
    {
        SendGp0(
            gpu,
            0xA000_0000,
            PackPosition(pixelX, pixelY),
            PackSize(width, height));
        SendGp0(gpu, words);
    }

    private static void DrawRawTextureRectangle(
        GpuDevice gpu,
        int pixelX,
        int pixelY,
        uint textureWord)
    {
        SendGp0(
            gpu,
            0x6500_0000,
            PackPosition(pixelX, pixelY),
            textureWord,
            PackSize(1, 1));
    }

    private static void SendGp0(GpuDevice gpu, params uint[] words)
    {
        foreach (uint word in words)
            gpu.Write32(GpuDevice.Gp0Address, word);
    }

    private static uint PackPosition(int pixelX, int pixelY)
    {
        return (uint)(pixelX & 0xFFFF) | ((uint)(pixelY & 0xFFFF) << 16);
    }

    private static uint PackSize(int width, int height)
    {
        return (uint)(width & 0xFFFF) | ((uint)(height & 0xFFFF) << 16);
    }
}
