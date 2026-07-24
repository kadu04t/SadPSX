using SadPSX.Core.Cpu;
using Xunit;

namespace SadPSX.Tests.Cpu;

public sealed class MultiplyDivideTests
{
    [Fact]
    public void MultComputesSignedProductWithoutOverflow()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 6
        cpu.Execute(new Instruction(0x2408_0006));
        // addiu $t1, $zero, 7
        cpu.Execute(new Instruction(0x2409_0007));
        // mult $t0, $t1
        cpu.Execute(new Instruction(0x0109_0018));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(42u, cpu.GetRegister(2));
        Assert.Equal(0u, cpu.GetRegister(3));
    }

    [Fact]
    public void MultSignExtendsNegativeResultIntoHi()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // addiu $t1, $zero, 5
        cpu.Execute(new Instruction(0x2409_0005));
        // mult $t0, $t1  -> -1 * 5 = -5
        cpu.Execute(new Instruction(0x0109_0018));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(unchecked((uint)-5), cpu.GetRegister(2));
        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(3)); // Hi sign-extended
    }

    [Fact]
    public void MultOverflowsIntoHi()
    {
        var cpu = new R3000A();

        // lui $t0, 0x0001 -> t0 = 0x00010000
        cpu.Execute(new Instruction(0x3C08_0001));
        // lui $t1, 0x0001 -> t1 = 0x00010000
        cpu.Execute(new Instruction(0x3C09_0001));
        // mult $t0, $t1  -> 0x10000 * 0x10000 = 0x1_0000_0000
        cpu.Execute(new Instruction(0x0109_0018));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(0u, cpu.GetRegister(2));
        Assert.Equal(1u, cpu.GetRegister(3));
    }

    [Fact]
    public void MultuTreatsOperandsAsUnsigned()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -1  (0xFFFFFFFF)
        cpu.Execute(new Instruction(0x2408_FFFF));
        // ori $t1, $zero, 0xFFFF (só pra ter um valor grande; combinado com lui abaixo)
        cpu.Execute(new Instruction(0x2409_FFFF)); // addiu $t1, $zero, -1 (0xFFFFFFFF)
        // multu $t0, $t1  -> 0xFFFFFFFF * 0xFFFFFFFF (unsigned)
        cpu.Execute(new Instruction(0x0109_0019));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        ulong expected = (ulong)0xFFFF_FFFFu * 0xFFFF_FFFFu;
        Assert.Equal((uint)expected, cpu.GetRegister(2));
        Assert.Equal((uint)(expected >> 32), cpu.GetRegister(3));
    }

    [Fact]
    public void DivComputesQuotientAndRemainder()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 10
        cpu.Execute(new Instruction(0x2408_000A));
        // addiu $t1, $zero, 3
        cpu.Execute(new Instruction(0x2409_0003));
        // div $t0, $t1
        cpu.Execute(new Instruction(0x0109_001A));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(3u, cpu.GetRegister(2));
        Assert.Equal(1u, cpu.GetRegister(3));
    }

    [Fact]
    public void DivTruncatesTowardZeroForNegativeDividend()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -7
        cpu.Execute(new Instruction(0x2408_FFF9));
        // addiu $t1, $zero, 2
        cpu.Execute(new Instruction(0x2409_0002));
        // div $t0, $t1  -> -7 / 2 = -3 (trunca em direção a zero, não -4)
        cpu.Execute(new Instruction(0x0109_001A));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(unchecked((uint)-3), cpu.GetRegister(2));
        Assert.Equal(unchecked((uint)-1), cpu.GetRegister(3));
    }

    [Fact]
    public void DivByZeroWithPositiveDividendUsesHardwareDefinedResult()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 5
        cpu.Execute(new Instruction(0x2408_0005));
        // div $t0, $zero  -> divisor = 0
        cpu.Execute(new Instruction(0x0100_001A));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(2));
        Assert.Equal(5u, cpu.GetRegister(3));
    }

    [Fact]
    public void DivByZeroWithNegativeDividendUsesHardwareDefinedResult()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, -5
        cpu.Execute(new Instruction(0x2408_FFFB));
        // div $t0, $zero
        cpu.Execute(new Instruction(0x0100_001A));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(1u, cpu.GetRegister(2));
        Assert.Equal(unchecked((uint)-5), cpu.GetRegister(3));
    }

    [Fact]
    public void DivIntMinByMinusOneUsesHardwareDefinedOverflowResult()
    {
        var cpu = new R3000A();

        // lui $t0, 0x8000 -> t0 = 0x80000000 (int.MinValue)
        cpu.Execute(new Instruction(0x3C08_8000));
        // addiu $t1, $zero, -1
        cpu.Execute(new Instruction(0x2409_FFFF));
        // div $t0, $t1  -> INT_MIN / -1 (caso de overflow definido pelo hardware)
        cpu.Execute(new Instruction(0x0109_001A));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(0x8000_0000u, cpu.GetRegister(2));
        Assert.Equal(0u, cpu.GetRegister(3));
    }

    [Fact]
    public void DivuComputesUnsignedQuotientAndRemainder()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 10
        cpu.Execute(new Instruction(0x2408_000A));
        // addiu $t1, $zero, 3
        cpu.Execute(new Instruction(0x2409_0003));
        // divu $t0, $t1
        cpu.Execute(new Instruction(0x0109_001B));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(3u, cpu.GetRegister(2));
        Assert.Equal(1u, cpu.GetRegister(3));
    }

    [Fact]
    public void DivuByZeroUsesHardwareDefinedResult()
    {
        var cpu = new R3000A();

        // lui $t0, 0xABCD -> valor alto qualquer
        cpu.Execute(new Instruction(0x3C08_ABCD));
        // ori $t0, $t0, 0x1234
        cpu.Execute(new Instruction(0x3508_1234));
        // divu $t0, $zero
        cpu.Execute(new Instruction(0x0100_001B));
        // mflo $v0
        cpu.Execute(new Instruction(0x0000_1012));
        // mfhi $v1
        cpu.Execute(new Instruction(0x0000_1810));

        Assert.Equal(0xFFFF_FFFFu, cpu.GetRegister(2));
        Assert.Equal(0xABCD_1234u, cpu.GetRegister(3));
    }

    [Fact]
    public void MthiAndMtloWriteDirectlyToHiAndLo()
    {
        var cpu = new R3000A();

        // addiu $t0, $zero, 123
        cpu.Execute(new Instruction(0x2408_007B));
        // addiu $t1, $zero, 456
        cpu.Execute(new Instruction(0x2409_01C8));
        // mthi $t0
        cpu.Execute(new Instruction(0x0100_0011));
        // mtlo $t1
        cpu.Execute(new Instruction(0x0120_0013));
        // mfhi $v0
        cpu.Execute(new Instruction(0x0000_1010));
        // mflo $v1
        cpu.Execute(new Instruction(0x0000_1812));

        Assert.Equal(123u, cpu.GetRegister(2));
        Assert.Equal(456u, cpu.GetRegister(3));
    }
}
