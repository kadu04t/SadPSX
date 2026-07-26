using System.Globalization;
using System.Text.RegularExpressions;

namespace SadPSX.Core.CdRom.Media;

public static partial class CueDiscImage
{
    public static DiscImage Open(string cuePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cuePath);

        string? currentFile = null;
        string? dataFile = null;
        DiscTrackMode? trackMode = null;
        int firstSector = 0;
        bool insideDataTrack = false;

        foreach (string sourceLine in File.ReadLines(cuePath))
        {
            string line = sourceLine.Trim();
            Match fileMatch = FileLine().Match(line);
            if (fileMatch.Success)
            {
                currentFile = fileMatch.Groups["path"].Value;
                continue;
            }

            Match trackMatch = TrackLine().Match(line);
            if (trackMatch.Success)
            {
                string mode = trackMatch.Groups["mode"].Value;
                insideDataTrack =
                    mode.Equals("MODE1/2352", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("MODE2/2352", StringComparison.OrdinalIgnoreCase);

                if (insideDataTrack && dataFile is null)
                {
                    dataFile = currentFile;
                    trackMode = mode.StartsWith("MODE1", StringComparison.OrdinalIgnoreCase)
                        ? DiscTrackMode.Mode1
                        : DiscTrackMode.Mode2;
                }
                else if (dataFile is not null)
                {
                    insideDataTrack = false;
                }

                continue;
            }

            Match indexMatch = IndexLine().Match(line);
            if (insideDataTrack && indexMatch.Success)
            {
                firstSector = ParseFrames(indexMatch.Groups["time"].Value);
                insideDataTrack = false;
            }
        }

        if (dataFile is null || trackMode is null)
        {
            throw new InvalidDataException(
                "O CUE não contém uma faixa MODE1/2352 ou MODE2/2352.");
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(cuePath))!;
        return new BinDiscImage(
            Path.Combine(directory, dataFile),
            trackMode.Value,
            firstSector);
    }

    private static int ParseFrames(string value)
    {
        string[] parts = value.Split(':');
        if (parts.Length != 3)
            throw new InvalidDataException($"Tempo CUE inválido: {value}.");

        int minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int seconds = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int frames = int.Parse(parts[2], CultureInfo.InvariantCulture);
        if (seconds >= 60 || frames >= 75)
            throw new InvalidDataException($"Tempo CUE inválido: {value}.");

        return (minutes * 60 + seconds) * 75 + frames;
    }

    [GeneratedRegex("^FILE\\s+\"(?<path>.+)\"\\s+BINARY$", RegexOptions.IgnoreCase)]
    private static partial Regex FileLine();

    [GeneratedRegex("^TRACK\\s+\\d+\\s+(?<mode>\\S+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackLine();

    [GeneratedRegex("^INDEX\\s+01\\s+(?<time>\\d{2}:\\d{2}:\\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex IndexLine();
}
