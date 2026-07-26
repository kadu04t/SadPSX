using System.Globalization;
using System.Text.RegularExpressions;

namespace SadPSX.Core.CdRom.Media;

public sealed partial class CueDiscImage : DiscImage
{
    private readonly FileSegment[] _files;
    private readonly int _imageBaseSector;

    private CueDiscImage(
        FileSegment[] files,
        DiscTrack[] tracks,
        int imageBaseSector,
        int sectorCount)
    {
        _files = files;
        Tracks = tracks;
        _imageBaseSector = imageBaseSector;
        SectorCount = sectorCount;
    }

    public override int SectorCount { get; }
    public override DiscTrackMode TrackMode => Tracks[0].Mode;
    public override IReadOnlyList<DiscTrack> Tracks { get; }

    public new static CueDiscImage Open(string cuePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cuePath);
        string directory = Path.GetDirectoryName(Path.GetFullPath(cuePath))!;
        var fileOrder = new List<string>();
        var tracks = new List<TrackBuilder>();
        string? currentFile = null;
        TrackBuilder? currentTrack = null;

        foreach (string sourceLine in File.ReadLines(cuePath))
        {
            string line = sourceLine.Trim();
            Match fileMatch = FileLine().Match(line);
            if (fileMatch.Success)
            {
                currentFile = Path.GetFullPath(
                    Path.Combine(directory, fileMatch.Groups["path"].Value));
                if (!fileOrder.Contains(currentFile, StringComparer.OrdinalIgnoreCase))
                    fileOrder.Add(currentFile);
                currentTrack = null;
                continue;
            }

            Match trackMatch = TrackLine().Match(line);
            if (trackMatch.Success)
            {
                if (currentFile is null)
                    throw new InvalidDataException("TRACK encontrado antes de FILE.");

                currentTrack = new TrackBuilder(
                    byte.Parse(
                        trackMatch.Groups["number"].Value,
                        CultureInfo.InvariantCulture),
                    currentFile,
                    ParseMode(trackMatch.Groups["mode"].Value));
                tracks.Add(currentTrack);
                continue;
            }

            Match indexMatch = IndexLine().Match(line);
            if (currentTrack is not null && indexMatch.Success)
            {
                currentTrack.Index01 =
                    ParseFrames(indexMatch.Groups["time"].Value);
            }
        }

        if (tracks.Count == 0 || tracks.Any(track => track.Index01 is null))
            throw new InvalidDataException("O CUE não contém faixas completas.");

        var files = new List<FileSegment>();
        var fileBases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int nextFileBase = 0;
        try
        {
            foreach (string filePath in fileOrder)
            {
                var image = new BinDiscImage(filePath);
                fileBases.Add(filePath, nextFileBase);
                files.Add(new FileSegment(nextFileBase, image.SectorCount, image));
                nextFileBase += image.SectorCount;
            }

            int imageBaseSector = fileBases[tracks[0].FilePath] +
                tracks[0].Index01!.Value;
            DiscTrack[] discTracks = tracks
                .Select(track => new DiscTrack(
                    track.Number,
                    fileBases[track.FilePath] +
                    track.Index01!.Value -
                    imageBaseSector,
                    track.Mode))
                .ToArray();
            int sectorCount = nextFileBase - imageBaseSector;
            if (sectorCount <= 0 || discTracks.Any(track =>
                    track.StartLogicalBlockAddress < 0 ||
                    track.StartLogicalBlockAddress >= sectorCount))
            {
                throw new InvalidDataException(
                    "As faixas do CUE apontam para fora das imagens BIN.");
            }

            return new CueDiscImage(
                files.ToArray(),
                discTracks,
                imageBaseSector,
                sectorCount);
        }
        catch
        {
            foreach (FileSegment file in files)
                file.Image.Dispose();
            throw;
        }
    }

    public override void ReadSector(
        int logicalBlockAddress,
        Span<byte> destination)
    {
        if ((uint)logicalBlockAddress >= SectorCount)
            throw new ArgumentOutOfRangeException(nameof(logicalBlockAddress));

        int imageSector = logicalBlockAddress + _imageBaseSector;
        foreach (FileSegment file in _files)
        {
            if (imageSector < file.StartSector ||
                imageSector >= file.StartSector + file.SectorCount)
            {
                continue;
            }

            file.Image.ReadSector(imageSector - file.StartSector, destination);
            return;
        }

        throw new InvalidDataException("Setor não encontrado nas imagens do CUE.");
    }

    public override void Dispose()
    {
        foreach (FileSegment file in _files)
            file.Image.Dispose();
    }

    private static DiscTrackMode ParseMode(string value) =>
        value.ToUpperInvariant() switch
        {
            "AUDIO" => DiscTrackMode.Audio,
            "MODE1/2352" => DiscTrackMode.Mode1,
            "MODE2/2352" => DiscTrackMode.Mode2,
            _ => throw new InvalidDataException(
                $"Modo de faixa não suportado: {value}."),
        };

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

    [GeneratedRegex(
        "^TRACK\\s+(?<number>\\d+)\\s+(?<mode>AUDIO|MODE1/2352|MODE2/2352)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrackLine();

    [GeneratedRegex(
        "^INDEX\\s+01\\s+(?<time>\\d{2}:\\d{2}:\\d{2})$",
        RegexOptions.IgnoreCase)]
    private static partial Regex IndexLine();

    private sealed class TrackBuilder(
        byte number,
        string filePath,
        DiscTrackMode mode)
    {
        public byte Number { get; } = number;
        public string FilePath { get; } = filePath;
        public DiscTrackMode Mode { get; } = mode;
        public int? Index01 { get; set; }
    }

    private sealed record FileSegment(
        int StartSector,
        int SectorCount,
        BinDiscImage Image);
}
