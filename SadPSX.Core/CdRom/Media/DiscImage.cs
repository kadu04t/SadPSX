namespace SadPSX.Core.CdRom.Media;

public abstract class DiscImage : IDisposable
{
    public const int RawSectorSize = 2352;

    public abstract int SectorCount { get; }
    public abstract DiscTrackMode TrackMode { get; }
    public virtual IReadOnlyList<DiscTrack> Tracks =>
        [new DiscTrack(1, 0, TrackMode)];

    public abstract void ReadSector(int logicalBlockAddress, Span<byte> destination);

    public void ReadUserDataSector(
        int logicalBlockAddress,
        Span<byte> destination)
    {
        if (destination.Length < 2048)
        {
            throw new ArgumentException(
                "O destino deve comportar 2048 bytes.",
                nameof(destination));
        }

        var rawSector = new byte[RawSectorSize];
        ReadSector(logicalBlockAddress, rawSector);
        int offset = GetTrackAt(logicalBlockAddress).Mode switch
        {
            DiscTrackMode.Mode1 => 16,
            DiscTrackMode.Mode2 => 24,
            _ => throw new InvalidDataException(
                "Faixas de áudio não contêm setores ISO9660."),
        };
        rawSector.AsSpan(offset, 2048).CopyTo(destination);
    }

    public bool TryGetBootInfo(out DiscBootInfo? bootInfo) =>
        Iso9660DiscReader.TryGetBootInfo(this, out bootInfo);

    public DiscTrack GetTrack(byte number)
    {
        foreach (DiscTrack track in Tracks)
        {
            if (track.Number == number)
                return track;
        }

        throw new ArgumentOutOfRangeException(nameof(number));
    }

    public DiscTrack GetTrackAt(int logicalBlockAddress)
    {
        if ((uint)logicalBlockAddress >= SectorCount)
            throw new ArgumentOutOfRangeException(nameof(logicalBlockAddress));

        DiscTrack current = Tracks[0];
        foreach (DiscTrack track in Tracks)
        {
            if (track.StartLogicalBlockAddress > logicalBlockAddress)
                break;
            current = track;
        }

        return current;
    }

    public static DiscImage Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        return Path.GetExtension(fullPath).Equals(".cue", StringComparison.OrdinalIgnoreCase)
            ? CueDiscImage.Open(fullPath)
            : new BinDiscImage(fullPath);
    }

    public abstract void Dispose();
}
