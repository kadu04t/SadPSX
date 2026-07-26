using SadPSX.Core.CdRom.Media;
using Xunit;

namespace SadPSX.Tests.CdRom;

public sealed class DiscImageTests
{
    [Fact]
    public void BinImageReadsRawSectors()
    {
        string directory = CreateTemporaryDirectory();
        string binPath = Path.Combine(directory, "game.bin");
        byte[] image = new byte[DiscImage.RawSectorSize * 2];
        image[DiscImage.RawSectorSize + 24] = 0x5A;
        File.WriteAllBytes(binPath, image);

        try
        {
            using var disc = new BinDiscImage(binPath);
            byte[] sector = new byte[DiscImage.RawSectorSize];

            disc.ReadSector(1, sector);

            Assert.Equal(2, disc.SectorCount);
            Assert.Equal(DiscTrackMode.Mode2, disc.TrackMode);
            Assert.Equal(0x5A, sector[24]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CueImageResolvesBinAndIndex()
    {
        string directory = CreateTemporaryDirectory();
        string binPath = Path.Combine(directory, "disc image.bin");
        string cuePath = Path.Combine(directory, "disc.cue");
        byte[] image = new byte[DiscImage.RawSectorSize * 2];
        image[DiscImage.RawSectorSize + 16] = 0xA5;
        File.WriteAllBytes(binPath, image);
        File.WriteAllText(
            cuePath,
            """
            FILE "disc image.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:01
            """);

        try
        {
            using DiscImage disc = DiscImage.Open(cuePath);
            byte[] sector = new byte[DiscImage.RawSectorSize];

            disc.ReadSector(0, sector);

            Assert.Equal(1, disc.SectorCount);
            Assert.Equal(DiscTrackMode.Mode1, disc.TrackMode);
            Assert.Equal(0xA5, sector[16]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"SadPSX-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
