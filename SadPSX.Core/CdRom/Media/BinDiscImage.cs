namespace SadPSX.Core.CdRom.Media;

public sealed class BinDiscImage : DiscImage
{
    private readonly FileStream _stream;
    private readonly int _firstSector;

    public BinDiscImage(
        string path,
        DiscTrackMode trackMode = DiscTrackMode.Mode2,
        int firstSector = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (firstSector < 0)
            throw new ArgumentOutOfRangeException(nameof(firstSector));

        _stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (_stream.Length % RawSectorSize != 0)
        {
            _stream.Dispose();
            throw new InvalidDataException(
                $"A imagem BIN deve conter setores de {RawSectorSize} bytes.");
        }

        int totalSectors = checked((int)(_stream.Length / RawSectorSize));
        if (firstSector > totalSectors)
        {
            _stream.Dispose();
            throw new InvalidDataException(
                "O INDEX 01 aponta para fora da imagem BIN.");
        }

        _firstSector = firstSector;
        SectorCount = totalSectors - firstSector;
        TrackMode = trackMode;
    }

    public override int SectorCount { get; }
    public override DiscTrackMode TrackMode { get; }

    public override void ReadSector(
        int logicalBlockAddress,
        Span<byte> destination)
    {
        if ((uint)logicalBlockAddress >= SectorCount)
            throw new ArgumentOutOfRangeException(nameof(logicalBlockAddress));
        if (destination.Length < RawSectorSize)
        {
            throw new ArgumentException(
                $"O destino deve comportar {RawSectorSize} bytes.",
                nameof(destination));
        }

        long offset = (long)(_firstSector + logicalBlockAddress) * RawSectorSize;
        _stream.Position = offset;
        _stream.ReadExactly(destination[..RawSectorSize]);
    }

    public override void Dispose() => _stream.Dispose();
}
