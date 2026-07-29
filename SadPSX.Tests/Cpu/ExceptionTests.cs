using SadPSX.Core.Cpu;
using SadPSX.Core.Memory;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Cpu;

public sealed class ExceptionTests
{
    [Fact]
    public void SyscallSetsEpcCauseAndJumpsToRamVector()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        // syscall
        cpu.Execute(new Instruction(0x0000_000C));

        Assert.Equal(0x1000u, cpu.Cop0.Epc);

        uint excCode = (cpu.Cop0.Cause >> 2) & 0x1F;
        Assert.Equal(0x08u, excCode); // ExceptionCode.Syscall

        uint bd = (cpu.Cop0.Cause >> 31) & 1;
        Assert.Equal(0u, bd); // não estava em delay slot

        // SR.BEV=0 por padrão após Reset -> vetor na RAM
        Assert.Equal(0x8000_0080u, cpu.Pc);
        Assert.Equal(0x8000_0084u, cpu.NextPc);
    }

    [Fact]
    public void BreakSetsBreakpointExceptionCode()
    {
        var cpu = new R3000A();
        cpu.Reset(0x2000);
        CpuExceptionInfo? exception = null;
        cpu.ExceptionOccurred += info => exception = info;
        uint instruction = (0x12345u << 6) | 0x0D;

        cpu.Execute(new Instruction(instruction));

        uint excCode = (cpu.Cop0.Cause >> 2) & 0x1F;
        Assert.Equal(0x09u, excCode); // ExceptionCode.Breakpoint
        Assert.Equal(0x2000u, cpu.Cop0.Epc);
        Assert.Equal(instruction, exception?.RawInstruction);
        Assert.Equal(0x12345u, exception?.BreakCode);
    }

    [Fact]
    public void SrBootExceptionVectorsBitSelectsRomVector()
    {
        var cpu = new R3000A();
        cpu.Reset(0x3000);

        // mtc0 $t0, $12 (SR) -> primeiro carregamos o bit BEV (bit 22) em $t0.
        // BEV = 1 << 22 = 0x0040_0000; via LUI, isso é o imediato 0x0040
        // deslocado para os 16 bits altos.
        cpu.Execute(new Instruction(0x3C08_0040)); // lui $t0, 0x0040 -> t0 = 0x00400000

        // mtc0 $t0, $12 (SR)
        cpu.Execute(new Instruction(0x4088_6000));

        // syscall -> agora deve usar o vetor da ROM
        cpu.Execute(new Instruction(0x0000_000C));

        Assert.Equal(0xBFC0_0180u, cpu.Pc);
    }

    [Fact]
    public void StatusRegisterModeStackShiftsOnExceptionAndRestoresOnRfe()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        // addiu $t0, zero, 1 (IEc = 1, os demais bits zerados)
        cpu.Execute(new Instruction(0x2408_0001));
        // mtc0 $t0, $12 (SR)
        cpu.Execute(new Instruction(0x4088_6000));

        Assert.Equal(0b0000_01u, cpu.Cop0.Sr & 0x3F);

        // syscall -> deve deslocar a pilha de modo 2 bits à esquerda
        cpu.Execute(new Instruction(0x0000_000C));

        Assert.Equal(0b0001_00u, cpu.Cop0.Sr & 0x3F);

        // rfe -> deve reverter o deslocamento
        cpu.Execute(new Instruction(0x4200_0010));

        Assert.Equal(0b0000_01u, cpu.Cop0.Sr & 0x3F);
    }

    [Fact]
    public void Mtc0AndMfc0RoundTripThroughStatusRegister()
    {
        var cpu = new R3000A();

        // lui $t0, 0xDEAD
        cpu.Execute(new Instruction(0x3C08_DEAD));
        // ori $t0, $t0, 0xBEEF
        cpu.Execute(new Instruction(0x3508_BEEF));
        // mtc0 $t0, $12 (SR)
        cpu.Execute(new Instruction(0x4088_6000));
        // mfc0 $t1, $12 (SR)
        cpu.Execute(new Instruction(0x4009_6000));

        Assert.Equal(0xDEAD_BEEFu, cpu.Cop0.Sr);
        Assert.Equal(0xDEAD_BEEFu, cpu.GetRegister(9));
    }

    [Fact]
    public void StepFullCycleWithSyscallJumpsToHandlerInsteadOfNextInstruction()
    {
        var ram = new Ram();
        var bus = new Bus(ram);
        var cpu = new R3000A(bus);
        cpu.Reset(0x0000_0000);

        bus.Write32(0x00, 0x2408_0001); // addiu $t0, zero, 1
        bus.Write32(0x04, 0x0000_000C); // syscall
        bus.Write32(0x08, 0x2409_0063); // addiu $t1, zero, 99 (não deveria rodar)

        cpu.Step(); // addiu
        Assert.Equal(1u, cpu.GetRegister(8));

        cpu.Step(); // syscall

        Assert.Equal(0x8000_0080u, cpu.Pc);
        Assert.Equal(0x04u, cpu.Cop0.Epc);
        Assert.Equal(0u, cpu.GetRegister(9)); // 0x08 nunca rodou
    }

    [Fact]
    public void SyscallInsideBranchDelaySlotSetsBdBitAndEpcMinusFour()
    {
        var ram = new Ram();
        var bus = new Bus(ram);
        var cpu = new R3000A(bus);
        cpu.Reset(0x0000_0000);

        bus.Write32(0x00, 0x2408_0001); // addiu $t0, zero, 1
        bus.Write32(0x04, 0x2409_0001); // addiu $t1, zero, 1
        bus.Write32(0x08, 0x1109_0002); // beq $t0, $t1, offset=2 (tomado)
        bus.Write32(0x0C, 0x0000_000C); // delay slot: syscall!
        bus.Write32(0x10, 0x240A_0063); // não deveria rodar (nem fall-through nem branch)

        cpu.Step(); // addiu t0
        cpu.Step(); // addiu t1
        cpu.Step(); // beq (agenda branch)
        cpu.Step(); // delay slot: syscall -> deve cancelar o branch e desviar

        Assert.Equal(0x8000_0080u, cpu.Pc);

        // EPC deve apontar para o PRÓPRIO branch (0x08), não para o delay
        // slot (0x0C) -- assim o handler pode reexecutar o branch ao voltar.
        Assert.Equal(0x08u, cpu.Cop0.Epc);

        uint bd = (cpu.Cop0.Cause >> 31) & 1;
        Assert.Equal(1u, bd);

        Assert.Equal(0u, cpu.GetRegister(10)); // 0x10 nunca rodou
    }

    [Fact]
    public void UserModeCannotFetchFromKernelSegments()
    {
        var cpu = new R3000A();
        cpu.Reset(0x8000_0000);
        cpu.Cop0.Sr = 1u << 1;

        cpu.Step();

        Assert.Equal((uint)ExceptionCode.AddressErrorLoad, ExceptionCodeFrom(cpu));
        Assert.Equal(0x8000_0000u, cpu.Cop0.BadVaddr);
    }

    [Fact]
    public void UserModeCannotLoadFromKernelSegments()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);
        cpu.Execute(new Instruction(0x3C08_8000)); // lui $t0, 0x8000
        cpu.Cop0.Sr = 1u << 1;

        cpu.Execute(new Instruction(0x8D09_0000)); // lw $t1, 0($t0)

        Assert.Equal((uint)ExceptionCode.AddressErrorLoad, ExceptionCodeFrom(cpu));
        Assert.Equal(0x8000_0000u, cpu.Cop0.BadVaddr);
    }

    [Fact]
    public void UserModeCannotStoreToKernelSegments()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);
        cpu.Execute(new Instruction(0x3C08_8000)); // lui $t0, 0x8000
        cpu.Cop0.Sr = 1u << 1;

        cpu.Execute(new Instruction(0xAD00_0000)); // sw $zero, 0($t0)

        Assert.Equal((uint)ExceptionCode.AddressErrorStore, ExceptionCodeFrom(cpu));
        Assert.Equal(0x8000_0000u, cpu.Cop0.BadVaddr);
    }

    [Fact]
    public void ProcessorIdMatchesPlayStationR3000A()
    {
        var cpu = new R3000A();

        Assert.Equal(2u, cpu.Cop0.GetRegister(15));
    }

    [Fact]
    public void Mtc0CanOnlyChangeSoftwareInterruptBitsInCause()
    {
        var cpu = new R3000A();
        cpu.Cop0.Cause = 0x8000_007C;
        cpu.Execute(new Instruction(0x2408_0300)); // addiu $t0, $zero, 0x0300

        cpu.Execute(new Instruction(0x4088_6800)); // mtc0 $t0, $13

        Assert.Equal(0x8000_037Cu, cpu.Cop0.Cause);
    }

    [Fact]
    public void Mtc0IgnoresWritesToReadOnlyRegisters()
    {
        var cpu = new R3000A();
        cpu.Execute(new Instruction(0x2408_002A)); // addiu $t0, $zero, 42

        cpu.Execute(new Instruction(0x4088_7800)); // mtc0 $t0, $15

        Assert.Equal(2u, cpu.Cop0.GetRegister(15));
    }

    [Fact]
    public void Mfc0FromUnavailableRegisterRaisesReservedInstruction()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        cpu.Execute(new Instruction(0x4008_0000)); // mfc0 $t0, $0

        Assert.Equal((uint)ExceptionCode.ReservedInstruction, ExceptionCodeFrom(cpu));
        Assert.Equal(0x1000u, cpu.Cop0.Epc);
    }

    private static uint ExceptionCodeFrom(R3000A cpu) =>
        (cpu.Cop0.Cause >> 2) & 0x1F;
}
