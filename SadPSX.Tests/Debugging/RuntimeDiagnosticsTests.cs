using SadPSX.Core;
using SadPSX.Core.Cpu;
using SadPSX.Core.Debugging;
using Xunit;

namespace SadPSX.Tests.Debugging;

public sealed class RuntimeDiagnosticsTests
{
    [Fact]
    public void CaptureReportsCurrentExecutionAndHardwareState()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBios(0x2408_002A));
        using var diagnostics = new RuntimeDiagnostics(machine);

        RuntimeDiagnosticSnapshot before = diagnostics.Capture();
        machine.Step();
        RuntimeDiagnosticSnapshot after = diagnostics.Capture();

        Assert.Equal(0xBFC0_0000u, before.Pc);
        Assert.Equal(0x2408_002Au, before.RawInstruction);
        Assert.Contains("addiu", before.Disassembly);
        Assert.Equal(1ul, after.Instructions);
        Assert.True(after.ClockCycles > 0);
        Assert.Equal(42u, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void UnexpectedExceptionsAreCountedAndPublished()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBios(0xFC00_0000));
        using var diagnostics = new RuntimeDiagnostics(machine);
        CpuExceptionInfo? reported = null;
        diagnostics.UnexpectedExceptionOccurred += info => reported = info;

        machine.Step();

        Assert.Equal(
            1ul,
            diagnostics.ExceptionCounts[ExceptionCode.ReservedInstruction]);
        Assert.Equal(ExceptionCode.ReservedInstruction, reported?.Code);
        Assert.Single(diagnostics.RecentExceptions);
    }

    [Fact]
    public void CaptureListsUnhandledMmioAccesses()
    {
        var machine = new PsxMachine();
        using var diagnostics = new RuntimeDiagnostics(machine);

        machine.Bus.Read32(0x1F80_1050);
        RuntimeDiagnosticSnapshot snapshot = diagnostics.Capture();

        Assert.Equal(1ul, snapshot.UnhandledMmioAccesses);
        Assert.Single(diagnostics.CaptureUnhandledMmio());
    }

    private static byte[] CreateBios(params uint[] instructions)
    {
        var image = new byte[512 * 1024];
        for (int index = 0; index < instructions.Length; index++)
        {
            BitConverter.GetBytes(instructions[index])
                .CopyTo(image, index * sizeof(uint));
        }

        return image;
    }
}
