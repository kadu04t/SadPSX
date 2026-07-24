using SadPSX.Core.Cpu;
using Xunit;

namespace SadPSX.Tests.Cpu;

public sealed class InstructionTests
{
    [Fact]
    public void ConstructorPreservesRawInstructionValue()
    {
        const uint rawInstruction = 0x1234_5678;

        var instruction = new Instruction(rawInstruction);

        Assert.Equal(rawInstruction, instruction.Value);
    }

    [Fact]
    public void DecodesRTypeInstructionFields()
    {
        // Instrução sintética tipo-R:
        //
        // opcode = 0x00
        // rs     = 17
        // rt     = 9
        // rd     = 10
        // sa     = 4
        // funct  = 0x21
        //
        // Bits:
        // 000000 10001 01001 01010 00100 100001
        const uint rawInstruction = 0x0229_5121;

        var instruction = new Instruction(rawInstruction);

        Assert.Equal(0x00u, instruction.Opcode);
        Assert.Equal(17, instruction.Rs);
        Assert.Equal(9, instruction.Rt);
        Assert.Equal(10, instruction.Rd);
        Assert.Equal(4, instruction.ShiftAmount);
        Assert.Equal(0x21u, instruction.Function);
    }

    [Fact]
    public void DecodesITypeInstructionFields()
    {
        // addiu $t0, $t1, -2
        //
        // opcode = 0x09
        // rs     = 9  ($t1)
        // rt     = 8  ($t0)
        // imm    = 0xFFFE (-2)
        const uint rawInstruction = 0x2528_FFFE;

        var instruction = new Instruction(rawInstruction);

        Assert.Equal(0x09u, instruction.Opcode);
        Assert.Equal(9, instruction.Rs);
        Assert.Equal(8, instruction.Rt);
        Assert.Equal(0xFFFE, instruction.Immediate);
        Assert.Equal(-2, instruction.SignedImmediate);
    }

    [Fact]
    public void DecodesJTypeInstructionFields()
    {
        // j com campo target 0x00123456
        //
        // opcode = 0x02
        // target = 0x00123456
        const uint rawInstruction = 0x0812_3456;

        var instruction = new Instruction(rawInstruction);

        Assert.Equal(0x02u, instruction.Opcode);
        Assert.Equal(0x0012_3456u, instruction.JumpTarget);
    }

    [Theory]
    [InlineData(0x0000_0000u, 0)]
    [InlineData(0x0000_0001u, 1)]
    [InlineData(0x0000_7FFFu, 32767)]
    [InlineData(0x0000_8000u, -32768)]
    [InlineData(0x0000_FFFFu, -1)]
    public void SignedImmediateSignExtendsLowerSixteenBits(
        uint rawInstruction,
        int expected)
    {
        var instruction = new Instruction(rawInstruction);

        Assert.Equal(expected, instruction.SignedImmediate);
    }

    [Fact]
    public void ImmediateUsesOnlyLowerSixteenBits()
    {
        const uint rawInstruction = 0xABCD_1234;

        var instruction = new Instruction(rawInstruction);

        Assert.Equal(0x1234, instruction.Immediate);
    }

    [Fact]
    public void JumpTargetUsesOnlyLowerTwentySixBits()
    {
        const uint rawInstruction = 0xFFFF_FFFF;

        var instruction = new Instruction(rawInstruction);

        Assert.Equal(0x03FF_FFFFu, instruction.JumpTarget);
    }

    [Fact]
    public void MaximumFieldValuesAreDecodedCorrectly()
    {
        const uint rawInstruction = 0xFFFF_FFFF;

        var instruction = new Instruction(rawInstruction);

        Assert.Equal(0x3Fu, instruction.Opcode);
        Assert.Equal(31, instruction.Rs);
        Assert.Equal(31, instruction.Rt);
        Assert.Equal(31, instruction.Rd);
        Assert.Equal(31, instruction.ShiftAmount);
        Assert.Equal(0x3Fu, instruction.Function);
        Assert.Equal(0xFFFF, instruction.Immediate);
        Assert.Equal(-1, instruction.SignedImmediate);
        Assert.Equal(0x03FF_FFFFu, instruction.JumpTarget);
    }
}