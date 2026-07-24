using Xunit;
using SadPSX.Core.Cpu;

namespace SadPSX.Tests.Cpu;

public sealed class R3000ATests
{
    [Fact]
    public void LuiCannotModifyZeroRegister()
    {
        var cpu = new R3000A();

        // lui $zero, 0x1234
        cpu.Execute(new Instruction(0x3C00_1234));

        Assert.Equal(0u, cpu.GetRegister(0));
    }

    [Fact]
    public void LuiAndOriBuildThirtyTwoBitValue()
    {
        var cpu = new R3000A();

        // lui $t0, 0x1234
        cpu.Execute(new Instruction(0x3C08_1234));

        // ori $t0, $t0, 0x5678
        cpu.Execute(new Instruction(0x3508_5678));

        Assert.Equal(0x1234_5678u, cpu.GetRegister(8));
    }

    [Fact]
    public void AddiuSignExtendsNegativeImmediate()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1
        cpu.Execute(new Instruction(0x2408_FFFF));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(8));
    }

    [Fact]
    public void SllZeroZeroZeroActsAsNop()
    {
        var cpu = new R3000A();

        cpu.Execute(new Instruction(0x0000_0000));

        Assert.Equal(0u, cpu.GetRegister(0));
    }

    [Fact]
    public void SllShiftsRegisterLeft()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 3
        cpu.Execute(new Instruction(0x2408_0003));

        // sll $t1, $t0, 4
        cpu.Execute(new Instruction(0x0008_4900));

        Assert.Equal(48u, cpu.GetRegister(9));
    }

    [Fact]
    public void AdduWrapsWithoutOverflowException()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1
        cpu.Execute(new Instruction(0x2408_FFFF));

        // addiu $t1, $zero, 1
        cpu.Execute(new Instruction(0x2409_0001));

        // addu $t2, $t0, $t1
        cpu.Execute(new Instruction(0x0109_5021));

        Assert.Equal(0u, cpu.GetRegister(10));
    }
}
