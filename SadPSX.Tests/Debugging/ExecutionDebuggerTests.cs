using SadPSX.Core;
using SadPSX.Core.Debugging;
using SadPSX.Core.Memory;
using Xunit;

namespace SadPSX.Tests.Debugging;

public sealed class ExecutionDebuggerTests
{
    [Fact]
    public void PcBreakpointStopsBeforeInstructionExecutes()
    {
        var machine = CreateMachine();
        machine.Bus.Write32(0, 0x2408_002A); // addiu $t0, $zero, 42

        using var debugger = new ExecutionDebugger(machine);
        debugger.PcBreakpoints.Add(0);

        bool executed = debugger.Step(machine.Step);

        Assert.False(executed);
        Assert.Equal(DebuggerStopKind.PcBreakpoint, debugger.StopReason?.Kind);
        Assert.Equal(0u, machine.Cpu.GetRegister(8));
        Assert.Equal(0ul, machine.Cpu.Cycles);
    }

    [Fact]
    public void MemoryBreakpointStopsAfterMatchingDataAccess()
    {
        var machine = CreateMachine();
        machine.Cpu.Execute(new(0x2408_002A)); // addiu $t0, $zero, 42
        machine.Bus.Write32(0, 0xAC08_0100); // sw $t0, 0x100($zero)

        using var debugger = new ExecutionDebugger(machine);
        debugger.MemoryBreakpoints.Add(0x100);

        bool executed = debugger.Step(machine.Step);

        Assert.True(executed);
        Assert.Equal(DebuggerStopKind.MemoryBreakpoint, debugger.StopReason?.Kind);
        Assert.Equal(42u, machine.Bus.Read32(0x100));
    }

    [Fact]
    public void InstructionFetchDoesNotTriggerMemoryBreakpoint()
    {
        var machine = CreateMachine();
        machine.Bus.Write32(0, 0);

        using var debugger = new ExecutionDebugger(machine);
        debugger.MemoryBreakpoints.Add(0);

        bool executed = debugger.Step(machine.Step);

        Assert.True(executed);
        Assert.Null(debugger.StopReason);
    }

    [Fact]
    public void CheckpointRecordsCycleAndMmioCountOnlyOnce()
    {
        var machine = CreateMachine();
        machine.Bus.Write32(0, 0);

        using var debugger = new ExecutionDebugger(machine);
        debugger.Checkpoints.Add(0);

        debugger.Step(machine.Step);
        machine.Cpu.Reset(0);
        debugger.Step(machine.Step);

        ExecutionCheckpoint checkpoint = Assert.Single(debugger.ReachedCheckpoints);
        Assert.Equal(0u, checkpoint.Pc);
        Assert.Equal(0ul, checkpoint.Cycle);
        Assert.Equal(0ul, checkpoint.MmioAccessCount);
    }

    [Fact]
    public void LoopDetectorStopsWhenPcVisitThresholdIsReached()
    {
        var machine = CreateMachine();
        machine.Bus.Write32(0x00, 0x0800_0000); // j 0
        machine.Bus.Write32(0x04, 0x0000_0000); // nop

        using var debugger = new ExecutionDebugger(machine)
        {
            LoopVisitThreshold = 2,
        };

        ulong executed = debugger.Run(10, machine.Step);

        Assert.Equal(2ul, executed);
        Assert.Equal(DebuggerStopKind.LoopDetected, debugger.StopReason?.Kind);
        Assert.Equal(0u, machine.Cpu.Pc);
    }

    [Fact]
    public void RegisterDumpContainsCpuAndCop0State()
    {
        var machine = CreateMachine();
        machine.Cpu.Execute(new(0x2408_002A)); // addiu $t0, $zero, 42

        using var debugger = new ExecutionDebugger(machine);
        string dump = debugger.FormatRegisters();

        Assert.Contains("$t0  =0x0000002A", dump);
        Assert.Contains("PC=0x00000000", dump);
        Assert.Contains("CAUSE=0x00000000", dump);
        Assert.Contains("BadVaddr=0x00000000", dump);
    }

    private static PsxMachine CreateMachine()
    {
        var machine = new PsxMachine(new Bus());
        machine.Cpu.Reset(0);
        return machine;
    }
}
