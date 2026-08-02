using System.Text.RegularExpressions;
using SadPSX.Core.CdRom.Media;

namespace SadPSX.Frontend.Library;

internal sealed partial class GameIdentityService
{
    public GameLibraryEntry Identify(string displayName, string discPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(discPath);
        string fullPath = Path.GetFullPath(discPath);
        int? discNumber = ParseDiscNumber(displayName);

        try
        {
            using DiscImage disc = DiscImage.Open(fullPath);
            if (!disc.TryGetBootInfo(out DiscBootInfo? bootInfo) ||
                bootInfo is null)
            {
                return new GameLibraryEntry(
                    displayName,
                    fullPath,
                    DiscNumber: discNumber);
            }

            string? serial = ParseSerial(bootInfo.ExecutablePath);
            return new GameLibraryEntry(
                displayName,
                fullPath,
                serial,
                GetRegion(serial),
                discNumber,
                bootInfo.ExecutablePath);
        }
        catch (IOException)
        {
            return new GameLibraryEntry(
                displayName,
                fullPath,
                DiscNumber: discNumber);
        }
        catch (InvalidDataException)
        {
            return new GameLibraryEntry(
                displayName,
                fullPath,
                DiscNumber: discNumber);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new GameLibraryEntry(
                displayName,
                fullPath,
                DiscNumber: discNumber);
        }
    }

    internal static string? ParseSerial(string executablePath)
    {
        Match match = SerialPattern().Match(executablePath);
        if (!match.Success)
            return null;

        return $"{match.Groups[1].Value.ToUpperInvariant()}-" +
               $"{match.Groups[2].Value}{match.Groups[3].Value}";
    }

    internal static string GetRegion(string? serial)
    {
        if (serial is null || serial.Length < 4)
            return "Unknown";

        return serial[..4].ToUpperInvariant() switch
        {
            "SCUS" or "SLUS" => "USA",
            "SCES" or "SLES" => "Europe",
            "SCPS" or "SLPS" or "SLPM" or "SCPM" => "Japan",
            _ => "Unknown",
        };
    }

    private static int? ParseDiscNumber(string displayName)
    {
        Match match = DiscPattern().Match(displayName);
        return match.Success && int.TryParse(match.Groups[1].Value, out int disc)
            ? disc
            : null;
    }

    [GeneratedRegex(
        @"(?i)([A-Z]{4})[_-](\d{3})[.](\d{2})",
        RegexOptions.CultureInvariant)]
    private static partial Regex SerialPattern();

    [GeneratedRegex(
        @"(?i)\bdisc\s*(\d+)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DiscPattern();
}
