using SadPSX.Core;
using SadPSX.Core.Bios;
using SadPSX.Core.Bus;
using SadPSX.Core.Cpu;
using SadPSX.Core.Debugging;
using Xunit;

namespace SadPSX.Tests.Debugging;

public sealed class BiosValidatorTests
{
    [Fact]
    public void RunCompletesStableBiosAndReportsExecutionMetrics()
    {
        var machine = CreateMachineWithBios(
            0,
            0,
            0);

        BiosValidationResult result = BiosValidator.Run(machine, 3);

        Assert.True(result.Succeeded);
        Assert.Equal(3ul, result.ExecutedInstructions);
        Assert.Equal(87ul, result.ElapsedClockCycles);
        Assert.Equal(3ul, result.UniqueProgramCounters);
        Assert.Empty(result.ExceptionCounts);
    }

    [Fact]
    public void RunCountsEmulatedExceptionsWithoutTreatingSyscallAsFailure()
    {
        var machine = CreateMachineWithBios(0x0000_000C);
        machine.Bus.Write32(0x80, 0);

        BiosValidationResult result = BiosValidator.Run(machine, 2);

        Assert.True(result.Succeeded);
        Assert.Equal(1ul, result.ExceptionCounts[ExceptionCode.Syscall]);
        Assert.Equal(0ul, result.UnexpectedExceptionCount);
    }

    [Fact]
    public void RunRejectsReservedInstructions()
    {
        var machine = CreateMachineWithBios(0xFC00_0000);

        BiosValidationResult result = BiosValidator.Run(machine, 1);

        Assert.False(result.Succeeded);
        Assert.Equal(
            1ul,
            result.ExceptionCounts[ExceptionCode.ReservedInstruction]);
        Assert.Equal(1ul, result.UnexpectedExceptionCount);
    }

    [Fact]
    public void RunReportsUnhandledMmioAccesses()
    {
        var machine = CreateMachineWithBios(
            0x3C08_1F80,
            0x8D09_1080);

        BiosValidationResult result = BiosValidator.Run(machine, 2);

        Assert.True(result.Succeeded);
        Assert.Equal(1ul, result.MmioAccesses);
        Assert.Equal(1ul, result.UnhandledMmioAccesses);
    }

    [Fact]
    public void RunConvertsHostExceptionsIntoFailedResult()
    {
        var machine = CreateMachineWithBios(0);
        machine.RegisterClockedDevice(new FailingClockedDevice());

        BiosValidationResult result = BiosValidator.Run(machine, 1);

        Assert.False(result.Succeeded);
        Assert.Equal(1ul, result.ExecutedInstructions);
        Assert.Equal(nameof(InvalidOperationException), result.FailureType);
        Assert.Equal("falha simulada", result.FailureMessage);
    }

    private static PsxMachine CreateMachineWithBios(params uint[] instructions)
    {
        var image = new byte[BiosRom.SizeInBytes];

        for (int index = 0; index < instructions.Length; index++)
        {
            uint instruction = instructions[index];
            int offset = index * 4;
            image[offset] = (byte)instruction;
            image[offset + 1] = (byte)(instruction >> 8);
            image[offset + 2] = (byte)(instruction >> 16);
            image[offset + 3] = (byte)(instruction >> 24);
        }

        var machine = new PsxMachine();
        machine.LoadBios(image);
        return machine;
    }

    private sealed class FailingClockedDevice : IClockedDevice
    {
        public void Tick(uint cycles)
        {
            throw new InvalidOperationException("falha simulada");
        }
    }
}
