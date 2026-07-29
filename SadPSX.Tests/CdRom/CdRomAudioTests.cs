using System.Buffers.Binary;
using SadPSX.Core.CdRom;
using SadPSX.Core.CdRom.Media;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.CdRom;

public sealed class CdRomAudioTests
{
    [Fact]
    public void CdVolumeMatrixCanSwapStereoChannels()
    {
        var sector = new byte[DiscImage.RawSectorSize];
        BinaryPrimitives.WriteInt16LittleEndian(sector, 1_000);
        BinaryPrimitives.WriteInt16LittleEndian(sector.AsSpan(2), -2_000);
        var bus = new Bus();
        bus.CdRom.LoadDisc(new MemoryDiscImage(
            sector,
            DiscTrackMode.Audio));
        byte[]? output = null;
        bus.CdRom.CdAudioSectorReady += audio => output = audio.ToArray();

        SetIndex(bus, 2);
        bus.Write8(CdRomController.BaseAddress + 2, 0);
        bus.Write8(CdRomController.BaseAddress + 3, 0x80);
        SetIndex(bus, 3);
        bus.Write8(CdRomController.BaseAddress + 1, 0);
        bus.Write8(CdRomController.BaseAddress + 2, 0x80);
        bus.Write8(CdRomController.BaseAddress + 3, 0x20);

        WriteCommand(bus, 0x03);
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SingleSpeedSectorCycles);

        Assert.NotNull(output);
        Assert.Equal(-2_000, BinaryPrimitives.ReadInt16LittleEndian(output));
        Assert.Equal(1_000, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(2)));
    }

    [Fact]
    public void ReadDeliversXaAudioInsteadOfDataInterrupt()
    {
        byte[] sector = CreateXaSector();
        var bus = new Bus();
        bus.CdRom.LoadDisc(new MemoryDiscImage(
            sector,
            DiscTrackMode.Mode2));
        byte[]? output = null;
        bus.CdRom.CdAudioSectorReady += audio => output = audio.ToArray();

        WriteCommand(bus, 0x0E, 0x40);
        Acknowledge(bus);
        WriteCommand(bus, 0x06);
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SingleSpeedSectorCycles);

        Assert.NotNull(output);
        Assert.Equal(2_352 * 4, output.Length);
        Assert.Equal(0, bus.CdRom.BufferedSectorCount);
        Assert.Equal(0, bus.CdRom.InterruptFlags);
    }

    [Fact]
    public void XaFilterDropsNonMatchingAudioChannels()
    {
        byte[] sector = CreateXaSector();
        sector[16] = 2;
        sector[17] = 3;
        var bus = new Bus();
        bus.CdRom.LoadDisc(new MemoryDiscImage(
            sector,
            DiscTrackMode.Mode2));
        int deliveredSectors = 0;
        bus.CdRom.CdAudioSectorReady += _ => deliveredSectors++;

        WriteCommand(bus, 0x0D, 1, 1);
        Acknowledge(bus);
        WriteCommand(bus, 0x0E, 0x48);
        Acknowledge(bus);
        WriteCommand(bus, 0x06);
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SingleSpeedSectorCycles);

        Assert.Equal(0, deliveredSectors);
        Assert.Equal(0, bus.CdRom.BufferedSectorCount);
        Assert.Equal(0, bus.CdRom.InterruptFlags);
    }

    private static byte[] CreateXaSector()
    {
        var sector = new byte[DiscImage.RawSectorSize];
        sector[15] = 2;
        sector[16] = 1;
        sector[17] = 1;
        sector[18] = 0x44;
        sector[19] = 1;
        return sector;
    }

    private static void WriteCommand(
        Bus bus,
        byte command,
        params byte[] parameters)
    {
        SetIndex(bus, 0);
        foreach (byte parameter in parameters)
            bus.Write8(CdRomController.BaseAddress + 2, parameter);
        bus.Write8(CdRomController.BaseAddress + 1, command);
        bus.CdRom.Tick(CdRomController.DefaultCommandDelayCycles);
    }

    private static void Acknowledge(Bus bus)
    {
        SetIndex(bus, 1);
        bus.Write8(CdRomController.BaseAddress + 3, 0x1F);
    }

    private static void SetIndex(Bus bus, byte index) =>
        bus.Write8(CdRomController.BaseAddress, index);

    private sealed class MemoryDiscImage(
        byte[] sector,
        DiscTrackMode mode) : DiscImage
    {
        public override int SectorCount => 1;
        public override DiscTrackMode TrackMode => mode;

        public override void ReadSector(
            int logicalBlockAddress,
            Span<byte> destination)
        {
            if (logicalBlockAddress != 0)
                throw new ArgumentOutOfRangeException(
                    nameof(logicalBlockAddress));
            sector.CopyTo(destination);
        }

        public override void Dispose()
        {
        }
    }
}
