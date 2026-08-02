using System.Text;
using SadPSX.Frontend.UI.Theming;
using SDL3;

namespace SadPSX.Frontend.UI.Rendering;

internal enum FontWeight
{
    Regular,
    Medium,
    SemiBold,
}

internal sealed class SdlTextRenderer : IDisposable
{
    private readonly nint _renderer;
    private readonly Dictionary<FontKey, nint> _fonts = [];
    private readonly Dictionary<TextKey, CachedText> _texts = [];
    private bool _disposed;

    public SdlTextRenderer(nint renderer)
    {
        _renderer = renderer;
        if (!TTF.Init())
        {
            throw new InvalidOperationException(
                $"Could not initialize SDL_ttf: {SDL.GetError()}");
        }
    }

    public (float Width, float Height) Measure(
        string text,
        int size,
        FontWeight weight = FontWeight.Regular)
    {
        CachedText cached = GetText(
            text,
            size,
            weight,
            new UiColor(255, 255, 255));
        return (cached.Width, cached.Height);
    }

    public void Draw(
        string text,
        float x,
        float y,
        int size,
        UiColor color,
        FontWeight weight = FontWeight.Regular,
        byte alpha = 255)
    {
        if (string.IsNullOrEmpty(text))
            return;

        CachedText cached = GetText(text, size, weight, color);
        SDL.SetTextureAlphaMod(cached.Texture, alpha);
        var destination = new SDL.FRect
        {
            X = x,
            Y = y,
            W = cached.Width,
            H = cached.Height,
        };
        if (!SDL.RenderTexture(
                _renderer,
                cached.Texture,
                nint.Zero,
                in destination))
        {
            throw new InvalidOperationException(
                $"Could not render text: {SDL.GetError()}");
        }
    }

    public void DrawCentered(
        string text,
        float centerX,
        float y,
        int size,
        UiColor color,
        FontWeight weight = FontWeight.Regular,
        byte alpha = 255)
    {
        (float width, _) = Measure(text, size, weight);
        Draw(text, centerX - (width / 2), y, size, color, weight, alpha);
    }

    public void DrawRightAligned(
        string text,
        float right,
        float y,
        int size,
        UiColor color,
        FontWeight weight = FontWeight.Regular,
        byte alpha = 255)
    {
        (float width, _) = Measure(text, size, weight);
        Draw(text, right - width, y, size, color, weight, alpha);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (CachedText text in _texts.Values)
            SDL.DestroyTexture(text.Texture);
        foreach (nint font in _fonts.Values)
            TTF.CloseFont(font);
        _texts.Clear();
        _fonts.Clear();
        TTF.Quit();
        _disposed = true;
    }

    private CachedText GetText(
        string text,
        int size,
        FontWeight weight,
        UiColor color)
    {
        var key = new TextKey(text, size, weight, color);
        if (_texts.TryGetValue(key, out CachedText cached))
            return cached;

        nint font = GetFont(size, weight);
        var sdlColor = new SDL.Color
        {
            R = color.Red,
            G = color.Green,
            B = color.Blue,
            A = 255,
        };
        nint surface = TTF.RenderTextBlended(
            font,
            text,
            (nuint)Encoding.UTF8.GetByteCount(text),
            sdlColor);
        if (surface == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Could not render font surface: {SDL.GetError()}");
        }

        nint texture = SDL.CreateTextureFromSurface(_renderer, surface);
        SDL.DestroySurface(surface);
        if (texture == nint.Zero ||
            !SDL.GetTextureSize(texture, out float width, out float height))
        {
            if (texture != nint.Zero)
                SDL.DestroyTexture(texture);
            throw new InvalidOperationException(
                $"Could not create text texture: {SDL.GetError()}");
        }

        SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
        cached = new CachedText(texture, width, height);
        _texts.Add(key, cached);
        return cached;
    }

    private nint GetFont(int size, FontWeight weight)
    {
        var key = new FontKey(size, weight);
        if (_fonts.TryGetValue(key, out nint font))
            return font;

        string path = weight switch
        {
            FontWeight.Medium => FrontendAssets.MediumFont,
            FontWeight.SemiBold => FrontendAssets.SemiBoldFont,
            _ => FrontendAssets.RegularFont,
        };
        font = TTF.OpenFont(path, size);
        if (font == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Could not open font '{path}': {SDL.GetError()}");
        }

        _fonts.Add(key, font);
        return font;
    }

    private readonly record struct FontKey(int Size, FontWeight Weight);

    private readonly record struct TextKey(
        string Text,
        int Size,
        FontWeight Weight,
        UiColor Color);

    private readonly record struct CachedText(
        nint Texture,
        float Width,
        float Height);
}
