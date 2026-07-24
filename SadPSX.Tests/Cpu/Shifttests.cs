using Xunit;
using SadPSX.Core.Cpu;

namespace SadPSX.Tests.Cpu;

public sealed class ShiftTests
{
    [Fact]
    public void SrlFillsWithZeroRegardlessOfSignBit()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // srl $t1, $t0, 4
        cpu.Execute(new Instruction(0x0008_4902));

        Assert.Equal(0x0FFF_FFFFu, cpu.GetRegister(9));
    }

    [Fact]
    public void SraPreservesSignBit()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // sra $t2, $t0, 4
        cpu.Execute(new Instruction(0x0008_5103));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(10));
    }

    [Fact]
    public void SllvShiftsByAmountFromRegister()
    {
        var cpu = new R3000A();

        // addiu $t3, $zero, 3
        cpu.Execute(new Instruction(0x240B_0003));
        // addiu $t4, $zero, 4
        cpu.Execute(new Instruction(0x240C_0004));
        // sllv $t5, $t3, $t4
        cpu.Execute(new Instruction(0x018B_6804));

        Assert.Equal(48u, cpu.GetRegister(13));
    }

    [Fact]
    public void SrlvShiftsLogicallyByAmountFromRegister()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // addiu $t4, $zero, 4
        cpu.Execute(new Instruction(0x240C_0004));
        // srlv $t6, $t0, $t4
        cpu.Execute(new Instruction(0x0188_7006));

        Assert.Equal(0x0FFF_FFFFu, cpu.GetRegister(14));
    }

    [Fact]
    public void SravShiftsArithmeticallyByAmountFromRegister()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // addiu $t4, $zero, 4
        cpu.Execute(new Instruction(0x240C_0004));
        // srav $t7, $t0, $t4
        cpu.Execute(new Instruction(0x0188_7807));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(15));
    }

    [Fact]
    public void SllvMasksShiftAmountToFiveBits()
    {
        // Quantidade de shift = 35 (0x23). Como só os 5 bits baixos contam,
        // 35 & 0x1F = 3, então deve se comportar como shift de 3, não de 35.
        var cpu = new R3000A();

        // addiu $t3, $zero, 1
        cpu.Execute(new Instruction(0x240B_0001));
        // addiu $t4, $zero, 35
        cpu.Execute(new Instruction(0x240C_0023));
        // sllv $t5, $t3, $t4
        cpu.Execute(new Instruction(0x018B_6804));

        Assert.Equal(8u, cpu.GetRegister(13));
    }
}
