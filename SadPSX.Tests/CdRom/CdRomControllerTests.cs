using SadPSX.Core.CdRom;
using SadPSX.Core.CdRom.Media;
using SadPSX.Core.Interrupts;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.CdRom;

public sealed class CdRomControllerTests
{
    [Fact]
    public void CommandResponseWaitsForControllerLatency()
    {
        var bus = new Bus();

        SetIndex(bus, 0);
        bus.Write8(CdRomController.BaseAddress + 1, 0x01);
        bus.CdRom.Tick(CdRomController.DefaultCommandDelayCycles - 1);

        Assert.Equal(0, bus.CdRom.InterruptFlags);
        Assert.Equal(0, bus.CdRom.ResultCount);

        bus.CdRom.Tick(1);

        Assert.Equal(
            (byte)CdRomInterruptType.Acknowledge,
            bus.CdRom.InterruptFlags);
        Assert.Equal(1, bus.CdRom.ResultCount);
    }

    [Fact]
    public void StatusTracksIndexAndParameterFifo()
    {
        var bus = new Bus();

        Assert.Equal(0x18, bus.Read8(CdRomController.BaseAddress));

        bus.Write8(CdRomController.BaseAddress + 2, 0x12);

        Assert.Equal(0x10, bus.Read8(CdRomController.BaseAddress));
        Assert.Equal(1, bus.CdRom.ParameterCount);

        bus.Write8(CdRomController.BaseAddress, 1);

        Assert.Equal(1, bus.Read8(CdRomController.BaseAddress) & 3);
    }

    [Fact]
    public void CommandProducesResultAndMaskedIrq()
    {
        var bus = new Bus();
        bus.Write16(InterruptController.MaskAddress, 1 << 2);
        SetIndex(bus, 1);
        bus.Write8(CdRomController.BaseAddress + 2, 0x1F);
        SetIndex(bus, 0);

        bus.Write8(CdRomController.BaseAddress + 1, 0x01);
        Assert.True(bus.CdRom.CommandBusy);

        bus.CdRom.Tick(CdRomController.DefaultCommandDelayCycles);

        Assert.False(bus.CdRom.CommandBusy);
        Assert.Equal(0x10, bus.Read8(CdRomController.BaseAddress + 1));
        Assert.Equal((byte)CdRomInterruptType.Acknowledge, bus.CdRom.InterruptFlags);
        Assert.NotEqual(0, bus.InterruptController.Status & (1 << 2));
    }

    [Fact]
    public void AcknowledgingGetIdPresentsSecondResponse()
    {
        var bus = new Bus();
        bus.Write8(CdRomController.BaseAddress + 1, 0x1A);
        bus.CdRom.Tick(CdRomController.DefaultCommandDelayCycles);

        Assert.Equal((byte)CdRomInterruptType.Acknowledge, bus.CdRom.InterruptFlags);
        Assert.Equal(0x10, bus.Read8(CdRomController.BaseAddress + 1));

        SetIndex(bus, 1);
        bus.Write8(CdRomController.BaseAddress + 3, 0x1F);
        bus.CdRom.Tick(CdRomController.SecondResponseDelayCycles);

        Assert.Equal((byte)CdRomInterruptType.DiskError, bus.CdRom.InterruptFlags);
        Assert.Equal(
            new byte[] { 0x08, 0x40, 0, 0, 0, 0, 0, 0 },
            ReadResults(bus, 8));
    }

    [Fact]
    public void ClearParameterControlEmptiesFifo()
    {
        var bus = new Bus();
        bus.Write8(CdRomController.BaseAddress + 2, 1);
        bus.Write8(CdRomController.BaseAddress + 2, 2);
        SetIndex(bus, 1);

        bus.Write8(CdRomController.BaseAddress + 3, 0x40);

        Assert.Equal(0, bus.CdRom.ParameterCount);
    }

    [Fact]
    public void SetModeAndGetParamPreserveConfiguration()
    {
        var bus = new Bus();
        WriteCommand(bus, 0x0D, 0x22, 0x07);
        Acknowledge(bus);
        WriteCommand(bus, 0x0E, 0x80);
        Acknowledge(bus);

        WriteCommand(bus, 0x0F);

        Assert.Equal(
            new byte[] { 0x10, 0x80, 0, 0x22, 0x07 },
            ReadResults(bus, 5));
    }

    [Fact]
    public void ReadNContinuesUntilEndOfDisc()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(3);
        bus.CdRom.LoadDisc(disc);

        WriteCommand(bus, 0x06);
        Acknowledge(bus);

        for (int sector = 0; sector < 3; sector++)
        {
            bus.CdRom.Tick(CdRomController.SingleSpeedSectorCycles);
            Assert.Equal(
                (byte)CdRomInterruptType.DataReady,
                bus.CdRom.InterruptFlags);
            Acknowledge(bus);
        }

        bus.CdRom.Tick(CdRomController.SingleSpeedSectorCycles);

        Assert.False(bus.CdRom.IsReading);
        Assert.Equal(3, bus.CdRom.BufferedSectorCount);
        Assert.Equal(
            (byte)CdRomInterruptType.DataEnd,
            bus.CdRom.InterruptFlags);
    }

    [Fact]
    public void DoubleSpeedModeHalvesSectorInterval()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(2);
        bus.CdRom.LoadDisc(disc);
        WriteCommand(bus, 0x0E, 0x80);
        Acknowledge(bus);
        WriteCommand(bus, 0x06);
        Acknowledge(bus);

        bus.CdRom.Tick(CdRomController.DoubleSpeedSectorCycles);

        Assert.Equal(
            (byte)CdRomInterruptType.DataReady,
            bus.CdRom.InterruptFlags);
        Assert.Equal(1, bus.CdRom.BufferedSectorCount);
    }

    [Fact]
    public void PauseStopsContinuousReadingAndCompletes()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(3);
        bus.CdRom.LoadDisc(disc);
        WriteCommand(bus, 0x06);
        Acknowledge(bus);

        WriteCommand(bus, 0x09);

        Assert.False(bus.CdRom.IsReading);
        Assert.Equal(
            (byte)CdRomInterruptType.Acknowledge,
            bus.CdRom.InterruptFlags);
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SecondResponseDelayCycles);
        Assert.Equal(
            (byte)CdRomInterruptType.Complete,
            bus.CdRom.InterruptFlags);
    }

    [Fact]
    public void TrackCommandsReportCueTableOfContents()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(
            300,
            [
                new DiscTrack(1, 0, DiscTrackMode.Mode2),
                new DiscTrack(2, 150, DiscTrackMode.Audio),
            ]);
        bus.CdRom.LoadDisc(disc);

        WriteCommand(bus, 0x13);
        Assert.Equal(
            new byte[] { 0x00, 0x01, 0x02 },
            ReadResults(bus, 3));
        Acknowledge(bus);

        WriteCommand(bus, 0x14, 0x02);
        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x04 },
            ReadResults(bus, 3));
    }

    [Fact]
    public void InitStartsMotorAndRestoresDefaultMode()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(1);
        bus.CdRom.LoadDisc(disc);

        SetIndex(bus, 0);
        bus.Write8(CdRomController.BaseAddress + 1, 0x0A);
        bus.CdRom.Tick(CdRomController.InitializationCommandDelayCycles);

        Assert.Equal(
            (byte)CdRomInterruptType.Acknowledge,
            bus.CdRom.InterruptFlags);
        Assert.Equal(new byte[] { 0x02 }, ReadResults(bus, 1));
        Assert.Equal(0x20, bus.CdRom.Mode);

        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SecondResponseDelayCycles);

        Assert.Equal(
            (byte)CdRomInterruptType.Complete,
            bus.CdRom.InterruptFlags);
        Assert.Equal(new byte[] { 0x02 }, ReadResults(bus, 1));
    }

    [Fact]
    public void SeekReportsBusyUntilDelayedCompletion()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(32);
        bus.CdRom.LoadDisc(disc);

        WriteCommand(bus, 0x02, 0x00, 0x02, 0x10);
        Acknowledge(bus);
        WriteCommand(bus, 0x15);

        Assert.Equal(
            (byte)CdRomInterruptType.Acknowledge,
            bus.CdRom.InterruptFlags);
        Assert.NotEqual(0, ReadResults(bus, 1)[0] & (1 << 6));

        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SeekResponseDelayCycles - 1);
        Assert.Equal(0, bus.CdRom.InterruptFlags);

        bus.CdRom.Tick(1);

        Assert.Equal(
            (byte)CdRomInterruptType.Complete,
            bus.CdRom.InterruptFlags);
        Assert.Equal(0, ReadResults(bus, 1)[0] & (1 << 6));
    }

    [Fact]
    public void InvalidSetLocationReturnsParameterError()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(32);
        bus.CdRom.LoadDisc(disc);

        WriteCommand(bus, 0x02, 0x00, 0x6A, 0x00);

        Assert.Equal(
            (byte)CdRomInterruptType.DiskError,
            bus.CdRom.InterruptFlags);
        Assert.Equal(new byte[] { 0x01, 0x10 }, ReadResults(bus, 2));
    }

    [Fact]
    public void LicensedDataDiscCompletesIdentificationAndSessionSetup()
    {
        var bus = new Bus();
        using var disc = new TestDiscImage(64);
        bus.CdRom.LoadDisc(disc);

        SetIndex(bus, 0);
        bus.Write8(CdRomController.BaseAddress + 1, 0x0A);
        bus.CdRom.Tick(CdRomController.InitializationCommandDelayCycles);
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SecondResponseDelayCycles);
        Acknowledge(bus);

        WriteCommand(bus, 0x1A);
        Assert.Equal(new byte[] { 0x02 }, ReadResults(bus, 1));
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SecondResponseDelayCycles);

        Assert.Equal(
            (byte)CdRomInterruptType.Complete,
            bus.CdRom.InterruptFlags);
        Assert.Equal(
            new byte[]
            {
                0x02,
                0,
                0x20,
                0,
                (byte)'S',
                (byte)'C',
                (byte)'E',
                (byte)'A',
            },
            ReadResults(bus, 8));
        Acknowledge(bus);

        WriteCommand(bus, 0x12, 1);
        Assert.NotEqual(0, ReadResults(bus, 1)[0] & (1 << 6));
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SeekResponseDelayCycles);

        Assert.Equal(
            (byte)CdRomInterruptType.Complete,
            bus.CdRom.InterruptFlags);
        Assert.Equal(0, ReadResults(bus, 1)[0] & (1 << 6));
    }

    private static void WriteCommand(Bus bus, byte command, params byte[] parameters)
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

    private static byte[] ReadResults(Bus bus, int count)
    {
        SetIndex(bus, 0);
        return Enumerable.Range(0, count)
            .Select(_ => bus.Read8(CdRomController.BaseAddress + 1))
            .ToArray();
    }

    private static void SetIndex(Bus bus, byte index) =>
        bus.Write8(CdRomController.BaseAddress, index);

    private sealed class TestDiscImage(
        int sectorCount,
        IReadOnlyList<DiscTrack>? tracks = null) : DiscImage
    {
        public override int SectorCount { get; } = sectorCount;
        public override DiscTrackMode TrackMode => DiscTrackMode.Mode2;
        public override IReadOnlyList<DiscTrack> Tracks { get; } =
            tracks ?? [new DiscTrack(1, 0, DiscTrackMode.Mode2)];

        public override void ReadSector(
            int logicalBlockAddress,
            Span<byte> destination)
        {
            destination.Clear();
            destination[24] = (byte)logicalBlockAddress;
        }

        public override void Dispose()
        {
        }
    }
}
