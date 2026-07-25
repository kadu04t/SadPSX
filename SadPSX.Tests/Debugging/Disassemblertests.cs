using SadPSX.Core.Cpu;
using SadPSX.Core.Debugging;
using Xunit;

namespace SadPSX.Tests.Debugging;

public sealed class DisassemblerTests
{
    [Fact]
    public void DisassemblesAddiu()
    {
        var text = Disassembler.Disassemble(new Instruction(0x2408_1234));

        Assert.Equal("addiu    $t0, $zero, 4660", text);
    }

    [Fact]
    public void DisassemblesOriWithHexImmediate()
    {
        var text = Disassembler.Disassemble(new Instruction(0x3508_5678));

        Assert.Equal("ori      $t0, $t0, 0x5678", text);
    }

    [Fact]
    public void DisassemblesLui()
    {
        var text = Disassembler.Disassemble(new Instruction(0x3C08_1234));

        Assert.Equal("lui      $t0, 0x1234", text);
    }

    [Fact]
    public void DisassemblesAllZerosAsNop()
    {
        var text = Disassembler.Disassemble(new Instruction(0x0000_0000));

        Assert.Equal("nop", text);
    }

    [Fact]
    public void DisassemblesSllWithNonZeroOperandsNormally()
    {
        // sll $t1, $t0, 4 (não é o caso especial de NOP)
        var text = Disassembler.Disassemble(new Instruction(0x0008_4900));

        Assert.Equal("sll      $t1, $t0, 4", text);
    }

    [Fact]
    public void DisassemblesSwAndLwWithOffsetAndBaseRegister()
    {
        var sw = Disassembler.Disassemble(new Instruction(0xAC08_0000));
        var lw = Disassembler.Disassemble(new Instruction(0x8C09_0000));

        Assert.Equal("sw       $t0, 0($zero)", sw);
        Assert.Equal("lw       $t1, 0($zero)", lw);
    }

    [Fact]
    public void DisassemblesAdduWithThreeRegisterOperands()
    {
        var text = Disassembler.Disassemble(new Instruction(0x0109_5021));

        Assert.Equal("addu     $t2, $t0, $t1", text);
    }

    [Fact]
    public void DisassemblesSyscallAndBreak()
    {
        var syscall = Disassembler.Disassemble(new Instruction(0x0000_000C));
        var brk = Disassembler.Disassemble(new Instruction(0x0000_000D));

        Assert.Equal("syscall", syscall);
        Assert.Equal("break", brk);
    }

    [Fact]
    public void DisassemblesMultAndDiv()
    {
        var mult = Disassembler.Disassemble(new Instruction(0x0109_0018));
        var div = Disassembler.Disassemble(new Instruction(0x0109_001A));

        Assert.Equal("mult     $t0, $t1", mult);
        Assert.Equal("div      $t0, $t1", div);
    }

    [Fact]
    public void DisassemblesBeqWithComputedAbsoluteTarget()
    {
        // beq $t0, $t1, offset=2, na instrução localizada em pc=0x08
        // target = pc+4 + (2<<2) = 0x0C + 8 = 0x14
        var text = Disassembler.Disassemble(new Instruction(0x1109_0002), pc: 0x08);

        Assert.Equal("beq      $t0, $t1, 0x00000014", text);
    }

    [Fact]
    public void DisassemblesJWithComputedAbsoluteTarget()
    {
        // j 0x10, buscada em pc=0x00 -> target = ((0x00+4) & 0xF0000000) | (0x10>>2 << 2) = 0x10
        var text = Disassembler.Disassemble(new Instruction(0x0800_0004), pc: 0x00);

        Assert.Equal("j        0x00000010", text);
    }

    [Fact]
    public void DisassemblesJalAndSetsReturnAddressImplicitlyInText()
    {
        var text = Disassembler.Disassemble(new Instruction(0x0C00_0004), pc: 0x00);

        Assert.Equal("jal      0x00000010", text);
    }

    [Fact]
    public void DisassemblesJrAndJalr()
    {
        var jr = Disassembler.Disassemble(new Instruction(0x0000_0008)); // jr $zero
        var jalrDefault = Disassembler.Disassemble(new Instruction(0x03E0_F809)); // jalr $ra (rd=31 implícito)

        Assert.Equal("jr       $zero", jr);
        Assert.Equal("jalr     $ra", jalrDefault);
    }

    [Fact]
    public void DisassemblesMfc0AndMtc0WithCop0RegisterNumber()
    {
        var mtc0 = Disassembler.Disassemble(new Instruction(0x4088_6000)); // mtc0 $t0, $12
        var mfc0 = Disassembler.Disassemble(new Instruction(0x4009_6000)); // mfc0 $t1, $12

        Assert.Equal("mtc0     $t0, cop0r12", mtc0);
        Assert.Equal("mfc0     $t1, cop0r12", mfc0);
    }

    [Fact]
    public void DisassemblesRfe()
    {
        var text = Disassembler.Disassemble(new Instruction(0x4200_0010));

        Assert.Equal("rfe", text);
    }

    [Fact]
    public void UnknownOpcodeProducesDiagnosticTextInsteadOfThrowing()
    {
        // opcode 0x3F não existe em nenhum switch do disassembler.
        var text = Disassembler.Disassemble(new Instruction(0xFC00_0000));

        Assert.Contains(".word", text);
        Assert.Contains("FC000000", text);
    }
}