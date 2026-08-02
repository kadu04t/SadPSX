using SDL3;

namespace SadPSX.Frontend.UI.Hosting;

internal sealed class SdlFrontendHost : IDisposable
{
    private bool _disposed;

    public SdlFrontendHost(bool fullscreen)
    {
        if (!SDL.CreateWindowAndRenderer(
                "SadPSX",
                1280,
                720,
                SDL.WindowFlags.HighPixelDensity | SDL.WindowFlags.Resizable,
                out nint window,
                out nint renderer))
        {
            throw new InvalidOperationException(
                $"Could not create SadPSX window: {SDL.GetError()}");
        }

        Window = window;
        Renderer = renderer;
        SDL.SetRenderDrawBlendMode(Renderer, SDL.BlendMode.Blend);
        SDL.SetRenderVSync(Renderer, 1);
        SetFullscreen(fullscreen);
    }

    public nint Window { get; }

    public nint Renderer { get; }

    public bool IsFullscreen =>
        (SDL.GetWindowFlags(Window) & SDL.WindowFlags.Fullscreen) != 0;

    public void ConfigurePresentation(
        int width,
        int height,
        SDL.RendererLogicalPresentation presentation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SDL.SetRenderLogicalPresentation(
                Renderer,
                width,
                height,
                presentation))
        {
            throw new InvalidOperationException(
                $"Could not configure presentation: {SDL.GetError()}");
        }
    }

    public void SetTitle(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SDL.SetWindowTitle(Window, title))
        {
            throw new InvalidOperationException(
                $"Could not update window title: {SDL.GetError()}");
        }
    }

    public bool SetFullscreen(bool fullscreen)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return SDL.SetWindowFullscreen(Window, fullscreen);
    }

    public bool ToggleFullscreen() => SetFullscreen(!IsFullscreen);

    public void Dispose()
    {
        if (_disposed)
            return;

        SDL.DestroyRenderer(Renderer);
        SDL.DestroyWindow(Window);
        _disposed = true;
    }
}
