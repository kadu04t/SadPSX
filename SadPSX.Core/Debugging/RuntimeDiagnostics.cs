using SadPSX.Core.Bus;
using SadPSX.Core.Controllers;
using SadPSX.Core.Cpu;

namespace SadPSX.Core.Debugging;

public sealed class RuntimeDiagnostics : IDisposable
{
    private const int MaximumRecentExceptions = 16;

    private readonly PsxMachine _machine;
    private readonly Dictionary<ExceptionCode, ulong> _exceptionCounts = new();
    private readonly Queue<CpuExceptionInfo> _recentExceptions = new();
    private bool _disposed;

    public RuntimeDiagnostics(PsxMachine machine)
    {
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        _machine.Cpu.ExceptionOccurred += OnExceptionOccurred;
    }

    public event Action<CpuExceptionInfo>? UnexpectedExceptionOccurred;

    public IReadOnlyDictionary<ExceptionCode, ulong> ExceptionCounts =>
        _exceptionCounts;

    public IReadOnlyCollection<CpuExceptionInfo> RecentExceptions =>
        _recentExceptions;

    public RuntimeDiagnosticSnapshot Capture()
    {
        uint pc = _machine.Cpu.Pc;
        uint rawInstruction = 0;
        string disassembly;
        try
        {
            rawInstruction = _machine.Bus.Peek32(pc);
            disassembly = Disassembler.Disassemble(
                new Instruction(rawInstruction),
                pc);
        }
        catch (InvalidOperationException)
        {
            disassembly = "<endereço de instrução não mapeado>";
        }

        ulong unhandledMmioAccesses = _machine.Bus.Mmio.AccessSummaries
            .Where(summary => !summary.Handled)
            .Aggregate(0ul, (total, summary) => total + summary.Count);

        return new RuntimeDiagnosticSnapshot(
            pc,
            _machine.Cpu.NextPc,
            rawInstruction,
            disassembly,
            _machine.Cpu.Cycles,
            _machine.Cpu.ClockCycles,
            _machine.Bus.VideoTiming.FrameCount,
            _machine.Bus.VideoTiming.CurrentScanline,
            _machine.Bus.Gpu.Status,
            _machine.Bus.Gpu.Gp0CommandCount,
            _machine.Bus.Gpu.Gp1CommandCount,
            _machine.Bus.InterruptController.Status,
            _machine.Bus.InterruptController.Mask,
            _machine.Bus.CdRom.HasDisc,
            _machine.Bus.CdRom.InterruptFlags,
            _machine.Bus.CdRom.InterruptEnable,
            _machine.Bus.CdRom.Mode,
            _machine.Bus.CdRom.DataCount,
            _machine.Bus.CdRom.ResultCount,
            _machine.Bus.CdRom.BufferedSectorCount,
            _machine.Bus.CdRom.LastCommand,
            _machine.Bus.CdRom.CommandCount,
            _machine.Bus.CdRom.CommandBusy,
            _machine.Bus.CdRom.CurrentLogicalBlockAddress,
            _machine.Bus.CdRom.IsReading,
            _machine.Bus.CdRom.IsPlaying,
            _machine.Bus.CdRom.IsSeeking,
            _machine.Bus.Dma.CompletedTransfers,
            _machine.Bus.PostStatus.Value,
            _machine.Bus.PostStatus.WriteCount,
            _machine.Bus.Sio0.Control,
            _machine.Bus.Sio0.ReceiveCount,
            unhandledMmioAccesses,
            _machine.Bus.Mmio.LastUnhandledReadAddress,
            _machine.Bus.Mmio.LastUnhandledWriteAddress);
    }

    public uint[] CaptureRegisters()
    {
        var registers = new uint[32];
        for (int index = 0; index < registers.Length; index++)
            registers[index] = _machine.Cpu.GetRegister(index);
        return registers;
    }

    public IReadOnlyList<MmioAccessSummary> CaptureUnhandledMmio() =>
        _machine.Bus.Mmio.AccessSummaries
            .Where(summary => !summary.Handled)
            .OrderByDescending(summary => summary.Count)
            .ThenBy(summary => summary.Address)
            .ToArray();

    public IReadOnlyList<Sio0TransferTrace> CaptureSio0Transfers() =>
        _machine.Bus.Sio0.TransferHistory.ToArray();

    public void Clear()
    {
        _exceptionCounts.Clear();
        _recentExceptions.Clear();
        _machine.Bus.Sio0.ClearTransferHistory();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _machine.Cpu.ExceptionOccurred -= OnExceptionOccurred;
        _disposed = true;
    }

    private void OnExceptionOccurred(CpuExceptionInfo exception)
    {
        _exceptionCounts.TryGetValue(exception.Code, out ulong count);
        _exceptionCounts[exception.Code] = count + 1;

        if (_recentExceptions.Count == MaximumRecentExceptions)
            _recentExceptions.Dequeue();
        _recentExceptions.Enqueue(exception);

        if (exception.Code is not ExceptionCode.Interrupt and
            not ExceptionCode.Syscall)
        {
            UnexpectedExceptionOccurred?.Invoke(exception);
        }
    }
}

public readonly record struct RuntimeDiagnosticSnapshot(
    uint Pc,
    uint NextPc,
    uint RawInstruction,
    string Disassembly,
    ulong Instructions,
    ulong ClockCycles,
    ulong VideoFrames,
    uint Scanline,
    uint GpuStatus,
    ulong Gp0Commands,
    ulong Gp1Commands,
    ushort InterruptStatus,
    ushort InterruptMask,
    bool HasDisc,
    byte CdRomInterruptFlags,
    byte CdRomInterruptEnable,
    byte CdRomMode,
    int CdRomDataBytes,
    int CdRomResultBytes,
    int CdRomBufferedSectors,
    byte CdRomLastCommand,
    ulong CdRomCommands,
    bool CdRomCommandBusy,
    int CdRomLogicalBlockAddress,
    bool CdRomReading,
    bool CdRomPlaying,
    bool CdRomSeeking,
    ulong DmaTransfers,
    byte PostStatus,
    ulong PostWriteCount,
    ushort Sio0Control,
    int Sio0ReceiveBytes,
    ulong UnhandledMmioAccesses,
    uint? LastUnhandledMmioRead,
    uint? LastUnhandledMmioWrite);
