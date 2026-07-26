using SadPSX.Core.Interrupts;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Memory;

public sealed class InterruptControllerTests
{
    [Fact]
    public void RequestSetsStatusEvenWhenSourceIsMasked()
    {
        var interrupts = new InterruptController();

        interrupts.Request(InterruptSource.Timer0);

        Assert.Equal(1 << 4, interrupts.Status);
        Assert.False(interrupts.IsPending);
    }

    [Fact]
    public void MaskMakesRequestedSourcePending()
    {
        var interrupts = new InterruptController();
        interrupts.Request(InterruptSource.Timer1);

        interrupts.Write16(
            InterruptController.MaskAddress,
            1 << (int)InterruptSource.Timer1);

        Assert.True(interrupts.IsPending);
    }

    [Fact]
    public void WritingZeroToStatusAcknowledgesSelectedSources()
    {
        var interrupts = new InterruptController();
        interrupts.Request(InterruptSource.Timer0);
        interrupts.Request(InterruptSource.Timer1);

        interrupts.Write16(
            InterruptController.StatusAddress,
            unchecked((ushort)~(1 << (int)InterruptSource.Timer0)));

        Assert.Equal(1 << (int)InterruptSource.Timer1, interrupts.Status);
    }

    [Fact]
    public void RegistersRoundTripThroughBusAndAreLoggedAsHandled()
    {
        var bus = new Bus();

        bus.Write16(InterruptController.MaskAddress, 0xFFFF);
        ushort mask = bus.Read16(InterruptController.MaskAddress);

        Assert.Equal(0x07FF, mask);
        Assert.All(
            bus.Mmio.AccessSummaries,
            summary => Assert.True(summary.Handled));
        Assert.Contains(
            bus.Mmio.AccessSummaries,
            summary => summary.RegisterName == "I_MASK");
    }

    [Fact]
    public void UpperHalfOfInterruptRegistersReadsAsZeroAndIgnoresWrites()
    {
        var interrupts = new InterruptController();
        interrupts.Request(InterruptSource.Timer0);

        interrupts.Write16(InterruptController.StatusAddress + 2, 0);

        Assert.Equal(0, interrupts.Read16(InterruptController.StatusAddress + 2));
        Assert.Equal(1 << (int)InterruptSource.Timer0, interrupts.Status);
    }
}
