using SadPSX.Core;
using SadPSX.Core.Cpu;
using SadPSX.Core.Memory;
using Xunit;

namespace SadPSX.Tests.Cpu;

public sealed class StepTests
{
    private const uint TestProgramAddress = 0x0000_0000;

    [Fact]
    public void CpuStartsAtPlayStationResetVector()
    {
        var (_, cpu) = CreateCpu();

        Assert.Equal(R3000A.ResetVector, cpu.Pc);
        Assert.Equal(R3000A.ResetVector + 4, cpu.NextPc);
        Assert.Equal(0ul, cpu.Cycles);
    }

    [Fact]
    public void StepFetchesAndExecutesInstructionFromBus()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);

        // lui $t0, 0x1234
        bus.Write32(
            TestProgramAddress,
            0x3C08_1234);

        cpu.Step();

        Assert.Equal(
            0x1234_0000u,
            cpu.GetRegister(8));
    }

    [Fact]
    public void StepAdvancesPcAndNextPc()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);

        // nop
        bus.Write32(
            TestProgramAddress,
            0x0000_0000);

        cpu.Step();

        Assert.Equal(
            0x0000_0004u,
            cpu.Pc);

        Assert.Equal(
            0x0000_0008u,
            cpu.NextPc);
    }

    [Fact]
    public void StepIncrementsCycleCounter()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);

        // Dois NOPs consecutivos.
        bus.Write32(
            TestProgramAddress,
            0x0000_0000);

        bus.Write32(
            TestProgramAddress + 4,
            0x0000_0000);

        cpu.Step();
        cpu.Step();

        Assert.Equal(
            2ul,
            cpu.Cycles);
    }

    [Fact]
    public void CachedRamInstructionCostsOneClockCycle()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);
        bus.Write32(TestProgramAddress, 0x0000_0000);

        cpu.Step();

        Assert.Equal(1u, cpu.LastStepCycles);
        Assert.Equal(1ul, cpu.ClockCycles);
    }

    [Fact]
    public void BiosInstructionFetchIsSlowerThanCachedRam()
    {
        var machine = new PsxMachine();
        machine.LoadBios(new byte[BiosRom.SizeInBytes]);

        machine.Step();

        Assert.Equal(29u, machine.Cpu.LastStepCycles);
        Assert.Equal(29ul, machine.ClockCycles);
    }

    [Fact]
    public void DataLoadAddsMemoryLatencyToInstructionFetch()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);
        bus.Write32(TestProgramAddress, 0x8C08_0100); // lw $t0, 0x100($zero)
        bus.Write32(0x100, 0xDEAD_BEEF);

        cpu.Step();

        Assert.Equal(8u, cpu.LastStepCycles);
        Assert.Equal(8ul, cpu.ClockCycles);
    }

    [Fact]
    public void ConsecutiveStepsExecuteSequentialInstructions()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);

        // lui $t0, 0x1234
        bus.Write32(
            TestProgramAddress,
            0x3C08_1234);

        // ori $t0, $t0, 0x5678
        bus.Write32(
            TestProgramAddress + 4,
            0x3508_5678);

        cpu.Step();
        cpu.Step();

        Assert.Equal(
            0x1234_5678u,
            cpu.GetRegister(8));

        Assert.Equal(
            0x0000_0008u,
            cpu.Pc);

        Assert.Equal(
            0x0000_000Cu,
            cpu.NextPc);

        Assert.Equal(
            2ul,
            cpu.Cycles);
    }

    [Fact]
    public void StepCanExecuteProgramThatWritesAndReadsMemory()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);

        /*
         * Programa:
         *
         * lui   $t0, 0x1234
         * ori   $t0, $t0, 0x5678
         * sw    $t0, 0x0100($zero)
         * lw    $t1, 0x0100($zero)
         * nop
         */

        bus.Write32(
            TestProgramAddress + 0x00,
            0x3C08_1234);

        bus.Write32(
            TestProgramAddress + 0x04,
            0x3508_5678);

        bus.Write32(
            TestProgramAddress + 0x08,
            0xAC08_0100);

        bus.Write32(
            TestProgramAddress + 0x0C,
            0x8C09_0100);

        bus.Write32(
            TestProgramAddress + 0x10,
            0x0000_0000);

        cpu.Step();
        cpu.Step();
        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(
            0x1234_5678u,
            bus.Read32(0x0000_0100));

        Assert.Equal(
            0x1234_5678u,
            cpu.GetRegister(9));

        Assert.Equal(
            5ul,
            cpu.Cycles);

        Assert.Equal(
            0x0000_0014u,
            cpu.Pc);
    }

    [Fact]
    public void ResetClearsRegistersAndExecutionState()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);

        // addiu $t0, $zero, 5
        bus.Write32(
            TestProgramAddress,
            0x2408_0005);

        cpu.Step();

        Assert.Equal(
            5u,
            cpu.GetRegister(8));

        Assert.Equal(
            1ul,
            cpu.Cycles);

        cpu.Reset();

        Assert.Equal(
            0u,
            cpu.GetRegister(8));

        Assert.Equal(
            R3000A.ResetVector,
            cpu.Pc);

        Assert.Equal(
            R3000A.ResetVector + 4,
            cpu.NextPc);

        Assert.Equal(
            0u,
            cpu.Hi);

        Assert.Equal(
            0u,
            cpu.Lo);

        Assert.Equal(
            0ul,
            cpu.Cycles);

        Assert.Equal(
            0ul,
            cpu.ClockCycles);

        Assert.Equal(
            0u,
            cpu.LastStepCycles);
    }

    [Fact]
    public void ResetCanUseCustomProgramCounter()
    {
        var (_, cpu) = CreateCpu();

        cpu.Reset(0x0000_1000);

        Assert.Equal(
            0x0000_1000u,
            cpu.Pc);

        Assert.Equal(
            0x0000_1004u,
            cpu.NextPc);

        Assert.Equal(
            0ul,
            cpu.Cycles);
    }

    [Fact]
    public void ResetDoesNotEraseRamContents()
    {
        var (bus, cpu) = CreateCpu(TestProgramAddress);

        bus.Write32(
            0x0000_0100,
            0x1234_5678);

        cpu.Reset();

        Assert.Equal(
            0x1234_5678u,
            bus.Read32(0x0000_0100));
    }

    private static (Bus Bus, R3000A Cpu) CreateCpu(
        uint? initialPc = null)
    {
        var ram = new Ram();
        var bus = new Bus(ram);
        var cpu = new R3000A(bus);

        if (initialPc.HasValue)
            cpu.Reset(initialPc.Value);

        return (bus, cpu);
    }
}
