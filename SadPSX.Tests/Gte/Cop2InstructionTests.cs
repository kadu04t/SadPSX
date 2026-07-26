using SadPSX.Core.Cpu;
using Xunit;

namespace SadPSX.Tests.Gte;

public sealed class Cop2InstructionTests
{
    [Fact]
    public void Mtc2AndMfc2TransferDataRegisters()
    {
        var cpu = new R3000A();
        SetRegister(cpu, 8, 0x1234_5678);

        cpu.Execute(Cop2Transfer(rs: 4, rt: 8, rd: 6));
        cpu.Execute(Cop2Transfer(rs: 0, rt: 9, rd: 6));

        Assert.Equal(0x1234_5678u, cpu.Gte.ReadDataRegister(6));
        Assert.Equal(0x1234_5678u, cpu.GetRegister(9));
    }

    [Fact]
    public void Ctc2AndCfc2TransferControlRegisters()
    {
        var cpu = new R3000A();
        SetRegister(cpu, 8, 0x0012_3456);

        cpu.Execute(Cop2Transfer(rs: 6, rt: 8, rd: 5));
        cpu.Execute(Cop2Transfer(rs: 2, rt: 9, rd: 5));

        Assert.Equal(0x0012_3456u, cpu.Gte.ReadControlRegister(5));
        Assert.Equal(0x0012_3456u, cpu.GetRegister(9));
    }

    [Fact]
    public void Lwc2AndSwc2RoundTripThroughRam()
    {
        var cpu = new R3000A();
        SetRegister(cpu, 8, 0xCAFE_BABE);
        cpu.Execute(new Instruction(0xAC08_0000));

        cpu.Execute(new Instruction(0xC806_0000));
        cpu.Execute(new Instruction(0xE806_0004));
        cpu.Execute(new Instruction(0x8C09_0004));

        Assert.Equal(0xCAFE_BABEu, cpu.Gte.ReadDataRegister(6));
        Assert.Equal(0xCAFE_BABEu, cpu.GetRegister(9));
    }

    [Fact]
    public void Cop2CommandDispatchesToGte()
    {
        var cpu = new R3000A();
        cpu.Gte.WriteDataRegister(12, 0);
        cpu.Gte.WriteDataRegister(13, 10);
        cpu.Gte.WriteDataRegister(14, 10u << 16);

        cpu.Execute(new Instruction(0x4A00_0006));

        Assert.Equal(100u, cpu.Gte.ReadDataRegister(24));
    }

    [Fact]
    public void RaymanNcdsOpcodeDispatchesWithoutReservedInstruction()
    {
        var cpu = new R3000A();
        CpuExceptionInfo? raisedException = null;
        cpu.ExceptionOccurred += exception => raisedException = exception;

        cpu.Execute(new Instruction(0x4AE8_0413));

        Assert.Null(raisedException);
    }

    private static Instruction Cop2Transfer(int rs, int rt, int rd) =>
        new(0x4800_0000u |
            ((uint)rs << 21) |
            ((uint)rt << 16) |
            ((uint)rd << 11));

    private static void SetRegister(R3000A cpu, int register, uint value)
    {
        cpu.Execute(new Instruction(0x3C00_0000u |
                                    ((uint)register << 16) |
                                    (value >> 16)));
        cpu.Execute(new Instruction(0x3400_0000u |
                                    ((uint)register << 21) |
                                    ((uint)register << 16) |
                                    (value & 0xFFFF)));
    }
}
