using SadPSX.Core.Interrupts;
using SadPSX.Core.Timers;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

namespace SadPSX.Core.Gpu;

public sealed class VideoTiming : IClockedDevice
{
    public const uint CpuClockHz = 33_868_800;
    public const uint NtscVideoClockHz = 53_693_175;
    public const uint PalVideoClockHz = 53_203_425;
    public const uint NtscVideoCyclesPerScanline = 3_413;
    public const uint PalVideoCyclesPerScanline = 3_406;
    public const uint NtscScanlinesPerFrame = 263;
    public const uint PalScanlinesPerFrame = 314;

    private readonly GpuDevice _gpu;
    private readonly RootCounters _rootCounters;
    private readonly InterruptController _interruptController;

    private ulong _videoClockRemainder;
    private ulong _dotClockRemainder;
    private bool _lastPalMode;
    private uint _lastDotClockDivisor;

    public VideoTiming(
        GpuDevice gpu,
        RootCounters rootCounters,
        InterruptController interruptController)
    {
        _gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
        _rootCounters = rootCounters ??
            throw new ArgumentNullException(nameof(rootCounters));
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        Reset();
    }

    public uint CurrentScanline { get; private set; }

    public uint VideoCycleInScanline { get; private set; }

    public ulong FrameCount { get; private set; }

    public bool InHBlank { get; private set; }

    public bool InVBlank { get; private set; }

    public uint ScanlinesPerFrame =>
        _gpu.IsPalMode ? PalScanlinesPerFrame : NtscScanlinesPerFrame;

    public uint VideoCyclesPerScanline =>
        _gpu.IsPalMode
            ? PalVideoCyclesPerScanline
            : NtscVideoCyclesPerScanline;

    public event Action<ulong>? FrameCompleted;

    public event Action<uint>? VBlankStarted;

    public void Reset()
    {
        _videoClockRemainder = 0;
        _dotClockRemainder = 0;
        CurrentScanline = 0;
        VideoCycleInScanline = 0;
        FrameCount = 0;
        InHBlank = false;
        InVBlank = false;
        _lastPalMode = _gpu.IsPalMode;
        _lastDotClockDivisor = _gpu.DotClockDivisor;
        UpdateBlankSignals(requestVBlankInterrupt: false);
    }

    public void Tick(uint cycles)
    {
        if (cycles == 0)
            return;

        SynchronizeDisplayMode();

        uint videoClockHz =
            _gpu.IsPalMode ? PalVideoClockHz : NtscVideoClockHz;
        ulong scaledCycles =
            _videoClockRemainder + (ulong)cycles * videoClockHz;
        ulong videoCycles = scaledCycles / CpuClockHz;
        _videoClockRemainder = scaledCycles % CpuClockHz;

        if (videoCycles == 0)
            return;

        AdvanceRaster(videoCycles);
    }

    private void SynchronizeDisplayMode()
    {
        bool palMode = _gpu.IsPalMode;
        uint dotClockDivisor = _gpu.DotClockDivisor;

        if (palMode != _lastPalMode)
        {
            _lastPalMode = palMode;
            _videoClockRemainder = 0;
            CurrentScanline %= ScanlinesPerFrame;
            VideoCycleInScanline %= VideoCyclesPerScanline;
            UpdateBlankSignals(requestVBlankInterrupt: false);
        }

        if (dotClockDivisor != _lastDotClockDivisor)
        {
            _lastDotClockDivisor = dotClockDivisor;
            _dotClockRemainder = 0;
        }
    }

    private void AdvanceDotClock(ulong videoCycles)
    {
        ulong accumulated = _dotClockRemainder + videoCycles;
        ulong ticks = accumulated / _lastDotClockDivisor;
        _dotClockRemainder = accumulated % _lastDotClockDivisor;

        while (ticks != 0)
        {
            uint batch = (uint)Math.Min(ticks, uint.MaxValue);
            _rootCounters.TickDotClock(batch);
            ticks -= batch;
        }
    }

    private void AdvanceRaster(ulong videoCycles)
    {
        while (videoCycles != 0)
        {
            uint lineCycles = VideoCyclesPerScanline;
            uint nextBoundary = GetNextHorizontalBoundary(lineCycles);
            uint cyclesToBoundary = nextBoundary - VideoCycleInScanline;
            ulong consumed = Math.Min(videoCycles, cyclesToBoundary);

            AdvanceDotClock(consumed);
            VideoCycleInScanline += (uint)consumed;
            videoCycles -= consumed;

            if (VideoCycleInScanline == lineCycles)
                CompleteScanline();
            else if (consumed != 0)
                UpdateHorizontalBlank();

            if (consumed == 0)
            {
                AdvanceDotClock(1);
                UpdateHorizontalBlank();
                VideoCycleInScanline++;
                videoCycles--;

                if (VideoCycleInScanline == lineCycles)
                    CompleteScanline();
            }
        }
    }

    private uint GetNextHorizontalBoundary(uint lineCycles)
    {
        (uint displayStart, uint displayEnd) =
            GetHorizontalDisplayRange(lineCycles);
        uint nextBoundary = lineCycles;

        if (VideoCycleInScanline < displayStart)
            nextBoundary = Math.Min(nextBoundary, displayStart);

        if (VideoCycleInScanline < displayEnd)
            nextBoundary = Math.Min(nextBoundary, displayEnd);

        return nextBoundary;
    }

    private void CompleteScanline()
    {
        if (_gpu.IsPalMode &&
            _lastDotClockDivisor == 8 &&
            _dotClockRemainder != 0)
        {
            _rootCounters.TickDotClock(1);
        }

        _dotClockRemainder = 0;
        VideoCycleInScanline = 0;
        CurrentScanline++;

        if (CurrentScanline >= ScanlinesPerFrame)
        {
            CurrentScanline = 0;
            FrameCount++;
            FrameCompleted?.Invoke(FrameCount);
        }

        UpdateBlankSignals(requestVBlankInterrupt: true);
    }

    private void UpdateBlankSignals(bool requestVBlankInterrupt)
    {
        UpdateHorizontalBlank();

        bool wasInVBlank = InVBlank;
        InVBlank = IsVerticalBlank(CurrentScanline);
        _rootCounters.SetVBlank(InVBlank);

        if (requestVBlankInterrupt && !wasInVBlank && InVBlank)
        {
            _interruptController.Request(InterruptSource.VBlank);
            VBlankStarted?.Invoke(CurrentScanline);
        }

        _gpu.SetVideoTimingStatus(InVBlank, CurrentScanline, FrameCount);
    }

    private void UpdateHorizontalBlank()
    {
        (uint displayStart, uint displayEnd) =
            GetHorizontalDisplayRange(VideoCyclesPerScanline);
        bool hblank = displayStart >= displayEnd ||
            VideoCycleInScanline < displayStart ||
            VideoCycleInScanline >= displayEnd;

        if (hblank == InHBlank)
            return;

        InHBlank = hblank;
        _rootCounters.SetHBlank(InHBlank);
    }

    private (uint Start, uint End) GetHorizontalDisplayRange(uint lineCycles)
    {
        uint start = _gpu.HorizontalDisplayRange & 0x0FFF;
        uint end = (_gpu.HorizontalDisplayRange >> 12) & 0x0FFF;
        return (Math.Min(start, lineCycles), Math.Min(end, lineCycles));
    }

    private bool IsVerticalBlank(uint scanline)
    {
        uint totalLines = ScanlinesPerFrame;
        uint start = Math.Min(
            _gpu.VerticalDisplayRange & 0x03FF,
            totalLines);
        uint end = Math.Min(
            (_gpu.VerticalDisplayRange >> 10) & 0x03FF,
            totalLines);

        bool visible = start < end
            ? scanline >= start && scanline < end
            : start > end &&
              (scanline >= start || scanline < end);
        return !visible;
    }
}
