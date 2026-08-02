using System.Diagnostics;
using SadPSX.Frontend.UI.Audio;
using SadPSX.Frontend.UI.Rendering;
using SadPSX.Frontend.UI.Theming;
using SDL3;

namespace SadPSX.Frontend.Launcher;

internal sealed class SdlSplashScreen : IDisposable
{
    private const int WindowSize = 520;
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1.45);

    private readonly nint _window;
    private readonly nint _renderer;
    private readonly SdlTexture _logo;
    private readonly SdlTextRenderer _text;
    private readonly SdlUiAudio _audio;
    private bool _disposed;

    public SdlSplashScreen()
    {
        SDL.WindowFlags flags =
            SDL.WindowFlags.Transparent |
            SDL.WindowFlags.Borderless |
            SDL.WindowFlags.AlwaysOnTop |
            SDL.WindowFlags.Utility;
        if (!SDL.CreateWindowAndRenderer(
                "SadPSX",
                WindowSize,
                WindowSize,
                flags,
                out _window,
                out _renderer))
        {
            throw new InvalidOperationException(
                $"Could not create splash screen: {SDL.GetError()}");
        }

        SDL.SetWindowPosition(
            _window,
            unchecked((int)SDL.WindowPosCentered()),
            unchecked((int)SDL.WindowPosCentered()));
        SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);
        SDL.SetRenderVSync(_renderer, 1);
        _logo = new SdlTexture(_renderer, FrontendAssets.SadcatOpen);
        _text = new SdlTextRenderer(_renderer);
        _audio = new SdlUiAudio();
    }

    public void Run()
    {
        var clock = Stopwatch.StartNew();
        _audio.Play(UiSound.Startup);
        bool running = true;
        while (running && clock.Elapsed < Duration)
        {
            while (SDL.PollEvent(out SDL.Event currentEvent))
            {
                if ((SDL.EventType)currentEvent.Type is
                    SDL.EventType.Quit or
                    SDL.EventType.WindowCloseRequested)
                {
                    running = false;
                }
            }

            Render(clock.Elapsed.TotalSeconds);
            SDL.Delay(1);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _audio.Dispose();
        _text.Dispose();
        _logo.Dispose();
        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        _disposed = true;
    }

    private void Render(double elapsedSeconds)
    {
        double fadeIn = Math.Clamp(elapsedSeconds / 0.42, 0, 1);
        double fadeOut = Math.Clamp(
            (Duration.TotalSeconds - elapsedSeconds) / 0.28,
            0,
            1);
        byte alpha = (byte)(255 * Math.Min(fadeIn, fadeOut));
        float floatOffset = MathF.Sin((float)(elapsedSeconds * 2.6)) * 5;

        SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 0);
        SDL.RenderClear(_renderer);
        var destination = new SDL.FRect
        {
            X = 82,
            Y = 35 + floatOffset,
            W = 356,
            H = 356,
        };
        _logo.Render(_renderer, in destination, alpha);
        _text.DrawCentered(
            "SadPSX",
            WindowSize / 2f,
            397 + floatOffset,
            42,
            new UiColor(245, 245, 247),
            FontWeight.SemiBold,
            alpha);
        _text.DrawCentered(
            "A PLAYSTATION EMULATOR",
            WindowSize / 2f,
            448 + floatOffset,
            13,
            new UiColor(166, 168, 176),
            FontWeight.Medium,
            alpha);
        SDL.RenderPresent(_renderer);
    }
}
