namespace SadPSX.Frontend.UI.Theming;

internal readonly record struct UiColor(
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha = byte.MaxValue)
{
    public UiColor WithAlpha(byte alpha) => this with { Alpha = alpha };

    public static UiColor Lerp(UiColor start, UiColor end, float amount)
    {
        float clamped = Math.Clamp(amount, 0f, 1f);
        return new UiColor(
            LerpChannel(start.Red, end.Red, clamped),
            LerpChannel(start.Green, end.Green, clamped),
            LerpChannel(start.Blue, end.Blue, clamped),
            LerpChannel(start.Alpha, end.Alpha, clamped));
    }

    private static byte LerpChannel(byte start, byte end, float amount) =>
        (byte)MathF.Round(start + ((end - start) * amount));
}
