using System.Runtime.InteropServices;
using SadPSX.Core.Gpu;
using SDL3;

namespace SadPSX.Frontend.Video;

internal sealed class SdlVideoOutput : IDisposable
{
    private readonly nint _window;
    private readonly nint _renderer;

    private nint _texture;
    private uint[] _pixels = [];
    private int _textureWidth;
    private int _textureHeight;
    private bool _disposed;

    public SdlVideoOutput(string title, int width, int height)
    {
        if (!SDL.CreateWindowAndRenderer(
                title,
                width,
                height,
                SDL.WindowFlags.Resizable,
                out _window,
                out _renderer))
        {
            throw new InvalidOperationException(
                $"Não foi possível criar a janela SDL3: {SDL.GetError()}");
        }

        SDL.SetRenderVSync(_renderer, 0);
    }

    public void Present(Gpu gpu)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        GpuDisplayInfo display = gpu.GetDisplayInfo();
        EnsureTexture(display.Width, display.Height);
        gpu.CopyDisplayRgba(_pixels);

        Span<byte> bytes = MemoryMarshal.AsBytes(_pixels.AsSpan());
        if (!SDL.UpdateTexture(
                _texture,
                nint.Zero,
                bytes,
                display.Width * sizeof(uint)))
        {
            throw new InvalidOperationException(
                $"Não foi possível atualizar a textura SDL3: {SDL.GetError()}");
        }

        EnsureSuccess(
            SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 255),
            "configurar a cor de fundo");
        EnsureSuccess(SDL.RenderClear(_renderer), "limpar o frame");

        if (!SDL.RenderTexture(
                _renderer,
                _texture,
                nint.Zero,
                nint.Zero))
        {
            throw new InvalidOperationException(
                $"Não foi possível apresentar a textura SDL3: {SDL.GetError()}");
        }

        EnsureSuccess(SDL.RenderPresent(_renderer), "apresentar o frame");
    }

    public void SetTitle(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(
            SDL.SetWindowTitle(_window, title),
            "atualizar o título da janela");
    }

    public void ToggleFullscreen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool fullscreen =
            (SDL.GetWindowFlags(_window) & SDL.WindowFlags.Fullscreen) != 0;

        if (!SDL.SetWindowFullscreen(_window, !fullscreen))
        {
            Console.Error.WriteLine(
                $"Não foi possível alternar tela cheia: {SDL.GetError()}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_texture != nint.Zero)
            SDL.DestroyTexture(_texture);

        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        _disposed = true;
    }

    private void EnsureTexture(int width, int height)
    {
        if (_texture != nint.Zero &&
            _textureWidth == width &&
            _textureHeight == height)
        {
            return;
        }

        if (_texture != nint.Zero)
        {
            SDL.DestroyTexture(_texture);
            _texture = nint.Zero;
        }

        _texture = SDL.CreateTexture(
            _renderer,
            SDL.PixelFormat.ABGR8888,
            SDL.TextureAccess.Streaming,
            width,
            height);
        if (_texture == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Não foi possível criar a textura SDL3: {SDL.GetError()}");
        }

        EnsureSuccess(
            SDL.SetTextureScaleMode(_texture, SDL.ScaleMode.Nearest),
            "configurar o filtro da textura");
        EnsureSuccess(
            SDL.SetRenderLogicalPresentation(
                _renderer,
                width,
                height,
                SDL.RendererLogicalPresentation.Letterbox),
            "configurar a apresentação do frame");

        _textureWidth = width;
        _textureHeight = height;
        _pixels = new uint[width * height];
    }

    private static void EnsureSuccess(bool succeeded, string operation)
    {
        if (!succeeded)
        {
            throw new InvalidOperationException(
                $"Não foi possível {operation} no SDL3: {SDL.GetError()}");
        }
    }
}
