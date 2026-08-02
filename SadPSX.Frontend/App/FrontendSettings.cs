using System.Text.Json;
using System.Text.Json.Serialization;
using SadPSX.Frontend.Input;

namespace SadPSX.Frontend.App;

internal sealed record FrontendSettings(
    string? BiosPath = null,
    string? LastDiscPath = null,
    string? LibraryPath = null,
    bool Fullscreen = true,
    bool ShowBootAnimation = true,
    bool DownloadCovers = true,
    bool UiSounds = true,
    VideoScalingMode VideoScaling = VideoScalingMode.AspectRatio,
    bool SmoothVideo = false,
    bool AudioEnabled = true,
    int AudioVolume = 100,
    bool DefaultAnalogController = true,
    GamepadMapping? ControllerMapping = null,
    FrontendThemeMode Theme = FrontendThemeMode.Sadcat,
    FrontendWallpaperMode Wallpaper = FrontendWallpaperMode.GameArtwork,
    string? CustomWallpaperPath = null,
    bool WallpaperParallax = true,
    bool CheckForUpdates = true)
{
    [JsonIgnore]
    public GamepadMapping EffectiveControllerMapping =>
        ControllerMapping ?? GamepadMapping.Default;
}

internal enum VideoScalingMode
{
    AspectRatio,
    Stretch,
    IntegerScale,
}

internal sealed class FrontendSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public FrontendSettingsStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
    }

    public FrontendSettings Load()
    {
        if (!File.Exists(_path))
            return new FrontendSettings();

        try
        {
            string json = File.ReadAllText(_path);
            FrontendSettings settings =
                JsonSerializer.Deserialize<FrontendSettings>(
                    json,
                    SerializerOptions) ?? new FrontendSettings();
            return settings with
            {
                AudioVolume = Math.Clamp(settings.AudioVolume, 0, 100),
                VideoScaling = Enum.IsDefined(settings.VideoScaling)
                    ? settings.VideoScaling
                    : VideoScalingMode.AspectRatio,
                Theme = Enum.IsDefined(settings.Theme)
                    ? settings.Theme
                    : FrontendThemeMode.Sadcat,
                Wallpaper = Enum.IsDefined(settings.Wallpaper)
                    ? settings.Wallpaper
                    : FrontendWallpaperMode.GameArtwork,
                ControllerMapping = settings.ControllerMapping?.Normalize(),
            };
        }
        catch (IOException)
        {
            return new FrontendSettings();
        }
        catch (JsonException)
        {
            return new FrontendSettings();
        }
    }

    public void Save(FrontendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(settings, SerializerOptions));
    }

    private static string GetDefaultPath()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "SadPSX", "settings.json");
    }
}

internal enum FrontendThemeMode
{
    Sadcat,
    PlayStation,
    Minimal,
    Terminal,
}

internal enum FrontendWallpaperMode
{
    GameArtwork,
    Sadcat,
    Custom,
    Solid,
}
