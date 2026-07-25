using SadPSX.Core.Cpu;
using Xunit;

namespace SadPSX.Tests.Cpu;

public sealed class AddiTests
{
    [Fact]
    public void AddiAddsImmediateToRegisterInNormalCase()
    {
        var cpu = new R3000A();

        // addiu $t2, zero, 100 (monta um valor inicial)
        cpu.Execute(new Instruction(0x2408_0000)); // addiu $t0, zero, 0 (placeholder neutro)

        // addi $t2, $t2, 128 -- exatamente a instrução real observada no
        // loop de zeragem de memória da BIOS (SCPH1001).
        cpu.Execute(new Instruction(0x214A_0080));

        Assert.Equal(128u, cpu.GetRegister(10));
    }

    [Fact]
    public void AddiAccumulatesAcrossMultipleExecutionsLikeABiosLoop()
    {
        var cpu = new R3000A();

        // Simula várias iterações do loop real: addi $t2, $t2, 128
        for (int i = 0; i < 5; i++)
            cpu.Execute(new Instruction(0x214A_0080));

        Assert.Equal(640u, cpu.GetRegister(10)); // 128 * 5
    }

    [Fact]
    public void AddiPositiveOverflowRaisesExceptionAndDoesNotWriteDestination()
    {
        var cpu = new R3000A();

        // lui $t0, 0x7FFF ; ori $t0, $t0, 0xFFFF -> $t0 = 0x7FFFFFFF (int.MaxValue)
        cpu.Execute(new Instruction(0x3C08_7FFF));
        cpu.Execute(new Instruction(0x3508_FFFF));

        Assert.Equal(0x7FFF_FFFFu, cpu.GetRegister(8));

        // addi $t1, $t0, 1 -> estoura int.MaxValue
        cpu.Execute(new Instruction(0x2109_0001));

        // O registrador de destino não deve ter sido modificado.
        Assert.Equal(0u, cpu.GetRegister(9));

        // A exceção deve ter sido registrada em COP0.
        uint excCode = (cpu.Cop0.Cause >> 2) & 0x1F;
        Assert.Equal(0x0Cu, excCode); // ExceptionCode.Overflow
    }

    [Fact]
    public void AddiNegativeOverflowRaisesException()
    {
        var cpu = new R3000A();

        // lui $t0, 0x8000 -> $t0 = 0x80000000 (int.MinValue)
        cpu.Execute(new Instruction(0x3C08_8000));

        // addi $t1, $t0, -1 -> estoura int.MinValue para baixo
        cpu.Execute(new Instruction(0x2109_FFFF));

        Assert.Equal(0u, cpu.GetRegister(9));

        uint excCode = (cpu.Cop0.Cause >> 2) & 0x1F;
        Assert.Equal(0x0Cu, excCode);
    }

    [Fact]
    public void AddiOverflowRedirectsPcToExceptionVector()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        // lui $t0, 0x7FFF ; ori $t0, $t0, 0xFFFF -> int.MaxValue
        cpu.Execute(new Instruction(0x3C08_7FFF));
        cpu.Execute(new Instruction(0x3508_FFFF));

        // addi $t1, $t0, 1 -> overflow
        cpu.Execute(new Instruction(0x2109_0001));

        // SR.BEV=0 por padrão -> vetor de exceção na RAM
        Assert.Equal(0x8000_0080u, cpu.Pc);
    }

    [Fact]
    public void AddiWithoutOverflowNeverRaisesException()
    {
        var cpu = new R3000A();
        cpu.Reset(0x1000);

        // addi $t0, $zero, 5000 (bem dentro do range de int32)
        cpu.Execute(new Instruction(0x2008_1388));

        Assert.Equal(5000u, cpu.GetRegister(8));

        // PC não deveria ter sido desviado para nenhum vetor de exceção.
        Assert.Equal(0x1000u, cpu.Pc);
    }
}