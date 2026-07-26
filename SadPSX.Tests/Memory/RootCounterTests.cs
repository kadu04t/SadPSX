using SadPSX.Core;
using SadPSX.Core.Bios;
using SadPSX.Core.Interrupts;
using SadPSX.Core.Timers;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Memory;

public sealed class RootCounterTests
{
    [Fact]
    public void CounterAndTargetRoundTripThroughMmio()
    {
        var bus = new Bus();

        bus.Write16(RootCounters.Timer0BaseAddress, 0x1234);
        bus.Write16(RootCounters.Timer0BaseAddress + 8, 0x5678);

        Assert.Equal(0x1234, bus.Read16(RootCounters.Timer0BaseAddress));
        Assert.Equal(0x5678, bus.Read16(RootCounters.Timer0BaseAddress + 8));
    }

    [Fact]
    public void ModeWriteResetsCounterAndDelaysCountingForTwoCycles()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        timers.Write16(RootCounters.Timer0BaseAddress, 100);

        timers.Write16(RootCounters.Timer0BaseAddress + 4, 0);
        timers.Tick(2);

        Assert.Equal(0, timers.GetCounter(0));

        timers.Tick(1);

        Assert.Equal(1, timers.GetCounter(0));
    }

    [Fact]
    public void TargetFlagIsClearedAfterReadingMode()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        timers.Write16(RootCounters.Timer0BaseAddress + 8, 3);
        timers.Write16(RootCounters.Timer0BaseAddress + 4, 1 << 3);
        timers.Tick(5);

        ushort firstRead = timers.Read16(RootCounters.Timer0BaseAddress + 4);
        ushort secondRead = timers.Read16(RootCounters.Timer0BaseAddress + 4);

        Assert.NotEqual(0, firstRead & (1 << 11));
        Assert.Equal(0, secondRead & (1 << 11));
    }

    [Fact]
    public void TargetIrqRequestsTimerInterrupt()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        timers.Write16(RootCounters.Timer0BaseAddress + 8, 3);
        timers.Write16(
            RootCounters.Timer0BaseAddress + 4,
            (1 << 3) | (1 << 4) | (1 << 6));

        timers.Tick(5);

        Assert.NotEqual(
            0,
            interrupts.Status & (1 << (int)InterruptSource.Timer0));
    }

    [Fact]
    public void OneShotModeSuppressesFurtherInterruptRequests()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        timers.Write16(RootCounters.Timer0BaseAddress + 8, 1);
        timers.Write16(
            RootCounters.Timer0BaseAddress + 4,
            (1 << 3) | (1 << 4));
        timers.Tick(3);
        interrupts.Write16(
            InterruptController.StatusAddress,
            unchecked((ushort)~(1 << (int)InterruptSource.Timer0)));

        timers.Tick(2);

        Assert.Equal(
            0,
            interrupts.Status & (1 << (int)InterruptSource.Timer0));
    }

    [Fact]
    public void Timer2CanUseSystemClockDividedByEight()
    {
        var interrupts = new InterruptController();
        var timers = new RootCounters(interrupts);
        timers.Write16(
            RootCounters.Timer2BaseAddress + 4,
            2 << 8);
        timers.Tick(9);

        Assert.Equal(0, timers.GetCounter(2));

        timers.Tick(1);

        Assert.Equal(1, timers.GetCounter(2));
    }

    [Fact]
    public void MachineTicksRootCountersAfterEachCpuStep()
    {
        var machine = new PsxMachine();
        machine.LoadBios(new byte[BiosRom.SizeInBytes]);
        machine.Bus.Write16(
            RootCounters.Timer0BaseAddress + 4,
            0);

        machine.Step();

        Assert.Equal(27, machine.Bus.RootCounters.GetCounter(0));
    }
}
