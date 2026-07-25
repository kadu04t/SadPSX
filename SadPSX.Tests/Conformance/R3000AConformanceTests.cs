using SadPSX.Core.Cpu;
using Xunit;

namespace SadPSX.Tests.Conformance;

public sealed class R3000AConformanceTests
{
    [Fact]
    public void BranchDelayAndLoadDelayRemainIndependent()
    {
        var program = new MipsProgram();
        program.Bus.Write32(0x100, 0xDEAD_BEEF);
        program.Load(
            0,
            MipsProgram.IType(0x09, 0, 8, 1),
            MipsProgram.IType(0x04, 8, 8, 2),
            MipsProgram.IType(0x23, 0, 9, 0x100),
            MipsProgram.IType(0x09, 0, 9, 99),
            MipsProgram.RType(9, 0, 10, 0x21),
            0);

        program.Run(5);

        Assert.Equal(0xDEAD_BEEFu, program.Cpu.GetRegister(9));
        Assert.Equal(0u, program.Cpu.GetRegister(10));
        Assert.Equal(0x18u, program.Cpu.Pc);
    }

    [Fact]
    public void UnalignedLoadStorePairCopiesAWordWithoutTouchingNeighbors()
    {
        var program = new MipsProgram();
        program.Bus.Write32(0x100, 0x4433_2211);
        program.Bus.Write32(0x104, 0x8877_6655);
        program.Bus.Write32(0x200, 0xDEAD_BEEF);
        program.Bus.Write32(0x204, 0xCAFE_BABE);
        program.Load(
            0,
            MipsProgram.IType(0x22, 0, 8, 0x104),
            MipsProgram.IType(0x26, 0, 8, 0x101),
            0,
            MipsProgram.IType(0x2A, 0, 8, 0x204),
            MipsProgram.IType(0x2E, 0, 8, 0x201));

        program.Run(5);

        Assert.Equal(0x5544_3322u, program.Cpu.GetRegister(8));
        Assert.Equal(0xEF, program.Bus.Read8(0x200));
        Assert.Equal(0x22, program.Bus.Read8(0x201));
        Assert.Equal(0x33, program.Bus.Read8(0x202));
        Assert.Equal(0x44, program.Bus.Read8(0x203));
        Assert.Equal(0x55, program.Bus.Read8(0x204));
        Assert.Equal(0xBA, program.Bus.Read8(0x205));
    }

    [Fact]
    public void ExceptionVectorCanExecuteAHandlerFromMappedRam()
    {
        var program = new MipsProgram();
        program.Load(0, 0x0000_000C);
        program.Load(
            0x80,
            MipsProgram.IType(0x09, 0, 26, 42));

        program.Run(2);

        Assert.Equal(42u, program.Cpu.GetRegister(26));
        Assert.Equal(0u, program.Cpu.Cop0.Epc);
        Assert.Equal(
            (uint)ExceptionCode.Syscall,
            (program.Cpu.Cop0.Cause >> 2) & 0x1F);
    }

    [Fact]
    public void Cop0TransfersHonorLoadDelayInAProgram()
    {
        var program = new MipsProgram();
        program.Load(
            0,
            MipsProgram.IType(0x0F, 0, 8, 0x0040),
            MipsProgram.IType(0x0D, 8, 8, 1),
            0x4088_6000,
            0x4009_6000,
            MipsProgram.RType(9, 0, 10, 0x21),
            0);

        program.Run(6);

        Assert.Equal(0x0040_0001u, program.Cpu.Cop0.Sr);
        Assert.Equal(0x0040_0001u, program.Cpu.GetRegister(9));
        Assert.Equal(0u, program.Cpu.GetRegister(10));
    }
}
