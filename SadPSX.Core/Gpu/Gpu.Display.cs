namespace SadPSX.Core.Gpu;

public sealed partial class Gpu
{
    public GpuDisplayInfo GetDisplayInfo()
    {
        int width = (_status & (1u << 16)) != 0
            ? 368
            : ((_status >> 17) & 3) switch
            {
                0 => 256,
                1 => 320,
                2 => 512,
                3 => 640,
                _ => 256,
            };
        int verticalStart = (int)(VerticalDisplayRange & 0x03FF);
        int verticalEnd = (int)((VerticalDisplayRange >> 10) & 0x03FF);
        int visibleScanlines = Math.Max(1, verticalEnd - verticalStart);
        int height = (_status & (1u << 19)) != 0
            ? visibleScanlines * 2
            : visibleScanlines;
        height = Math.Min(height, Vram.Height);

        return new GpuDisplayInfo(
            (int)(DisplayVramStart & 0x3FF),
            (int)((DisplayVramStart >> 10) & 0x1FF),
            width,
            height,
            (_status & DisplayDisabledBit) == 0,
            (_status & (1u << 21)) != 0,
            IsPalMode,
            IsInterlaced);
    }

    public void CopyDisplayRgba(Span<uint> destination)
    {
        GpuDisplayInfo display = GetDisplayInfo();
        int requiredPixels = display.Width * display.Height;
        if (destination.Length < requiredPixels)
        {
            throw new ArgumentException(
                $"O buffer de vídeo precisa comportar {requiredPixels} pixels.",
                nameof(destination));
        }

        Span<uint> output = destination[..requiredPixels];
        if (!display.Enabled)
        {
            output.Fill(0xFF00_0000);
            return;
        }

        if (display.Is24BitColor)
            Copy24BitDisplay(output, display);
        else
            Copy15BitDisplay(output, display);
    }

    private void Copy15BitDisplay(
        Span<uint> destination,
        GpuDisplayInfo display)
    {
        for (int pixelY = 0; pixelY < display.Height; pixelY++)
        {
            for (int pixelX = 0; pixelX < display.Width; pixelX++)
            {
                ushort pixel = Vram.ReadPixel(
                    display.VramX + pixelX,
                    display.VramY + pixelY);
                destination[pixelY * display.Width + pixelX] =
                    Convert15BitToRgba(pixel);
            }
        }
    }

    private void Copy24BitDisplay(
        Span<uint> destination,
        GpuDisplayInfo display)
    {
        for (int pixelY = 0; pixelY < display.Height; pixelY++)
        {
            int vramY = (display.VramY + pixelY) & (Vram.Height - 1);
            int rowByteOffset = display.VramX * 2;

            for (int pixelX = 0; pixelX < display.Width; pixelX++)
            {
                int byteOffset = rowByteOffset + pixelX * 3;
                byte red = ReadVramByte(byteOffset, vramY);
                byte green = ReadVramByte(byteOffset + 1, vramY);
                byte blue = ReadVramByte(byteOffset + 2, vramY);
                destination[pixelY * display.Width + pixelX] =
                    PackRgba(red, green, blue);
            }
        }
    }

    private byte ReadVramByte(int byteOffset, int pixelY)
    {
        int wrappedOffset = byteOffset & (Vram.Width * 2 - 1);
        ushort packed = Vram.ReadPixel(wrappedOffset / 2, pixelY);
        return (byte)(
            (wrappedOffset & 1) == 0
                ? packed
                : packed >> 8);
    }

    private static uint Convert15BitToRgba(ushort pixel)
    {
        byte red = ExpandFiveBitChannel(pixel & 0x1F);
        byte green = ExpandFiveBitChannel((pixel >> 5) & 0x1F);
        byte blue = ExpandFiveBitChannel((pixel >> 10) & 0x1F);
        return PackRgba(red, green, blue);
    }

    private static byte ExpandFiveBitChannel(int channel)
    {
        return (byte)((channel << 3) | (channel >> 2));
    }

    private static uint PackRgba(byte red, byte green, byte blue)
    {
        return red |
               ((uint)green << 8) |
               ((uint)blue << 16) |
               0xFF00_0000;
    }
}
