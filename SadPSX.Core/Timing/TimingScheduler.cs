namespace SadPSX.Core.Timing;

public sealed class TimingScheduler
{
    private readonly List<IClockedDevice> _devices = new();
    private readonly HashSet<IClockedDevice> _registeredDevices =
        new(ReferenceEqualityComparer.Instance);
    private readonly PriorityQueue<TimingEvent, (ulong Cycle, ulong Sequence)>
        _events = new();

    private ulong _eventSequence;

    public ulong ClockCycles { get; private set; }
    public int RegisteredDeviceCount => _devices.Count;
    public int PendingEventCount => _events.UnorderedItems.Count(
        item => item.Element.IsPending);

    public bool Register(IClockedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_registeredDevices.Add(device))
            return false;

        _devices.Add(device);
        return true;
    }

    public TimingEvent Schedule(uint delayCycles, Action callback)
    {
        if (delayCycles == 0)
            throw new ArgumentOutOfRangeException(nameof(delayCycles));
        ArgumentNullException.ThrowIfNull(callback);

        ulong dueCycle = checked(ClockCycles + delayCycles);
        var timingEvent = new TimingEvent(dueCycle, callback);
        _events.Enqueue(
            timingEvent,
            (dueCycle, _eventSequence++));
        return timingEvent;
    }

    public void Advance(uint cycles)
    {
        if (cycles == 0)
            return;

        ulong targetCycle = checked(ClockCycles + cycles);
        while (_events.TryPeek(out TimingEvent? timingEvent, out var priority) &&
               priority.Cycle <= targetCycle)
        {
            _events.Dequeue();
            if (!timingEvent.IsPending)
                continue;

            TickDevices((uint)(priority.Cycle - ClockCycles));
            timingEvent.Invoke();
        }

        TickDevices((uint)(targetCycle - ClockCycles));
    }

    private void TickDevices(uint cycles)
    {
        if (cycles == 0)
            return;

        ClockCycles += cycles;
        int deviceCount = _devices.Count;
        for (int index = 0; index < deviceCount; index++)
            _devices[index].Tick(cycles);
    }

    public void ResetClock()
    {
        foreach (var item in _events.UnorderedItems)
            item.Element.CancelFromReset();
        _events.Clear();

        ClockCycles = 0;
        _eventSequence = 0;
    }
}
