using System.Buffers.Binary;
using System.Text;
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

    [Fact]
    public void CueImageExposesMultipleTracksAndLeadOut()
    {
        string directory = CreateTemporaryDirectory();
        string binPath = Path.Combine(directory, "mixed.bin");
        string cuePath = Path.Combine(directory, "mixed.cue");
        File.WriteAllBytes(
            binPath,
            new byte[DiscImage.RawSectorSize * 4]);
        File.WriteAllText(
            cuePath,
            """
            FILE "mixed.bin" BINARY
              TRACK 01 MODE2/2352
                INDEX 01 00:00:01
              TRACK 02 AUDIO
                INDEX 01 00:00:03
            """);

        try
        {
            using DiscImage disc = DiscImage.Open(cuePath);

            Assert.Equal(3, disc.SectorCount);
            Assert.Equal(2, disc.Tracks.Count);
            Assert.Equal(
                new DiscTrack(1, 0, DiscTrackMode.Mode2),
                disc.Tracks[0]);
            Assert.Equal(
                new DiscTrack(2, 2, DiscTrackMode.Audio),
                disc.Tracks[1]);
            Assert.Equal(DiscTrackMode.Audio, disc.GetTrackAt(2).Mode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BootInfoResolvesSystemConfigAndExecutable()
    {
        string directory = CreateTemporaryDirectory();
        string binPath = Path.Combine(directory, "boot.bin");
        string cuePath = Path.Combine(directory, "boot.cue");
        byte[] image = new byte[DiscImage.RawSectorSize * 32];
        Span<byte> primaryVolumeDescriptor = UserSector(image, 16);
        primaryVolumeDescriptor[0] = 1;
        "CD001"u8.CopyTo(primaryVolumeDescriptor[1..]);
        primaryVolumeDescriptor[6] = 1;
        WriteDirectoryRecord(
            primaryVolumeDescriptor,
            156,
            extent: 20,
            size: 2048,
            isDirectory: true,
            [0]);

        Span<byte> rootDirectory = UserSector(image, 20);
        int directoryOffset = 0;
        directoryOffset += WriteDirectoryRecord(
            rootDirectory,
            directoryOffset,
            extent: 20,
            size: 2048,
            isDirectory: true,
            [0]);
        directoryOffset += WriteDirectoryRecord(
            rootDirectory,
            directoryOffset,
            extent: 20,
            size: 2048,
            isDirectory: true,
            [1]);
        byte[] config = Encoding.ASCII.GetBytes(
            "BOOT = cdrom:\\SLUS_000.00;1\r\nTCB = 4\r\n");
        directoryOffset += WriteDirectoryRecord(
            rootDirectory,
            directoryOffset,
            extent: 21,
            size: config.Length,
            isDirectory: false,
            "SYSTEM.CNF;1"u8);
        WriteDirectoryRecord(
            rootDirectory,
            directoryOffset,
            extent: 22,
            size: 4096,
            isDirectory: false,
            "SLUS_000.00;1"u8);
        config.CopyTo(UserSector(image, 21));
        "PS-X EXE"u8.CopyTo(UserSector(image, 22));
        File.WriteAllBytes(binPath, image);
        File.WriteAllText(
            cuePath,
            """
            FILE "boot.bin" BINARY
              TRACK 01 MODE2/2352
                INDEX 01 00:00:00
            """);

        try
        {
            using DiscImage disc = DiscImage.Open(cuePath);

            bool found = disc.TryGetBootInfo(out DiscBootInfo? bootInfo);

            Assert.True(found);
            Assert.NotNull(bootInfo);
            Assert.Equal("SLUS_000.00;1", bootInfo.ExecutablePath);
            Assert.Equal(22, bootInfo.LogicalBlockAddress);
            Assert.Equal(4096, bootInfo.FileSize);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Span<byte> UserSector(byte[] image, int sector) =>
        image.AsSpan(
            sector * DiscImage.RawSectorSize + 24,
            2048);

    private static int WriteDirectoryRecord(
        Span<byte> destination,
        int offset,
        int extent,
        int size,
        bool isDirectory,
        ReadOnlySpan<byte> name)
    {
        int recordLength = 33 + name.Length +
            (name.Length % 2 == 0 ? 1 : 0);
        Span<byte> record = destination.Slice(offset, recordLength);
        record.Clear();
        record[0] = (byte)recordLength;
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.Slice(2, 4),
            (uint)extent);
        BinaryPrimitives.WriteUInt32BigEndian(
            record.Slice(6, 4),
            (uint)extent);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.Slice(10, 4),
            (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(
            record.Slice(14, 4),
            (uint)size);
        record[25] = isDirectory ? (byte)2 : (byte)0;
        record[28] = 1;
        record[31] = 1;
        record[32] = (byte)name.Length;
        name.CopyTo(record[33..]);
        return recordLength;
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
