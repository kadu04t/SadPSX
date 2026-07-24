using Xunit;
using SadPSX.Core.Cpu;

namespace SadPSX.Tests.Cpu;

public sealed class ArithmeticLogicalTests
{
    [Fact]
    public void AndiMasksLowByte()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // andi $t1, $t0, 0x00FF
        cpu.Execute(new Instruction(0x3109_00FF));

        Assert.Equal(0x0000_00FFu, cpu.GetRegister(9));
    }

    [Fact]
    public void XoriFlipsLowerHalfword()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // xori $t2, $t0, 0xFFFF
        cpu.Execute(new Instruction(0x390A_FFFF));

        Assert.Equal(0xFFFF_0000u, cpu.GetRegister(10));
    }

    [Fact]
    public void SltiReturnsOneWhenLessThan()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 5
        cpu.Execute(new Instruction(0x2408_0005));
        // slti $t1, $t0, 10
        cpu.Execute(new Instruction(0x2909_000A));

        Assert.Equal(1u, cpu.GetRegister(9));
    }

    [Fact]
    public void SltiReturnsZeroWhenNotLessThan()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 5
        cpu.Execute(new Instruction(0x2408_0005));
        // slti $t2, $t0, 3
        cpu.Execute(new Instruction(0x290A_0003));

        Assert.Equal(0u, cpu.GetRegister(10));
    }

    [Fact]
    public void SltiuComparesSignExtendedImmediateAsUnsigned()
    {
        // Caso clássico: sltiu $t5, $zero, -
        // O imediato -1 é sign-extended para 0xFFFFFFFF ANTES da comparação
        // unsigned. 0 < 0xFFFFFFFF é verdadeiro, mesmo com o "U" no nome
        // sugerindo (erroneamente) que o imediato seria zero-extended.
        var cpu = new R3000A();

        // sltiu $t5, $zero, -1
        cpu.Execute(new Instruction(0x2C0D_FFFF));

        Assert.Equal(1u, cpu.GetRegister(13));
    }

    [Fact]
    public void SltiuWithIdenticalBitPatternsReturnsFalse()
    {
        var cpu = new R3000A();

        // addiu $t3, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x240B_FFFF));
        // sltiu $t4, $t3, -1  -> 0xFFFFFFFF < 0xFFFFFFFF = false
        cpu.Execute(new Instruction(0x2D6C_FFFF));

        Assert.Equal(0u, cpu.GetRegister(12));
    }

    [Fact]
    public void SltUsesSignedComparison()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF, ou seja -1 signed)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // addiu $t1, $zero, 1
        cpu.Execute(new Instruction(0x2409_0001));
        // slt $t2, $t0, $t1  -> -1 < 1 (signed) = true
        cpu.Execute(new Instruction(0x0109_502A));

        Assert.Equal(1u, cpu.GetRegister(10));
    }

    [Fact]
    public void SltuUsesUnsignedComparison()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // addiu $t1, $zero, 1
        cpu.Execute(new Instruction(0x2409_0001));
        // sltu $t3, $t0, $t1  -> 0xFFFFFFFF < 1 (unsigned) = false
        cpu.Execute(new Instruction(0x0109_582B));

        Assert.Equal(0u, cpu.GetRegister(11));
    }

    [Fact]
    public void NorOfZeroAndZeroIsAllOnes()
    {
        var cpu = new R3000A();

        // nor $t4, $zero, $zero
        cpu.Execute(new Instruction(0x0000_6027));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(12));
    }
}