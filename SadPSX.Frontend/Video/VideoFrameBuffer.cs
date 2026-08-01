using SadPSX.Core.Gpu;

namespace SadPSX.Frontend.Video;

internal sealed class VideoFrameBuffer
{
    private const int SignatureSampleCount = 2_048;
    private const ulong SignatureOffset = 14_695_981_039_346_656_037;
    private const ulong SignaturePrime = 1_099_511_628_211;

    private uint[] _pixels = [];
    private GpuDisplayInfo _display;
    private bool _hasFrame;
    private bool _hasSignature;
    private ulong _signature;

    public bool HasFrame => _hasFrame;

    public GpuDisplayInfo Display => _display;

    public Span<uint> Pixels => _pixels;

    public ulong CapturedFrames { get; private set; }

    public ulong PresentedFrames { get; private set; }

    public ulong DroppedFrames { get; private set; }

    public ulong ConsecutiveDuplicateFrames { get; private set; }

    public ulong LastSignature => _signature;

    public void Capture(Gpu gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);

        GpuDisplayInfo display = gpu.GetDisplayInfo();
        int requiredPixels = checked(display.Width * display.Height);
        if (_pixels.Length != requiredPixels)
            _pixels = new uint[requiredPixels];

        gpu.CopyDisplayRgba(_pixels);
        ulong signature = ComputeSignature(display, _pixels);

        if (_hasFrame)
            DroppedFrames++;
        if (_hasSignature && signature == _signature)
            ConsecutiveDuplicateFrames++;
        else
            ConsecutiveDuplicateFrames = 0;

        _display = display;
        _signature = signature;
        _hasSignature = true;
        _hasFrame = true;
        CapturedFrames++;
    }

    public void MarkPresented()
    {
        if (!_hasFrame)
            return;

        _hasFrame = false;
        PresentedFrames++;
    }

    public VideoPresentationMetrics GetMetrics() =>
        new(
            CapturedFrames,
            PresentedFrames,
            DroppedFrames,
            ConsecutiveDuplicateFrames,
            _signature,
            _hasFrame);

    private static ulong ComputeSignature(
        GpuDisplayInfo display,
        ReadOnlySpan<uint> pixels)
    {
        ulong signature = SignatureOffset;
        AddValue(ref signature, (uint)display.VramX);
        AddValue(ref signature, (uint)display.VramY);
        AddValue(ref signature, (uint)display.Width);
        AddValue(ref signature, (uint)display.Height);
        AddValue(ref signature, display.Enabled ? 1u : 0u);
        AddValue(ref signature, display.Is24BitColor ? 1u : 0u);

        int step = Math.Max(1, pixels.Length / SignatureSampleCount);
        for (int index = 0; index < pixels.Length; index += step)
            AddValue(ref signature, pixels[index]);

        return signature;
    }

    private static void AddValue(ref ulong signature, uint value)
    {
        signature ^= value;
        signature *= SignaturePrime;
    }
}

internal readonly record struct VideoPresentationMetrics(
    ulong CapturedFrames,
    ulong PresentedFrames,
    ulong DroppedFrames,
    ulong ConsecutiveDuplicateFrames,
    ulong LastSignature,
    bool FramePending);
