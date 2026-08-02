using SadPSX.Frontend.UI.Rendering;
using SDL3;

namespace SadPSX.Frontend.UI.Audio;

internal enum UiSound
{
    Startup,
    Navigate,
    Confirm,
    Back,
}

internal sealed class SdlUiAudio : IDisposable
{
    private readonly Dictionary<UiSound, nint> _sounds = [];
    private nint _mixer;
    private bool _initialized;

    public SdlUiAudio()
    {
        try
        {
            _initialized = Mixer.Init();
            if (!_initialized)
                return;

            _mixer = Mixer.CreateMixerDevice(
                SDL.AudioDeviceDefaultPlayback,
                nint.Zero);
            if (_mixer == nint.Zero)
                return;

            Load(UiSound.Startup, FrontendAssets.StartupSound);
            Load(UiSound.Navigate, FrontendAssets.NavigateSound);
            Load(UiSound.Confirm, FrontendAssets.ConfirmSound);
            Load(UiSound.Back, FrontendAssets.BackSound);
        }
        catch (DllNotFoundException)
        {
            _initialized = false;
        }
    }

    public void Play(UiSound sound)
    {
        if (_mixer != nint.Zero && _sounds.TryGetValue(sound, out nint audio))
            Mixer.PlayAudio(_mixer, audio);
    }

    public void Dispose()
    {
        foreach (nint audio in _sounds.Values)
            Mixer.DestroyAudio(audio);
        _sounds.Clear();
        if (_mixer != nint.Zero)
            Mixer.DestroyMixer(_mixer);
        if (_initialized)
            Mixer.Quit();
    }

    private void Load(UiSound sound, string path)
    {
        nint audio = Mixer.LoadAudio(_mixer, path, predecode: true);
        if (audio != nint.Zero)
            _sounds.Add(sound, audio);
    }
}
