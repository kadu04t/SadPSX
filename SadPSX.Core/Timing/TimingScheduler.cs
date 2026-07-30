namespace SadPSX.Core.Timing;

public sealed class TimingScheduler
{
    private readonly List<IClockedDevice> _devices = new();
    private readonly HashSet<IClockedDevice> _registeredDevices =
        new(ReferenceEqualityComparer.Instance);
    private readonly PriorityQueue<TimingEvent, (ulong Cycle, ulong Sequence)>
        _events = new();

    private ulong _eventSequence;
    private IClockedDevice[] _deviceSnapshot = [];
    private Action<uint>? _primaryTicker;
    private int _primaryDeviceCount;

    public ulong ClockCycles { get; private set; }
    public int RegisteredDeviceCount =>
        _primaryDeviceCount + _devices.Count;
    public int PendingEventCount => _events.UnorderedItems.Count(
        item => item.Element.IsPending);

    public bool Register(IClockedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_registeredDevices.Add(device))
            return false;

        _devices.Add(device);
        _deviceSnapshot = [.. _devices];
        return true;
    }

    internal void SetPrimaryTicker(Action<uint> ticker, int deviceCount)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        if (deviceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(deviceCount));
        if (_primaryTicker is not null)
        {
            throw new InvalidOperationException(
                "A primary timing ticker is already configured.");
        }

        _primaryTicker = ticker;
        _primaryDeviceCount = deviceCount;
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

        if (_events.Count == 0)
        {
            TickDevices(cycles);
            return;
        }

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
        _primaryTicker?.Invoke(cycles);

        IClockedDevice[] devices = _deviceSnapshot;
        int deviceCount = devices.Length;
        for (int index = 0; index < deviceCount; index++)
            devices[index].Tick(cycles);
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
