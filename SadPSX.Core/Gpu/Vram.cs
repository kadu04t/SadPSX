namespace SadPSX.Core.Gpu;

public sealed class Vram
{
    public const int Width = 1024;
    public const int Height = 512;

    private readonly ushort[] _pixels = new ushort[Width * Height];

    public ReadOnlyMemory<ushort> Pixels => _pixels;

    public ushort ReadPixel(int pixelX, int pixelY)
    {
        return _pixels[GetIndex(pixelX, pixelY)];
    }

    public ushort[] CopyPixels()
    {
        return (ushort[])_pixels.Clone();
    }

    internal void WritePixel(int pixelX, int pixelY, ushort value)
    {
        _pixels[GetIndex(pixelX, pixelY)] = value;
    }

    private static int GetIndex(int pixelX, int pixelY)
    {
        int wrappedX = pixelX & (Width - 1);
        int wrappedY = pixelY & (Height - 1);
        return wrappedY * Width + wrappedX;
    }
}
