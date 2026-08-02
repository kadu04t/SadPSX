namespace SadPSX.Frontend.UI.Rendering;

internal static class FrontendAssets
{
    public static string SadcatOpen => GetPath("Brand", "sadcat-open.png");

    public static string SadcatClosed => GetPath("Brand", "sadcat-closed.png");

    public static string DefaultBackground =>
        GetPath("Backgrounds", "default.png");

    public static string CoverPlaceholder =>
        GetPath("Covers", "placeholder.png");

    public static string RegularFont => GetPath("Fonts", "Geist-Regular.ttf");

    public static string MediumFont => GetPath("Fonts", "Geist-Medium.ttf");

    public static string SemiBoldFont =>
        GetPath("Fonts", "Geist-SemiBold.ttf");

    public static string StartupSound => GetPath("Audio", "startup.wav");

    public static string NavigateSound => GetPath("Audio", "navigate.wav");

    public static string ConfirmSound => GetPath("Audio", "confirm.wav");

    public static string BackSound => GetPath("Audio", "back.wav");

    private static string GetPath(params string[] segments)
    {
        string[] pathSegments = [AppContext.BaseDirectory, "Assets", ..segments];
        return Path.Combine(pathSegments);
    }
}
