using SadPSX.Core;
using SadPSX.Core.Timing;
using Xunit;

namespace SadPSX.Tests.Timing;

public sealed class TimingSchedulerTests
{
    [Fact]
    public void AdvanceTicksDevicesInRegistrationOrderAndTracksClock()
    {
        var scheduler = new TimingScheduler();
        var tickOrder = new List<string>();
        var first = new RecordingDevice("first", tickOrder);
        var second = new RecordingDevice("second", tickOrder);

        scheduler.Register(first);
        scheduler.Register(second);
        scheduler.Advance(17);

        Assert.Equal(17ul, scheduler.ClockCycles);
        Assert.Equal(["first", "second"], tickOrder);
        Assert.Equal(17ul, first.TotalCycles);
        Assert.Equal(17ul, second.TotalCycles);
    }

    [Fact]
    public void RegisterRejectsTheSameInstanceTwice()
    {
        var scheduler = new TimingScheduler();
        var device = new RecordingDevice("device", []);

        Assert.True(scheduler.Register(device));
        Assert.False(scheduler.Register(device));
        Assert.Equal(1, scheduler.RegisteredDeviceCount);

        scheduler.Advance(3);

        Assert.Equal(1, device.TickCount);
    }

    [Fact]
    public void AdvanceWithZeroCyclesDoesNotTickDevices()
    {
        var scheduler = new TimingScheduler();
        var device = new RecordingDevice("device", []);
        scheduler.Register(device);

        scheduler.Advance(0);

        Assert.Equal(0ul, scheduler.ClockCycles);
        Assert.Equal(0, device.TickCount);
    }

    [Fact]
    public void ResetClockPreservesRegisteredDevices()
    {
        var scheduler = new TimingScheduler();
        var device = new RecordingDevice("device", []);
        scheduler.Register(device);
        scheduler.Advance(11);

        scheduler.ResetClock();
        scheduler.Advance(5);

        Assert.Equal(5ul, scheduler.ClockCycles);
        Assert.Equal(16ul, device.TotalCycles);
        Assert.Equal(2, device.TickCount);
    }

    [Fact]
    public void DeviceRegisteredDuringAdvanceStartsOnNextAdvance()
    {
        var scheduler = new TimingScheduler();
        var lateDevice = new RecordingDevice("late", []);
        var registeringDevice = new CallbackDevice(
            () => scheduler.Register(lateDevice));
        scheduler.Register(registeringDevice);

        scheduler.Advance(2);

        Assert.Equal(0, lateDevice.TickCount);

        scheduler.Advance(3);

        Assert.Equal(1, lateDevice.TickCount);
        Assert.Equal(3ul, lateDevice.TotalCycles);
    }

    [Fact]
    public void ScheduledEventRunsAtExactCycleAndSplitsDeviceTicks()
    {
        var scheduler = new TimingScheduler();
        var segments = new List<uint>();
        scheduler.Register(new SegmentRecordingDevice(segments));
        ulong observedCycle = 0;
        scheduler.Schedule(3, () => observedCycle = scheduler.ClockCycles);

        scheduler.Advance(10);

        Assert.Equal(3ul, observedCycle);
        Assert.Equal([3u, 7u], segments);
        Assert.Equal(10ul, scheduler.ClockCycles);
    }

    [Fact]
    public void EventsAtSameCycleRunInSchedulingOrder()
    {
        var scheduler = new TimingScheduler();
        var order = new List<int>();
        scheduler.Schedule(5, () => order.Add(1));
        scheduler.Schedule(5, () => order.Add(2));
        scheduler.Schedule(5, () => order.Add(3));

        scheduler.Advance(5);

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void CancelledEventDoesNotRun()
    {
        var scheduler = new TimingScheduler();
        bool invoked = false;
        TimingEvent timingEvent =
            scheduler.Schedule(2, () => invoked = true);

        Assert.True(timingEvent.Cancel());
        scheduler.Advance(2);

        Assert.False(invoked);
        Assert.True(timingEvent.IsCancelled);
        Assert.Equal(0, scheduler.PendingEventCount);
    }

    [Fact]
    public void ResetCancelsPendingEvents()
    {
        var scheduler = new TimingScheduler();
        TimingEvent timingEvent = scheduler.Schedule(4, () => { });

        scheduler.ResetClock();

        Assert.True(timingEvent.IsCancelled);
        Assert.Equal(0, scheduler.PendingEventCount);
        Assert.Equal(0ul, scheduler.ClockCycles);
    }

    private sealed class RecordingDevice(
        string name,
        List<string> tickOrder) : IClockedDevice
    {
        public ulong TotalCycles { get; private set; }
        public int TickCount { get; private set; }

        public void Tick(uint cycles)
        {
            TotalCycles += cycles;
            TickCount++;
            tickOrder.Add(name);
        }
    }

    private sealed class CallbackDevice(Action callback) : IClockedDevice
    {
        public void Tick(uint cycles)
        {
            callback();
        }
    }

    private sealed class SegmentRecordingDevice(
        List<uint> segments) : IClockedDevice
    {
        public void Tick(uint cycles)
        {
            segments.Add(cycles);
        }
    }
}
