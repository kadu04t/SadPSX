using SadPSX.Core.Cpu;
using SadPSX.Core.Memory;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Cpu;

public sealed class CpuCorrectnessTests
{
    [Fact]
    public void AddComputesSignedResult()
    {
        var cpu = new R3000A();

        cpu.Execute(new Instruction(0x2408_0028)); // addiu $t0, $zero, 40
        cpu.Execute(new Instruction(0x2409_0002)); // addiu $t1, $zero, 2
        cpu.Execute(new Instruction(0x0109_5020)); // add $t2, $t0, $t1

        Assert.Equal(42u, cpu.GetRegister(10));
    }

    [Fact]
    public void AddOverflowRaisesExceptionWithoutWritingDestination()
    {
        var cpu = new R3000A();

        cpu.Execute(new Instruction(0x3C08_7FFF)); // lui $t0, 0x7FFF
        cpu.Execute(new Instruction(0x3508_FFFF)); // ori $t0, $t0, 0xFFFF
        cpu.Execute(new Instruction(0x2409_0001)); // addiu $t1, $zero, 1
        cpu.Execute(new Instruction(0x0109_5020)); // add $t2, $t0, $t1

        Assert.Equal(0u, cpu.GetRegister(10));
        Assert.Equal((uint)ExceptionCode.Overflow, ExceptionCodeFrom(cpu));
    }

    [Fact]
    public void SubComputesSignedResult()
    {
        var cpu = new R3000A();

        cpu.Execute(new Instruction(0x2408_002C)); // addiu $t0, $zero, 44
        cpu.Execute(new Instruction(0x2409_0002)); // addiu $t1, $zero, 2
        cpu.Execute(new Instruction(0x0109_5022)); // sub $t2, $t0, $t1

        Assert.Equal(42u, cpu.GetRegister(10));
    }

    [Fact]
    public void SubOverflowRaisesExceptionWithoutWritingDestination()
    {
        var cpu = new R3000A();

        cpu.Execute(new Instruction(0x3C08_8000)); // lui $t0, 0x8000
        cpu.Execute(new Instruction(0x2409_0001)); // addiu $t1, $zero, 1
        cpu.Execute(new Instruction(0x0109_5022)); // sub $t2, $t0, $t1

        Assert.Equal(0u, cpu.GetRegister(10));
        Assert.Equal((uint)ExceptionCode.Overflow, ExceptionCodeFrom(cpu));
    }

    [Fact]
    public void LoadResultIsVisibleOnlyAfterTheFollowingInstruction()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x8C08_0100); // lw $t0, 0x100($zero)
        bus.Write32(0x04, 0x0100_4821); // addu $t1, $t0, $zero
        bus.Write32(0x08, 0x0100_5021); // addu $t2, $t0, $zero
        bus.Write32(0x100, 0x1234_5678);

        cpu.Step();
        Assert.Equal(0u, cpu.GetRegister(8));

        cpu.Step();
        Assert.Equal(0u, cpu.GetRegister(9));
        Assert.Equal(0x1234_5678u, cpu.GetRegister(8));

        cpu.Step();
        Assert.Equal(0x1234_5678u, cpu.GetRegister(10));
    }

    [Fact]
    public void ImmediateWriteToLoadDestinationCancelsPendingLoad()
    {
        var (bus, cpu) = CreateCpu();

        bus.Write32(0x00, 0x8C08_0100); // lw $t0, 0x100($zero)
        bus.Write32(0x04, 0x2408_0007); // addiu $t0, $zero, 7
        bus.Write32(0x08, 0x0000_0000); // nop
        bus.Write32(0x100, 0x1234_5678);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(7u, cpu.GetRegister(8));
    }

    [Fact]
    public void IsolatedCacheStoresDoNotOverwriteMainRam()
    {
        var (bus, cpu) = CreateCpu();
        bus.Write32(0x100, 0xDEAD_BEEF);

        cpu.Cop0.Sr = 1u << 16;
        cpu.Execute(new Instruction(0xAC00_0100)); // sw $zero, 0x100($zero)

        Assert.Equal(0xDEAD_BEEFu, bus.Read32(0x100));
    }

    [Fact]
    public void UnalignedLoadRaisesAddressErrorAndSetsBadVaddr()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        cpu.Execute(new Instruction(0x8C08_0001)); // lw $t0, 1($zero)

        Assert.Equal((uint)ExceptionCode.AddressErrorLoad, ExceptionCodeFrom(cpu));
        Assert.Equal(1u, cpu.Cop0.BadVaddr);
        Assert.Equal(0x8000_0080u, cpu.Pc);
    }

    [Fact]
    public void UnalignedStoreRaisesAddressErrorAndSetsBadVaddr()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        cpu.Execute(new Instruction(0xAC08_0002)); // sw $t0, 2($zero)

        Assert.Equal((uint)ExceptionCode.AddressErrorStore, ExceptionCodeFrom(cpu));
        Assert.Equal(2u, cpu.Cop0.BadVaddr);
    }

    [Fact]
    public void UnalignedInstructionFetchRaisesAddressError()
    {
        var cpu = new R3000A();
        cpu.Reset(0x0000_0002);

        cpu.Step();

        Assert.Equal((uint)ExceptionCode.AddressErrorLoad, ExceptionCodeFrom(cpu));
        Assert.Equal(2u, cpu.Cop0.BadVaddr);
        Assert.Equal(0x8000_0080u, cpu.Pc);
    }

    [Fact]
    public void UnmappedInstructionFetchRaisesInstructionBusError()
    {
        var cpu = new R3000A();
        cpu.Reset(0xE000_0000);

        cpu.Step();

        Assert.Equal((uint)ExceptionCode.InstructionBusError, ExceptionCodeFrom(cpu));
        Assert.Equal(0x8000_0080u, cpu.Pc);
    }

    [Fact]
    public void UnmappedDataLoadRaisesDataBusError()
    {
        var cpu = new R3000A();

        cpu.Execute(new Instruction(0x3C08_E000)); // lui $t0, 0xE000
        cpu.Execute(new Instruction(0x8D09_0000)); // lw $t1, 0($t0)

        Assert.Equal((uint)ExceptionCode.DataBusError, ExceptionCodeFrom(cpu));
        Assert.Equal(0u, cpu.GetRegister(9));
    }

    [Fact]
    public void UnknownOpcodeRaisesReservedInstructionException()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        cpu.Execute(new Instruction(0xFC00_0000));

        Assert.Equal((uint)ExceptionCode.ReservedInstruction, ExceptionCodeFrom(cpu));
        Assert.Equal(0x1000u, cpu.Cop0.Epc);
        Assert.Equal(0x8000_0080u, cpu.Pc);
    }

    [Fact]
    public void ResetClearsWritableCop0StateAndRestoresProcessorId()
    {
        var cpu = new R3000A();

        cpu.Cop0.SetRegister(3, 0xDEAD_BEEF);
        cpu.Cop0.BadVaddr = 0x1234_5678;

        cpu.Reset();

        Assert.Equal(0u, cpu.Cop0.GetRegister(3));
        Assert.Equal(0u, cpu.Cop0.BadVaddr);
        Assert.Equal(2u, cpu.Cop0.GetRegister(15));
    }

    [Fact]
    public void RfePreservesOldModeBits()
    {
        var cpu = new R3000A();
        cpu.Cop0.Sr = 0b11_10_01;

        cpu.Execute(new Instruction(0x4200_0010));

        Assert.Equal(0b11_11_10u, cpu.Cop0.Sr & 0x3F);
    }

    [Fact]
    public void MalformedRfeRaisesReservedInstructionException()
    {
        var cpu = new R3000A();

        cpu.Execute(new Instruction(0x4201_0010));

        Assert.Equal((uint)ExceptionCode.ReservedInstruction, ExceptionCodeFrom(cpu));
    }

    private static (Bus Bus, R3000A Cpu) CreateCpu()
    {
        var bus = new Bus();
        var cpu = new R3000A(bus);
        cpu.Reset(0);
        return (bus, cpu);
    }

    private static uint ExceptionCodeFrom(R3000A cpu) =>
        (cpu.Cop0.Cause >> 2) & 0x1F;
}
