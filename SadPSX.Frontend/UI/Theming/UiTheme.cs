using SadPSX.Frontend.App;

namespace SadPSX.Frontend.UI.Theming;

internal sealed record UiTheme(
    UiColor Background,
    UiColor Surface,
    UiColor SurfaceElevated,
    UiColor Accent,
    UiColor TextPrimary,
    UiColor TextSecondary,
    UiColor Disabled,
    float SmallSpacing,
    float MediumSpacing,
    float LargeSpacing,
    float CornerRadius,
    TimeSpan QuickMotion,
    TimeSpan ScreenMotion)
{
    public static UiTheme Minimal { get; } = new(
        Background: new UiColor(9, 10, 14),
        Surface: new UiColor(25, 27, 34),
        SurfaceElevated: new UiColor(38, 41, 50),
        Accent: new UiColor(238, 82, 37),
        TextPrimary: new UiColor(246, 247, 249),
        TextSecondary: new UiColor(154, 158, 170),
        Disabled: new UiColor(67, 70, 80),
        SmallSpacing: 8,
        MediumSpacing: 16,
        LargeSpacing: 32,
        CornerRadius: 12,
        QuickMotion: TimeSpan.FromMilliseconds(160),
        ScreenMotion: TimeSpan.FromMilliseconds(320));

    public static UiTheme Sadcat { get; } = Minimal with
    {
        Accent = new UiColor(244, 78, 35),
    };

    public static UiTheme PlayStation { get; } = Minimal with
    {
        Background = new UiColor(5, 9, 20),
        Surface = new UiColor(17, 28, 52),
        SurfaceElevated = new UiColor(28, 48, 82),
        Accent = new UiColor(56, 125, 255),
        TextSecondary = new UiColor(166, 184, 216),
        Disabled = new UiColor(61, 75, 101),
    };

    public static UiTheme Terminal { get; } = Minimal with
    {
        Background = new UiColor(2, 9, 5),
        Surface = new UiColor(8, 24, 15),
        SurfaceElevated = new UiColor(14, 39, 23),
        Accent = new UiColor(76, 224, 128),
        TextPrimary = new UiColor(213, 255, 226),
        TextSecondary = new UiColor(123, 185, 143),
        Disabled = new UiColor(43, 83, 55),
    };

    public static UiTheme Get(FrontendThemeMode mode) => mode switch
    {
        FrontendThemeMode.PlayStation => PlayStation,
        FrontendThemeMode.Minimal => Minimal,
        FrontendThemeMode.Terminal => Terminal,
        _ => Sadcat,
    };
}
