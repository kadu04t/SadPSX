using System.Runtime.InteropServices;
using SadPSX.Core.Gpu;
using SadPSX.Frontend.App;
using SadPSX.Frontend.UI.Hosting;
using SDL3;

namespace SadPSX.Frontend.Video;

internal sealed class SdlVideoOutput : IDisposable
{
    private readonly SdlFrontendHost _host;
    private readonly nint _renderer;
    private readonly VideoFrameBuffer _frameBuffer = new();
    private readonly VideoScalingMode _scalingMode;
    private readonly SDL.ScaleMode _textureScaleMode;

    private nint _texture;
    private int _textureWidth;
    private int _textureHeight;
    private bool _disposed;

    public SdlVideoOutput(
        SdlFrontendHost host,
        VideoScalingMode scalingMode,
        bool smoothVideo)
    {
        _host = host;
        _renderer = host.Renderer;
        _scalingMode = scalingMode;
        _textureScaleMode = smoothVideo
            ? SDL.ScaleMode.Linear
            : SDL.ScaleMode.Nearest;
        SDL.SetRenderVSync(_renderer, 0);
    }

    public VideoPresentationMetrics Metrics => _frameBuffer.GetMetrics();

    public void Capture(Gpu gpu)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _frameBuffer.Capture(gpu);
    }

    public bool PresentPending()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_frameBuffer.HasFrame)
            return false;

        GpuDisplayInfo display = _frameBuffer.Display;
        EnsureTexture(display.Width, display.Height);
        Span<byte> bytes = MemoryMarshal.AsBytes(_frameBuffer.Pixels);
        if (!SDL.UpdateTexture(
                _texture,
                nint.Zero,
                bytes,
                display.Width * sizeof(uint)))
        {
            throw new InvalidOperationException(
                $"Could not update video texture: {SDL.GetError()}");
        }

        EnsureSuccess(
            SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 255),
            "set video background");
        EnsureSuccess(SDL.RenderClear(_renderer), "clear video frame");
        if (!SDL.RenderTexture(
                _renderer,
                _texture,
                nint.Zero,
                nint.Zero))
        {
            throw new InvalidOperationException(
                $"Could not present video texture: {SDL.GetError()}");
        }

        EnsureSuccess(SDL.RenderPresent(_renderer), "present video frame");
        _frameBuffer.MarkPresented();
        return true;
    }

    public void SetTitle(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _host.SetTitle(title);
    }

    public bool ToggleFullscreen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_host.ToggleFullscreen())
        {
            Console.Error.WriteLine(
                $"Could not toggle fullscreen: {SDL.GetError()}");
        }

        return _host.IsFullscreen;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_texture != nint.Zero)
            SDL.DestroyTexture(_texture);
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
                $"Could not create video texture: {SDL.GetError()}");
        }

        EnsureSuccess(
            SDL.SetTextureScaleMode(_texture, _textureScaleMode),
            "set video scaling");
        _host.ConfigurePresentation(
            width,
            height,
            GetPresentation(_scalingMode));
        _textureWidth = width;
        _textureHeight = height;
    }

    internal static SDL.RendererLogicalPresentation GetPresentation(
        VideoScalingMode scalingMode) => scalingMode switch
    {
        VideoScalingMode.Stretch => SDL.RendererLogicalPresentation.Stretch,
        VideoScalingMode.IntegerScale =>
            SDL.RendererLogicalPresentation.IntegerScale,
        _ => SDL.RendererLogicalPresentation.Letterbox,
    };

    private static void EnsureSuccess(bool succeeded, string operation)
    {
        if (!succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {operation} with SDL3: {SDL.GetError()}");
        }
    }
}
