using SadPSX.Core;
using SadPSX.Core.Bios;
using SadPSX.Core.Cpu;
using SadPSX.Core.Interrupts;
using SadPSX.Core.Timers;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Cpu;

public sealed class InterruptTests
{
    [Fact]
    public void EnabledHardwareInterruptJumpsToExceptionVector()
    {
        var (bus, cpu) = CreateCpu();
        EnableTimer0Interrupt(bus, cpu);

        cpu.Step();

        Assert.Equal(0x8000_0080u, cpu.Pc);
        Assert.Equal(0u, cpu.Cop0.Epc);
        Assert.Equal(
            (uint)ExceptionCode.Interrupt,
            (cpu.Cop0.Cause >> 2) & 0x1F);
        Assert.NotEqual(0u, cpu.Cop0.Cause & (1u << 10));
    }

    [Fact]
    public void MaskedHardwareInterruptDoesNotReachCop0()
    {
        var (bus, cpu) = CreateCpu();
        bus.InterruptController.Request(InterruptSource.Timer0);
        cpu.Cop0.Sr = (1u << 10) | 1;

        cpu.Step();

        Assert.Equal(4u, cpu.Pc);
        Assert.Equal(0u, cpu.Cop0.Cause & (1u << 10));
    }

    [Fact]
    public void AcknowledgingLastSourceClearsCop0HardwarePendingBit()
    {
        var (bus, cpu) = CreateCpu();
        bus.InterruptController.Request(InterruptSource.Timer0);
        bus.Write16(
            InterruptController.MaskAddress,
            1 << (int)InterruptSource.Timer0);
        cpu.Cop0.Sr = 1u << 10;

        cpu.Step();
        Assert.NotEqual(0u, cpu.Cop0.Cause & (1u << 10));

        bus.Write16(
            InterruptController.StatusAddress,
            unchecked((ushort)~(1 << (int)InterruptSource.Timer0)));
        cpu.Step();

        Assert.Equal(0u, cpu.Cop0.Cause & (1u << 10));
    }

    [Fact]
    public void InterruptWaitsUntilBranchDelaySlotFinishes()
    {
        var (bus, cpu) = CreateCpu();
        bus.Write32(0, 0x1000_0001);
        bus.Write32(4, 0x2408_0007);
        bus.Write32(8, 0);
        cpu.Step();
        EnableTimer0Interrupt(bus, cpu);

        cpu.Step();

        Assert.Equal(7u, cpu.GetRegister(8));
        Assert.Equal(8u, cpu.Pc);

        cpu.Step();

        Assert.Equal(0x8000_0080u, cpu.Pc);
        Assert.Equal(8u, cpu.Cop0.Epc);
    }

    [Fact]
    public void SoftwareInterruptUsesCauseAndStatusMasks()
    {
        var (_, cpu) = CreateCpu();
        cpu.Cop0.TryWriteRegister(13, 1u << 8);
        cpu.Cop0.Sr = (1u << 8) | 1;

        cpu.Step();

        Assert.Equal(0x8000_0080u, cpu.Pc);
        Assert.Equal(
            (uint)ExceptionCode.Interrupt,
            (cpu.Cop0.Cause >> 2) & 0x1F);
    }

    [Fact]
    public void TimerInterruptReachesCpuOnFollowingMachineStep()
    {
        var machine = new PsxMachine();
        machine.LoadBios(new byte[BiosRom.SizeInBytes]);
        machine.Bus.Write16(
            RootCounters.Timer0BaseAddress + 8,
            1);
        machine.Bus.Write16(
            RootCounters.Timer0BaseAddress + 4,
            (1 << 3) | (1 << 4));
        machine.Bus.Write16(
            InterruptController.MaskAddress,
            1 << (int)InterruptSource.Timer0);
        machine.Cpu.Cop0.Sr = (1u << 10) | 1;

        machine.Step();
        machine.Step();

        Assert.Equal(0x8000_0080u, machine.Cpu.Pc);
        Assert.Equal(0xBFC0_0004u, machine.Cpu.Cop0.Epc);
        Assert.Equal(
            (uint)ExceptionCode.Interrupt,
            (machine.Cpu.Cop0.Cause >> 2) & 0x1F);
    }

    private static (Bus Bus, R3000A Cpu) CreateCpu()
    {
        var bus = new Bus();
        bus.Write32(0, 0);
        bus.Write32(4, 0);
        var cpu = new R3000A(bus);
        cpu.Reset(0);
        return (bus, cpu);
    }

    private static void EnableTimer0Interrupt(Bus bus, R3000A cpu)
    {
        bus.InterruptController.Request(InterruptSource.Timer0);
        bus.Write16(
            InterruptController.MaskAddress,
            1 << (int)InterruptSource.Timer0);
        cpu.Cop0.Sr = (1u << 10) | 1;
    }
}
