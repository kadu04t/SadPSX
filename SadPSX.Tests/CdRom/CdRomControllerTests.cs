using SadPSX.Core.CdRom;
using SadPSX.Core.Interrupts;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.CdRom;

public sealed class CdRomControllerTests
{
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

        bus.CdRom.Tick(400);

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
        bus.CdRom.Tick(400);

        Assert.Equal((byte)CdRomInterruptType.Acknowledge, bus.CdRom.InterruptFlags);
        Assert.Equal(0x10, bus.Read8(CdRomController.BaseAddress + 1));

        SetIndex(bus, 1);
        bus.Write8(CdRomController.BaseAddress + 3, 0x1F);

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

    private static void WriteCommand(Bus bus, byte command, params byte[] parameters)
    {
        SetIndex(bus, 0);
        foreach (byte parameter in parameters)
            bus.Write8(CdRomController.BaseAddress + 2, parameter);
        bus.Write8(CdRomController.BaseAddress + 1, command);
        bus.CdRom.Tick(400);
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
}
