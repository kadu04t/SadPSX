namespace SadPSX.Frontend.UI.Animation;

internal sealed class AnimatedValue
{
    private float _start;
    private TimeSpan _elapsed;
    private TimeSpan _duration;

    public AnimatedValue(float initialValue)
    {
        Value = initialValue;
        Target = initialValue;
        _start = initialValue;
    }

    public float Value { get; private set; }

    public float Target { get; private set; }

    public bool IsAnimating => _elapsed < _duration;

    public void SetTarget(float target, TimeSpan duration)
    {
        if (target == Target)
            return;

        if (duration <= TimeSpan.Zero)
        {
            SnapTo(target);
            return;
        }

        _start = Value;
        Target = target;
        _duration = duration;
        _elapsed = TimeSpan.Zero;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (!IsAnimating || elapsed <= TimeSpan.Zero)
            return;

        TimeSpan nextElapsed = _elapsed + elapsed;
        _elapsed = nextElapsed < _duration ? nextElapsed : _duration;
        float progress = (float)(_elapsed.TotalSeconds / _duration.TotalSeconds);
        float eased = 1f - MathF.Pow(1f - progress, 3f);
        Value = _start + ((Target - _start) * eased);

        if (_elapsed == _duration)
            Value = Target;
    }

    public void SnapTo(float value)
    {
        _start = value;
        Value = value;
        Target = value;
        _elapsed = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
    }
}
