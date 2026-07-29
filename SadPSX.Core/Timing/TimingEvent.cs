namespace SadPSX.Core.Timing;

public sealed class TimingEvent
{
    private readonly Action _callback;

    internal TimingEvent(ulong dueCycle, Action callback)
    {
        DueCycle = dueCycle;
        _callback = callback;
    }

    public ulong DueCycle { get; }
    public bool IsCancelled { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsPending => !IsCancelled && !IsCompleted;

    public bool Cancel()
    {
        if (!IsPending)
            return false;

        IsCancelled = true;
        return true;
    }

    internal void Invoke()
    {
        if (!IsPending)
            return;

        IsCompleted = true;
        _callback();
    }

    internal void CancelFromReset()
    {
        if (IsPending)
            IsCancelled = true;
    }
}
