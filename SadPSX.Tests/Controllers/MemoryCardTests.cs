using SadPSX.Core.Controllers;
using Xunit;

namespace SadPSX.Tests.Controllers;

public sealed class MemoryCardTests
{
    [Fact]
    public void FormattedCardHasValidHeaderAndDirectory()
    {
        MemoryCard memoryCard = MemoryCard.CreateFormatted();
        byte[] image = memoryCard.ExportImage();

        Assert.Equal(MemoryCard.ImageSize, image.Length);
        Assert.Equal((byte)'M', image[0]);
        Assert.Equal((byte)'C', image[1]);
        Assert.Equal(0x0E, image[127]);

        for (int sector = 1; sector <= 15; sector++)
        {
            int offset = sector * MemoryCard.SectorSize;
            Assert.Equal(0xA0, image[offset]);
            Assert.Equal(0xFF, image[offset + 8]);
            Assert.Equal(0xFF, image[offset + 9]);
            Assert.Equal(
                CalculateChecksum(image.AsSpan(offset, 127)),
                image[offset + 127]);
        }
    }

    [Fact]
    public void ReadCommandReturnsSectorDataAndChecksum()
    {
        MemoryCard memoryCard = MemoryCard.CreateFormatted();
        byte[] request = new byte[140];
        request[0] = 0x81;
        request[1] = 0x52;

        ControllerTransferResult[] response =
            TransferPacket(memoryCard, request);

        Assert.Equal(0x08, response[1].Data);
        Assert.Equal(0x5A, response[2].Data);
        Assert.Equal(0x5D, response[3].Data);
        Assert.Equal((byte)'M', response[10].Data);
        Assert.Equal((byte)'C', response[11].Data);
        Assert.Equal(0x00, response[138].Data);
        Assert.Equal(0x47, response[139].Data);
        Assert.False(response[139].Acknowledge);
    }

    [Fact]
    public void WriteCommandCommitsValidSectorAndClearsFlag()
    {
        MemoryCard memoryCard = MemoryCard.CreateFormatted();
        byte[] sector = Enumerable.Range(0, MemoryCard.SectorSize)
            .Select(index => (byte)index)
            .ToArray();
        byte[] request = CreateWriteRequest(4, sector);

        ControllerTransferResult[] response =
            TransferPacket(memoryCard, request);

        Assert.Equal(0x47, response[^1].Data);
        Assert.False(response[^1].Acknowledge);
        Assert.Equal(0, memoryCard.Flag & 0x08);
        Assert.True(memoryCard.IsDirty);
        Assert.Equal(
            sector,
            memoryCard.ExportImage()
                .AsSpan(4 * MemoryCard.SectorSize, MemoryCard.SectorSize)
                .ToArray());
    }

    [Fact]
    public void InvalidChecksumDoesNotModifySector()
    {
        MemoryCard memoryCard = MemoryCard.CreateFormatted();
        byte[] before = memoryCard.ExportImage();
        byte[] request = CreateWriteRequest(
            4,
            Enumerable.Repeat((byte)0xCC, MemoryCard.SectorSize).ToArray());
        request[134] ^= 0xFF;

        ControllerTransferResult[] response =
            TransferPacket(memoryCard, request);

        Assert.Equal(0x4E, response[^1].Data);
        Assert.Equal(before, memoryCard.ExportImage());
    }

    [Fact]
    public void RawImagePersistsAcrossInstances()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-{Guid.NewGuid():N}.mcr");

        try
        {
            MemoryCard memoryCard = MemoryCard.LoadOrCreate(path);
            byte[] sector = Enumerable.Repeat(
                (byte)0x5A,
                MemoryCard.SectorSize).ToArray();
            TransferPacket(memoryCard, CreateWriteRequest(20, sector));

            MemoryCard reloaded = MemoryCard.Load(path);

            Assert.Equal(
                sector,
                reloaded.ExportImage()
                    .AsSpan(
                        20 * MemoryCard.SectorSize,
                        MemoryCard.SectorSize)
                    .ToArray());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".tmp"))
                File.Delete(path + ".tmp");
        }
    }

    private static ControllerTransferResult[] TransferPacket(
        MemoryCard memoryCard,
        byte[] request) =>
        request.Select(memoryCard.Transfer).ToArray();

    private static byte[] CreateWriteRequest(int sector, byte[] data)
    {
        var request = new byte[138];
        request[0] = 0x81;
        request[1] = 0x57;
        request[4] = (byte)(sector >> 8);
        request[5] = (byte)sector;
        data.CopyTo(request, 6);

        byte checksum = (byte)(request[4] ^ request[5]);
        foreach (byte value in data)
            checksum ^= value;
        request[134] = checksum;
        return request;
    }

    private static byte CalculateChecksum(ReadOnlySpan<byte> data)
    {
        byte checksum = 0;
        foreach (byte value in data)
            checksum ^= value;
        return checksum;
    }
}
