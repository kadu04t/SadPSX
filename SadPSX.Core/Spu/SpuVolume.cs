namespace SadPSX.Core.Spu;

internal sealed class SpuVolume
{
    private ushort _control;
    private int _counter;

    public int Level { get; private set; }

    public void Reset()
    {
        _control = 0;
        _counter = 0;
        Level = 0;
    }

    public void Configure(ushort value)
    {
        _control = value;
        _counter = 0;
        if ((value & 0x8000) == 0)
            Level = DecodeFixed(value);
    }

    public int Tick()
    {
        if ((_control & 0x8000) == 0)
            return Level;

        bool exponential = (_control & 0x4000) != 0;
        bool decreasing = (_control & 0x2000) != 0;
        bool negative = (_control & 0x1000) != 0;
        int shift = (_control >> 2) & 0x1F;
        int stepValue = _control & 3;
        int magnitude = Math.Abs(Level);

        int step = 7 - stepValue;
        if (decreasing)
            step = ~step;
        step <<= Math.Max(0, 11 - shift);

        int counterIncrement = 0x8000 >> Math.Max(0, shift - 11);
        if (exponential && !decreasing && magnitude > 0x6000)
        {
            if (shift < 10)
                step >>= 2;
            else if (shift >= 11)
                counterIncrement >>= 2;
            else
            {
                step >>= 1;
                counterIncrement >>= 1;
            }
        }
        else if (exponential && decreasing)
        {
            step = step * magnitude / 0x8000;
        }

        _counter += Math.Max(counterIncrement, 1);
        if ((_counter & 0x8000) == 0)
            return Level;

        _counter &= 0x7FFF;
        magnitude = Math.Clamp(magnitude + step, 0, 0x7FFF);
        Level = negative ? -magnitude : magnitude;
        return Level;
    }

    private static int DecodeFixed(ushort value)
    {
        int volume = value & 0x7FFF;
        if ((volume & 0x4000) != 0)
            volume |= ~0x7FFF;
        return Math.Clamp(volume * 2, short.MinValue, short.MaxValue);
    }
}
