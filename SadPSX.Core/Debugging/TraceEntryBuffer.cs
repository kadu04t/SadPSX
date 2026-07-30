using System.Collections;

namespace SadPSX.Core.Debugging;

internal sealed class TraceEntryBuffer : IReadOnlyList<TraceEntry>
{
    private readonly List<TraceEntry> _entries = [];
    private int _start;
    private int? _capacity;

    public int Count => _entries.Count;

    public int? Capacity
    {
        get => _capacity;
        set
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            Normalize();
            _capacity = value;
            if (value is int capacity && _entries.Count > capacity)
                _entries.RemoveRange(0, _entries.Count - capacity);
        }
    }

    public TraceEntry this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= _entries.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _entries[(_start + index) % _entries.Count];
        }
    }

    public void Add(TraceEntry entry)
    {
        if (_capacity is not int capacity || _entries.Count < capacity)
        {
            Normalize();
            _entries.Add(entry);
            return;
        }

        _entries[_start] = entry;
        _start = (_start + 1) % _entries.Count;
    }

    public void Clear()
    {
        _entries.Clear();
        _start = 0;
    }

    public IEnumerator<TraceEntry> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
            yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Normalize()
    {
        if (_start == 0 || _entries.Count == 0)
            return;

        TraceEntry[] ordered = new TraceEntry[_entries.Count];
        for (int index = 0; index < ordered.Length; index++)
            ordered[index] = this[index];
        _entries.Clear();
        _entries.AddRange(ordered);
        _start = 0;
    }
}
