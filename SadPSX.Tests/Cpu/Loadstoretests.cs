using Xunit;
using SadPSX.Core.Cpu;
using SadPSX.Core.Memory;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Cpu;

public sealed class LoadStoreTests
{
    public static TheoryData<ushort, uint> LwlCases => new()
    {
        { 0, 0x11BB_CCDDu },
        { 1, 0x2211_CCDDu },
        { 2, 0x3322_11DDu },
        { 3, 0x4433_2211u },
    };

    public static TheoryData<ushort, uint> LwrCases => new()
    {
        { 0, 0x4433_2211u },
        { 1, 0xAA44_3322u },
        { 2, 0xAABB_4433u },
        { 3, 0xAABB_CC44u },
    };

    public static TheoryData<ushort, uint> SwlCases => new()
    {
        { 0, 0x4433_22AAu },
        { 1, 0x4433_AABBu },
        { 2, 0x44AA_BBCCu },
        { 3, 0xAABB_CCDDu },
    };

    public static TheoryData<ushort, uint> SwrCases => new()
    {
        { 0, 0xAABB_CCDDu },
        { 1, 0xBBCC_DD11u },
        { 2, 0xCCDD_2211u },
        { 3, 0xDD33_2211u },
    };

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

    [Theory]
    [MemberData(nameof(LwlCases))]
    public void LwlMergesExpectedBytesForEveryAlignment(
        ushort offset,
        uint expected)
    {
        var bus = new Bus();
        var cpu = CreateCpuWithRegisterValue(bus, 0xAABB_CCDD);
        bus.Write32(0, 0x4433_2211);

        cpu.Execute(new Instruction(0x8808_0000u | offset));

        Assert.Equal(expected, cpu.GetRegister(8));
    }

    [Theory]
    [MemberData(nameof(LwrCases))]
    public void LwrMergesExpectedBytesForEveryAlignment(
        ushort offset,
        uint expected)
    {
        var bus = new Bus();
        var cpu = CreateCpuWithRegisterValue(bus, 0xAABB_CCDD);
        bus.Write32(0, 0x4433_2211);

        cpu.Execute(new Instruction(0x9808_0000u | offset));

        Assert.Equal(expected, cpu.GetRegister(8));
    }

    [Theory]
    [MemberData(nameof(SwlCases))]
    public void SwlMergesExpectedBytesForEveryAlignment(
        ushort offset,
        uint expected)
    {
        var bus = new Bus();
        var cpu = CreateCpuWithRegisterValue(bus, 0xAABB_CCDD);
        bus.Write32(0, 0x4433_2211);

        cpu.Execute(new Instruction(0xA808_0000u | offset));

        Assert.Equal(expected, bus.Read32(0));
    }

    [Theory]
    [MemberData(nameof(SwrCases))]
    public void SwrMergesExpectedBytesForEveryAlignment(
        ushort offset,
        uint expected)
    {
        var bus = new Bus();
        var cpu = CreateCpuWithRegisterValue(bus, 0xAABB_CCDD);
        bus.Write32(0, 0x4433_2211);

        cpu.Execute(new Instruction(0xB808_0000u | offset));

        Assert.Equal(expected, bus.Read32(0));
    }

    [Fact]
    public void ConsecutiveLwlAndLwrMergePendingLoadValue()
    {
        var bus = new Bus();
        var cpu = CreateCpuWithRegisterValue(bus, 0xAABB_CCDD);
        cpu.Reset(0x100);
        cpu.Execute(new Instruction(0x3C08_AABB));
        cpu.Execute(new Instruction(0x3508_CCDD));

        bus.Write32(0x0000, 0x4433_2211);
        bus.Write32(0x0004, 0x8877_6655);
        bus.Write32(0x0100, 0x8808_0004); // lwl $t0, 4($zero)
        bus.Write32(0x0104, 0x9808_0001); // lwr $t0, 1($zero)
        bus.Write32(0x0108, 0x0000_0000); // nop

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(0x5544_3322u, cpu.GetRegister(8));
    }

    private static R3000A CreateCpuWithRegisterValue(Bus bus, uint value)
    {
        var cpu = new R3000A(bus);
        cpu.Execute(new Instruction(0x3C08_0000u | (value >> 16)));
        cpu.Execute(new Instruction(0x3508_0000u | (value & 0xFFFF)));
        return cpu;
    }
}
