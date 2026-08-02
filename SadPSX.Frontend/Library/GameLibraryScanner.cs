using System.Globalization;

namespace SadPSX.Frontend.Library;

internal sealed class GameLibraryScanner
{
    private readonly GameIdentityService _identity = new();

    public IReadOnlyList<GameLibraryEntry> Scan(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        try
        {
            string[] imageFiles = Directory
                .EnumerateFiles(
                    directory,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(IsDiscImage)
                .ToArray();
            string[] cueFiles = imageFiles
                .Where(IsCue)
                .ToArray();
            var referencedBins = cueFiles
                .SelectMany(ReadReferencedBins)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var directoriesWithCue = cueFiles
                .Select(Path.GetDirectoryName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> unreferencedBins = imageFiles
                .Where(path => !IsCue(path) && !referencedBins.Contains(path))
                .GroupBy(
                    Path.GetDirectoryName,
                    StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => directoriesWithCue.Contains(group.Key)
                    ? group
                    : group.OrderByDescending(
                        path => new FileInfo(path).Length).Take(1));
            IEnumerable<string> candidates = cueFiles.Concat(unreferencedBins);

            return candidates
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => _identity.Identify(
                    FormatName(Path.GetFileNameWithoutExtension(path)),
                    path))
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> ReadReferencedBins(string cuePath)
    {
        string? directory = Path.GetDirectoryName(cuePath);
        foreach (string line in File.ReadLines(cuePath))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
                continue;

            int firstQuote = trimmed.IndexOf('"');
            int lastQuote = trimmed.LastIndexOf('"');
            if (firstQuote < 0 || lastQuote <= firstQuote)
                continue;

            string relativePath = trimmed[(firstQuote + 1)..lastQuote];
            yield return Path.GetFullPath(
                Path.Combine(directory ?? string.Empty, relativePath));
        }
    }

    private static bool IsDiscImage(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCue(string path) =>
        Path.GetExtension(path).Equals(
            ".cue",
            StringComparison.OrdinalIgnoreCase);

    private static string FormatName(string fileName)
    {
        string readable = fileName
            .Replace('_', ' ')
            .Replace('.', ' ')
            .Replace('-', ' ');
        string title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            readable.ToLowerInvariant());
        string[] numerals = ["Viii", "Vii", "Iii", "Ii", "Iv", "Vi", "Ix"];
        foreach (string numeral in numerals)
        {
            title = title.Replace(
                numeral,
                numeral.ToUpperInvariant(),
                StringComparison.Ordinal);
        }

        return title
            .Replace("(Usa)", "(USA)", StringComparison.Ordinal)
            .Replace("(Uk)", "(UK)", StringComparison.Ordinal)
            .Replace("(Br)", "(Brazil)", StringComparison.Ordinal);
    }
}
