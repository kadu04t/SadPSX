namespace SadPSX.Core.Timing;

public sealed class TimingScheduler
{
    private readonly List<IClockedDevice> _devices = new();
    private readonly HashSet<IClockedDevice> _registeredDevices =
        new(ReferenceEqualityComparer.Instance);

    public ulong ClockCycles { get; private set; }
    public int RegisteredDeviceCount => _devices.Count;

    public bool Register(IClockedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_registeredDevices.Add(device))
            return false;

        _devices.Add(device);
        return true;
    }

    public void Advance(uint cycles)
    {
        if (cycles == 0)
            return;

        ClockCycles = unchecked(ClockCycles + cycles);

        int deviceCount = _devices.Count;
        for (int index = 0; index < deviceCount; index++)
            _devices[index].Tick(cycles);
    }

    public void ResetClock()
    {
        ClockCycles = 0;
    }
}
