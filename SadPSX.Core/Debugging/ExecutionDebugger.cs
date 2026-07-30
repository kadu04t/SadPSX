using System.Text;
using SadPSX.Core.Bus;
using SadPSX.Core.Cpu;

namespace SadPSX.Core.Debugging;

public sealed class ExecutionDebugger : IDisposable
{
    private static readonly string[] RegisterNames =
    [
        "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
        "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
        "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
        "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra",
    ];

    private readonly PsxMachine _machine;
    private readonly Dictionary<uint, ulong> _pcVisitCounts = new();
    private readonly HashSet<uint> _reachedCheckpointAddresses = new();
    private MemoryAccess? _pendingMemoryBreakpoint;
    private bool _executingStep;
    private bool _memoryAccessSubscribed;
    private bool _disposed;

    public ExecutionDebugger(PsxMachine machine)
    {
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
    }

    public HashSet<uint> PcBreakpoints { get; } = new();
    public HashSet<uint> MemoryBreakpoints { get; } = new();
    public HashSet<uint> Checkpoints { get; } = new();
    public List<ExecutionCheckpoint> ReachedCheckpoints { get; } = new();
    public int? LoopVisitThreshold { get; set; }
    public bool DumpRegistersOnStop { get; set; } = true;
    public TextWriter? Output { get; set; }
    public DebuggerStop? StopReason { get; private set; }
    public bool IsStopped => StopReason is not null;

    public bool Step(Action stepAction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stepAction);

        if (IsStopped)
            return false;

        uint pc = _machine.Cpu.Pc;
        UpdateMemoryAccessSubscription();
        if (Checkpoints.Count != 0)
            RecordCheckpoint(pc);

        if (PcBreakpoints.Count != 0 && PcBreakpoints.Contains(pc))
        {
            Stop(DebuggerStopKind.PcBreakpoint, $"Breakpoint de PC em 0x{pc:X8}.");
            return false;
        }

        if (LoopVisitThreshold is int threshold &&
            threshold > 0)
        {
            ulong visits = _pcVisitCounts.GetValueOrDefault(pc) + 1;
            _pcVisitCounts[pc] = visits;
            if (visits >= (ulong)threshold)
            {
                Stop(
                    DebuggerStopKind.LoopDetected,
                    $"Possível loop: PC 0x{pc:X8} visitado {visits} vezes.");
                return false;
            }
        }

        _pendingMemoryBreakpoint = null;
        _executingStep = true;
        try
        {
            stepAction();
        }
        finally
        {
            _executingStep = false;
        }

        if (_pendingMemoryBreakpoint is MemoryAccess access)
        {
            Stop(
                DebuggerStopKind.MemoryBreakpoint,
                $"Breakpoint de memória em 0x{access.VirtualAddress:X8}.",
                access);
        }

        return true;
    }

    public ulong Run(ulong maxSteps, Action stepAction)
    {
        ulong executed = 0;

        while (executed < maxSteps && Step(stepAction))
        {
            executed++;

            if (IsStopped)
                break;
        }

        return executed;
    }

    public void Continue()
    {
        StopReason = null;
        _pendingMemoryBreakpoint = null;
    }

    public void ResetTracking()
    {
        StopReason = null;
        _pendingMemoryBreakpoint = null;
        _pcVisitCounts.Clear();
        _reachedCheckpointAddresses.Clear();
        ReachedCheckpoints.Clear();
    }

    public string FormatRegisters()
    {
        var output = new StringBuilder();
        R3000A cpu = _machine.Cpu;

        output.AppendLine(
            $"PC=0x{cpu.Pc:X8} NextPC=0x{cpu.NextPc:X8} " +
            $"HI=0x{cpu.Hi:X8} LO=0x{cpu.Lo:X8} " +
            $"Instructions={cpu.Cycles} Clock={cpu.ClockCycles}");

        for (int index = 0; index < RegisterNames.Length; index += 4)
        {
            for (int column = 0; column < 4; column++)
            {
                int registerIndex = index + column;
                output.Append(
                    $"${RegisterNames[registerIndex],-4}=0x{cpu.GetRegister(registerIndex):X8}");

                if (column < 3)
                    output.Append("  ");
            }

            output.AppendLine();
        }

        output.Append(
            $"SR=0x{cpu.Cop0.Sr:X8} CAUSE=0x{cpu.Cop0.Cause:X8} " +
            $"EPC=0x{cpu.Cop0.Epc:X8} BadVaddr=0x{cpu.Cop0.BadVaddr:X8}");

        return output.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_memoryAccessSubscribed)
            _machine.Bus.MemoryAccessed -= OnMemoryAccessed;
        _disposed = true;
    }

    private void RecordCheckpoint(uint pc)
    {
        if (!Checkpoints.Contains(pc) ||
            !_reachedCheckpointAddresses.Add(pc))
        {
            return;
        }

        var checkpoint = new ExecutionCheckpoint(
            pc,
            _machine.Cpu.Cycles,
            _machine.Cpu.ClockCycles,
            _machine.Bus.Mmio.TotalAccessCount);

        ReachedCheckpoints.Add(checkpoint);
        Output?.WriteLine(
            $"[checkpoint] PC=0x{checkpoint.Pc:X8} " +
            $"instrução={checkpoint.Cycle} clock={checkpoint.ClockCycle} " +
            $"mmio={checkpoint.MmioAccessCount}");
    }

    private void OnMemoryAccessed(MemoryAccess access)
    {
        if (!_executingStep ||
            access.Kind == MemoryAccessKind.InstructionFetch)
        {
            return;
        }

        if (MemoryBreakpoints.Contains(access.VirtualAddress) ||
            MemoryBreakpoints.Contains(access.PhysicalAddress))
        {
            _pendingMemoryBreakpoint ??= access;
        }
    }

    private void UpdateMemoryAccessSubscription()
    {
        bool shouldSubscribe = MemoryBreakpoints.Count != 0;
        if (shouldSubscribe == _memoryAccessSubscribed)
            return;

        if (shouldSubscribe)
            _machine.Bus.MemoryAccessed += OnMemoryAccessed;
        else
            _machine.Bus.MemoryAccessed -= OnMemoryAccessed;
        _memoryAccessSubscribed = shouldSubscribe;
    }

    private void Stop(
        DebuggerStopKind kind,
        string message,
        MemoryAccess? memoryAccess = null)
    {
        StopReason = new DebuggerStop(
            kind,
            message,
            _machine.Cpu.Pc,
            _machine.Cpu.Cycles,
            _machine.Cpu.ClockCycles,
            memoryAccess);

        if (Output is null)
            return;

        Output.WriteLine($"[debugger] {message}");

        if (DumpRegistersOnStop)
        {
            Output.WriteLine("[registradores]");
            Output.WriteLine(FormatRegisters());
        }
    }
}

public enum DebuggerStopKind
{
    PcBreakpoint,
    MemoryBreakpoint,
    LoopDetected,
}

public readonly record struct DebuggerStop(
    DebuggerStopKind Kind,
    string Message,
    uint Pc,
    ulong Cycle,
    ulong ClockCycle,
    MemoryAccess? MemoryAccess);

public readonly record struct ExecutionCheckpoint(
    uint Pc,
    ulong Cycle,
    ulong ClockCycle,
    ulong MmioAccessCount);
