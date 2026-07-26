using System.Runtime.InteropServices;
using SadPSX.Core.Spu;
using SDL3;

namespace SadPSX.Frontend.Audio;

internal sealed class SdlAudioOutput : IDisposable
{
    private const int BufferFrames = 2_048;
    private const int MaximumQueuedBytes = Spu.SampleRate / 10 * 4;

    private readonly short[] _samples = new short[BufferFrames * 2];
    private nint _stream;
    private bool _disposed;

    public SdlAudioOutput()
    {
        var specification = new SDL.AudioSpec
        {
            Format = SDL.AudioFormat.AudioS16LE,
            Channels = 2,
            Freq = Spu.SampleRate,
        };

        _stream = SDL.OpenAudioDeviceStream(
            SDL.AudioDeviceDefaultPlayback,
            in specification,
            null!,
            nint.Zero);
        if (_stream == nint.Zero)
        {
            Console.Error.WriteLine(
                $"Áudio SDL3 indisponível: {SDL.GetError()}");
            return;
        }

        if (!SDL.ResumeAudioStreamDevice(_stream))
        {
            Console.Error.WriteLine(
                $"Não foi possível iniciar o áudio SDL3: {SDL.GetError()}");
            SDL.DestroyAudioStream(_stream);
            _stream = nint.Zero;
        }
    }

    public bool IsAvailable => _stream != nint.Zero;

    public void Pump(Spu spu)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream == nint.Zero ||
            SDL.GetAudioStreamQueued(_stream) >= MaximumQueuedBytes)
        {
            return;
        }

        int frames = spu.DrainSamples(_samples);
        if (frames == 0)
            return;

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            _samples.AsSpan(0, frames * 2));
        if (!SDL.PutAudioStreamData(_stream, bytes, bytes.Length))
        {
            Console.Error.WriteLine(
                $"Não foi possível enviar áudio ao SDL3: {SDL.GetError()}");
        }
    }

    public void SetPaused(bool paused)
    {
        if (_stream == nint.Zero)
            return;

        bool succeeded = paused
            ? SDL.PauseAudioStreamDevice(_stream)
            : SDL.ResumeAudioStreamDevice(_stream);
        if (!succeeded)
        {
            Console.Error.WriteLine(
                $"Não foi possível alterar o estado do áudio: {SDL.GetError()}");
        }
    }

    public void Clear()
    {
        if (_stream != nint.Zero)
            SDL.ClearAudioStream(_stream);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_stream != nint.Zero)
            SDL.DestroyAudioStream(_stream);
        _stream = nint.Zero;
        _disposed = true;
    }
}
