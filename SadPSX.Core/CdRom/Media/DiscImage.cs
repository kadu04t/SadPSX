namespace SadPSX.Core.CdRom.Media;

public abstract class DiscImage : IDisposable
{
    public const int RawSectorSize = 2352;

    public abstract int SectorCount { get; }
    public abstract DiscTrackMode TrackMode { get; }

    public abstract void ReadSector(int logicalBlockAddress, Span<byte> destination);

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
