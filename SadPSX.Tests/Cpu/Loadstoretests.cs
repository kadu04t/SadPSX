using Xunit;
using SadPSX.Core.Cpu;

namespace SadPSX.Tests.Cpu;

public sealed class LoadStoreTests
{
    [Fact]
    public void SwThenLwRoundTripsThirtyTwoBitValue()
    {
        var cpu = new R3000A();

        // lui $t0, 0x1234
        cpu.Execute(new Instruction(0x3C08_1234));
        // ori $t0, $t0, 0x5678
        cpu.Execute(new Instruction(0x3508_5678));
        // sw $t0, 0($zero)
        cpu.Execute(new Instruction(0xAC08_0000));
        // lw $t1, 0($zero)
        cpu.Execute(new Instruction(0x8C09_0000));

        Assert.Equal(0x1234_5678u, cpu.GetRegister(8));
        Assert.Equal(0x1234_5678u, cpu.GetRegister(9));
    }

    [Fact]
    public void SbThenLbSignExtendsNegativeByte()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // sb $t0, 0($zero)
        cpu.Execute(new Instruction(0xA008_0000));
        // lb $t1, 0($zero)
        cpu.Execute(new Instruction(0x8009_0000));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(9));
    }

    [Fact]
    public void SbThenLbuZeroExtendsByte()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // sb $t0, 0($zero)
        cpu.Execute(new Instruction(0xA008_0000));
        // lbu $t2, 0($zero)
        cpu.Execute(new Instruction(0x900A_0000));

        Assert.Equal(0x0000_00FFu, cpu.GetRegister(10));
    }

    [Fact]
    public void ShThenLhSignExtendsNegativeHalfword()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // sh $t0, 4($zero)
        cpu.Execute(new Instruction(0xA408_0004));
        // lh $t3, 4($zero)
        cpu.Execute(new Instruction(0x840B_0004));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(11));
    }

    [Fact]
    public void ShThenLhuZeroExtendsHalfword()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // sh $t0, 4($zero)
        cpu.Execute(new Instruction(0xA408_0004));
        // lhu $t4, 4($zero)
        cpu.Execute(new Instruction(0x940C_0004));

        Assert.Equal(0x0000_FFFFu, cpu.GetRegister(12));
    }

    [Fact]
    public void SbDoesNotClobberNeighborBytes()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // sb $t0, 0($zero)  -> só o byte 0 deve virar 0xFF
        cpu.Execute(new Instruction(0xA008_0000));
        // lw $t1, 0($zero)  -> os outros 3 bytes devem continuar zerados
        cpu.Execute(new Instruction(0x8C09_0000));

        Assert.Equal(0x0000_00FFu, cpu.GetRegister(9));
    }
}