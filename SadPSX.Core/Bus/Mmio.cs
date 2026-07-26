using SadPSX.Core.Bios;
using SadPSX.Core.CdRom;
using SadPSX.Core.Controllers;
using SadPSX.Core.Dma;
using SadPSX.Core.Interrupts;
using SadPSX.Core.Memory;
using SadPSX.Core.Timers;
using GpuDevice = SadPSX.Core.Gpu.Gpu;
using GpuVideoTiming = SadPSX.Core.Gpu.VideoTiming;
using MdecDevice = SadPSX.Core.Mdec.Mdec;
using SpuDevice = SadPSX.Core.Spu.Spu;

namespace SadPSX.Core.Bus;

public sealed class Mmio
{
    private readonly IMmioDevice[] _devices;
    private readonly List<MmioAccess> _accessLog = new();
    private readonly Dictionary<MmioAccessKey, MmioAccessSummary> _accessSummaries = new();
    private ulong _accessSequence;

    public Mmio()
        : this(new MemoryControl(), new InterruptController())
    {
    }

    public Mmio(MemoryControl memoryControl)
        : this(memoryControl, new InterruptController())
    {
    }

    public Mmio(
        MemoryControl memoryControl,
        InterruptController interruptController)
    {
        MemoryControl = memoryControl ?? throw new ArgumentNullException(nameof(memoryControl));
        InterruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        RootCounters = new RootCounters(InterruptController);
        Sio0 = new Sio0(InterruptController);
        PostStatus = new PostStatusRegister();
        CdRom = new CdRomController(InterruptController);
        Spu = new SpuDevice();
        Gpu = new GpuDevice(InterruptController);
        Mdec = new MdecDevice();
        VideoTiming = new GpuVideoTiming(
            Gpu,
            RootCounters,
            InterruptController);
        Dma = new DmaController(
            InterruptController,
            Mdec,
            Gpu,
            CdRom,
            Spu);
        _devices =
        [
            MemoryControl,
            InterruptController,
            Sio0,
            Mdec,
            Dma,
            RootCounters,
            CdRom,
            Spu,
            Gpu,
            PostStatus,
        ];
    }

    public MemoryControl MemoryControl { get; }
    public InterruptController InterruptController { get; }
    public RootCounters RootCounters { get; }
    public Sio0 Sio0 { get; }
    public PostStatusRegister PostStatus { get; }
    public CdRomController CdRom { get; }
    public SpuDevice Spu { get; }
    public GpuDevice Gpu { get; }
    public MdecDevice Mdec { get; }
    public GpuVideoTiming VideoTiming { get; }
    public DmaController Dma { get; }
    public uint? LastUnhandledReadAddress { get; private set; }
    public uint? LastUnhandledWriteAddress { get; private set; }
    public int MaxLoggedAccesses { get; set; } = 256;
    public ulong TotalAccessCount => _accessSequence;
    public IReadOnlyList<MmioAccess> AccessLog => _accessLog;
    public IReadOnlyCollection<MmioAccessSummary> AccessSummaries => _accessSummaries.Values;

    public event Action<MmioAccess>? Accessed;

    public byte Read8(uint address)
    {
        IMmioDevice? device = FindDevice(address);
        bool handled = device is not null;
        byte value = device?.Read8(address) ?? 0;

        if (!handled)
            LastUnhandledReadAddress = address;

        Record(address, MemoryAccessKind.Read, 1, value, handled);
        return value;
    }

    public void Write8(uint address, byte value)
    {
        IMmioDevice? device = FindDevice(address);
        bool handled = device is not null;

        if (handled)
            device!.Write8(address, value);
        else
            LastUnhandledWriteAddress = address;

        Record(address, MemoryAccessKind.Write, 1, value, handled);
    }

    public ushort Read16(uint address)
    {
        IMmioDevice? device = FindDevice(address);
        bool handled = device is not null;
        ushort value = device?.Read16(address) ?? 0;

        if (!handled)
            LastUnhandledReadAddress = address;

        Record(address, MemoryAccessKind.Read, 2, value, handled);
        return value;
    }

    public void Write16(uint address, ushort value)
    {
        IMmioDevice? device = FindDevice(address);
        bool handled = device is not null;

        if (handled)
            device!.Write16(address, value);
        else
            LastUnhandledWriteAddress = address;

        Record(address, MemoryAccessKind.Write, 2, value, handled);
    }

    public uint Read32(uint address)
    {
        IMmioDevice? device = FindDevice(address);
        bool handled = device is not null;
        uint value = device?.Read32(address) ?? 0;

        if (!handled)
            LastUnhandledReadAddress = address;

        Record(address, MemoryAccessKind.Read, 4, value, handled);
        return value;
    }

    public uint Peek32(uint address)
    {
        return FindDevice(address)?.Peek32(address) ?? 0;
    }

    public void Write32(uint address, uint value)
    {
        IMmioDevice? device = FindDevice(address);
        bool handled = device is not null;

        if (handled)
            device!.Write32(address, value);
        else
            LastUnhandledWriteAddress = address;

        Record(address, MemoryAccessKind.Write, 4, value, handled);
    }

    private IMmioDevice? FindDevice(uint address)
    {
        foreach (IMmioDevice device in _devices)
        {
            if (device.Handles(address))
                return device;
        }

        return null;
    }

    public void ClearAccessLog()
    {
        _accessLog.Clear();
        _accessSummaries.Clear();
        _accessSequence = 0;
        LastUnhandledReadAddress = null;
        LastUnhandledWriteAddress = null;
    }

    private void Record(
        uint address,
        MemoryAccessKind kind,
        int width,
        uint value,
        bool handled)
    {
        _accessSequence++;
        string registerName = FindDevice(address)?.GetRegisterName(address) ??
            "UNHANDLED";

        var access = new MmioAccess(
            _accessSequence,
            address,
            kind,
            width,
            value,
            handled,
            registerName);

        if (_accessLog.Count < MaxLoggedAccesses)
            _accessLog.Add(access);

        var key = new MmioAccessKey(address, kind, width, handled);
        if (!_accessSummaries.TryGetValue(key, out MmioAccessSummary? summary))
        {
            summary = new MmioAccessSummary(
                address,
                kind,
                width,
                handled,
                registerName,
                _accessSequence);
            _accessSummaries.Add(key, summary);
        }

        summary.Record(_accessSequence, value);
        Accessed?.Invoke(access);
    }
}

public readonly record struct MmioAccess(
    ulong Sequence,
    uint Address,
    MemoryAccessKind Kind,
    int Width,
    uint Value,
    bool Handled,
    string RegisterName)
{
    public override string ToString()
    {
        string operation = Kind == MemoryAccessKind.Write ? "W" : "R";
        return $"#{Sequence,6} {operation}{Width * 8,-2} 0x{Address:X8} = 0x{Value:X8} " +
               $"{RegisterName}{(Handled ? string.Empty : " (não tratado)")}";
    }
}

public sealed class MmioAccessSummary
{
    internal MmioAccessSummary(
        uint address,
        MemoryAccessKind kind,
        int width,
        bool handled,
        string registerName,
        ulong firstSequence)
    {
        Address = address;
        Kind = kind;
        Width = width;
        Handled = handled;
        RegisterName = registerName;
        FirstSequence = firstSequence;
    }

    public uint Address { get; }
    public MemoryAccessKind Kind { get; }
    public int Width { get; }
    public bool Handled { get; }
    public string RegisterName { get; }
    public ulong FirstSequence { get; }
    public ulong LastSequence { get; private set; }
    public ulong Count { get; private set; }
    public uint LastValue { get; private set; }

    internal void Record(ulong sequence, uint value)
    {
        LastSequence = sequence;
        LastValue = value;
        Count++;
    }
}

internal readonly record struct MmioAccessKey(
    uint Address,
    MemoryAccessKind Kind,
    int Width,
    bool Handled);
