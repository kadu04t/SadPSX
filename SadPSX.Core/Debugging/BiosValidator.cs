using SadPSX.Core.Cpu;
using SadPSX.Core.Memory;

namespace SadPSX.Core.Debugging;

public static class BiosValidator
{
    public static BiosValidationResult Run(
        PsxMachine machine,
        ulong instructionLimit)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (instructionLimit == 0)
            throw new ArgumentOutOfRangeException(nameof(instructionLimit));

        ulong initialInstructions = machine.Cpu.Cycles;
        ulong initialClockCycles = machine.Cpu.ClockCycles;
        ulong initialMmioAccesses = machine.Bus.Mmio.TotalAccessCount;
        ulong initialUnhandledMmioAccesses = CountUnhandledMmioAccesses(machine);
        uint initialPc = machine.Cpu.Pc;
        var visitedProgramCounters = new HashSet<uint>();
        var exceptionCounts = new Dictionary<ExceptionCode, ulong>();
        string? failureType = null;
        string? failureMessage = null;

        void RecordException(CpuExceptionInfo exception)
        {
            exceptionCounts[exception.Code] =
                exceptionCounts.GetValueOrDefault(exception.Code) + 1;
        }

        machine.Cpu.ExceptionOccurred += RecordException;
        try
        {
            for (ulong index = 0; index < instructionLimit; index++)
            {
                visitedProgramCounters.Add(machine.Cpu.Pc);
                machine.Step();
            }
        }
        catch (Exception exception)
        {
            failureType = exception.GetType().Name;
            failureMessage = exception.Message;
        }
        finally
        {
            machine.Cpu.ExceptionOccurred -= RecordException;
        }

        ulong executedInstructions = machine.Cpu.Cycles - initialInstructions;
        ulong elapsedClockCycles = machine.Cpu.ClockCycles - initialClockCycles;
        ulong mmioAccesses =
            machine.Bus.Mmio.TotalAccessCount - initialMmioAccesses;
        ulong unhandledMmioAccesses =
            CountUnhandledMmioAccesses(machine) - initialUnhandledMmioAccesses;

        return new BiosValidationResult(
            instructionLimit,
            executedInstructions,
            elapsedClockCycles,
            initialPc,
            machine.Cpu.Pc,
            (ulong)visitedProgramCounters.Count,
            mmioAccesses,
            unhandledMmioAccesses,
            exceptionCounts,
            failureType,
            failureMessage);
    }

    private static ulong CountUnhandledMmioAccesses(PsxMachine machine)
    {
        ulong count = 0;

        foreach (MmioAccessSummary summary in machine.Bus.Mmio.AccessSummaries)
        {
            if (!summary.Handled)
                count += summary.Count;
        }

        return count;
    }
}

public sealed record BiosValidationResult(
    ulong RequestedInstructions,
    ulong ExecutedInstructions,
    ulong ElapsedClockCycles,
    uint InitialPc,
    uint FinalPc,
    ulong UniqueProgramCounters,
    ulong MmioAccesses,
    ulong UnhandledMmioAccesses,
    IReadOnlyDictionary<ExceptionCode, ulong> ExceptionCounts,
    string? FailureType,
    string? FailureMessage)
{
    public ulong UnexpectedExceptionCount =>
        Count(ExceptionCode.ReservedInstruction) +
        Count(ExceptionCode.CoprocessorUnusable) +
        Count(ExceptionCode.InstructionBusError) +
        Count(ExceptionCode.DataBusError);

    public bool Succeeded =>
        FailureType is null &&
        ExecutedInstructions == RequestedInstructions &&
        UnexpectedExceptionCount == 0;

    private ulong Count(ExceptionCode code) =>
        ExceptionCounts.GetValueOrDefault(code);
}
