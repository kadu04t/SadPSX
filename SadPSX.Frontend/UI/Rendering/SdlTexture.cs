using SDL3;

namespace SadPSX.Frontend.UI.Rendering;

internal sealed class SdlTexture : IDisposable
{
    public SdlTexture(nint renderer, string path)
    {
        Handle = Image.LoadTexture(renderer, path);
        if (Handle == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Could not load texture '{path}': {SDL.GetError()}");
        }

        if (!SDL.GetTextureSize(Handle, out float width, out float height))
        {
            SDL.DestroyTexture(Handle);
            throw new InvalidOperationException(
                $"Could not query texture '{path}': {SDL.GetError()}");
        }

        Width = width;
        Height = height;
        SDL.SetTextureBlendMode(Handle, SDL.BlendMode.Blend);
    }

    public nint Handle { get; }

    public float Width { get; }

    public float Height { get; }

    public void Render(nint renderer, in SDL.FRect destination, byte alpha = 255)
    {
        SDL.SetTextureAlphaMod(Handle, alpha);
        if (!SDL.RenderTexture(renderer, Handle, nint.Zero, in destination))
        {
            throw new InvalidOperationException(
                $"Could not render texture: {SDL.GetError()}");
        }
    }

    public void RenderCover(
        nint renderer,
        in SDL.FRect destination,
        byte alpha = 255)
    {
        float sourceAspect = Width / Height;
        float destinationAspect = destination.W / destination.H;
        var source = new SDL.FRect { X = 0, Y = 0, W = Width, H = Height };
        if (sourceAspect > destinationAspect)
        {
            source.W = Height * destinationAspect;
            source.X = (Width - source.W) / 2;
        }
        else
        {
            source.H = Width / destinationAspect;
            source.Y = (Height - source.H) / 2;
        }

        SDL.SetTextureAlphaMod(Handle, alpha);
        if (!SDL.RenderTexture(renderer, Handle, in source, in destination))
        {
            throw new InvalidOperationException(
                $"Could not render cropped texture: {SDL.GetError()}");
        }
    }

    public void Dispose()
    {
        SDL.DestroyTexture(Handle);
    }
}
