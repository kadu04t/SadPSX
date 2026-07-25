using SadPSX.Core;
using SadPSX.Core.Memory;
using Xunit;

namespace SadPSX.Tests;

public sealed class PsxMachineTests
{
    private static byte[] CreateBiosImageWithInstructions(params uint[] instructions)
    {
        var image = new byte[BiosRom.SizeInBytes];

        for (int i = 0; i < instructions.Length; i++)
        {
            int offset = i * 4;
            uint word = instructions[i];

            // Little-endian, igual ao restante do sistema.
            image[offset] = (byte)word;
            image[offset + 1] = (byte)(word >> 8);
            image[offset + 2] = (byte)(word >> 16);
            image[offset + 3] = (byte)(word >> 24);
        }

        return image;
    }

    [Fact]
    public void LoadBiosResetsCpuToBiosResetVector()
    {
        var machine = new PsxMachine();
        var image = CreateBiosImageWithInstructions(0x2408_1234);

        machine.LoadBios(image);

        Assert.Equal(0xBFC0_0000u, machine.Cpu.Pc);
    }

    [Fact]
    public void StepExecutesRealInstructionsLoadedFromBios()
    {
        var machine = new PsxMachine();

        // addiu $t0, zero, 0x1234
        // addiu $t1, zero, 0x5678
        var image = CreateBiosImageWithInstructions(0x2408_1234, 0x2409_5678);

        machine.LoadBios(image);

        machine.Step();
        Assert.Equal(0x1234u, machine.Cpu.GetRegister(8));

        machine.Step();
        Assert.Equal(0x5678u, machine.Cpu.GetRegister(9));
    }

    [Fact]
    public void RunExecutesExactlyTheRequestedNumberOfSteps()
    {
        var machine = new PsxMachine();

        var image = CreateBiosImageWithInstructions(
            0x2408_0001, // addiu $t0, zero, 1
            0x2408_0002, // addiu $t0, zero, 2
            0x2408_0003, // addiu $t0, zero, 3
            0x2408_002A  // addiu $t0, zero, 42 (não deve rodar)
        );

        machine.LoadBios(image);
        machine.Run(3);

        // Apenas as 3 primeiras instruções rodaram; $t0 reflete a última
        // delas (a quarta, que setaria 42, nunca executou).
        Assert.Equal(3u, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void RunUntilStopsAsSoonAsConditionBecomesTrue()
    {
        var machine = new PsxMachine();

        var image = CreateBiosImageWithInstructions(
            0x2408_0001, // addiu $t0, zero, 1
            0x2408_0063, // addiu $t0, zero, 99  <- queremos parar logo após isso
            0x2408_0002  // addiu $t0, zero, 2   (não deve rodar)
        );

        machine.LoadBios(image);

        bool reached = machine.RunUntil(
            m => m.Cpu.GetRegister(8) == 99,
            maxSteps: 10);

        Assert.True(reached);
        Assert.Equal(99u, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void RunUntilReturnsFalseWhenMaxStepsIsReachedWithoutCondition()
    {
        var machine = new PsxMachine();

        var image = CreateBiosImageWithInstructions(0x2408_0001); // addiu $t0, zero, 1

        machine.LoadBios(image);

        bool reached = machine.RunUntil(
            m => m.Cpu.GetRegister(8) == 999, // nunca vai acontecer
            maxSteps: 5);

        Assert.False(reached);
    }

    [Fact]
    public void ResetZeroesRegistersButPreservesBiosContent()
    {
        var machine = new PsxMachine();

        var image = CreateBiosImageWithInstructions(0x2408_002A); // addiu $t0, zero, 42

        machine.LoadBios(image);
        machine.Step();

        Assert.Equal(42u, machine.Cpu.GetRegister(8));

        machine.Reset();

        Assert.Equal(0u, machine.Cpu.GetRegister(8));
        Assert.Equal(0xBFC0_0000u, machine.Cpu.Pc);

        // A BIOS não foi apagada pelo Reset: a mesma instrução deve
        // produzir o mesmo resultado ao ser executada novamente.
        machine.Step();
        Assert.Equal(42u, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void CpuAndBusShareTheSameMemoryInstance()
    {
        var machine = new PsxMachine();

        // Escreve diretamente no Bus e confirma que a CPU, ao executar um
        // LW a partir do mesmo endereço, enxerga o valor escrito — provando
        // que ambos operam sobre a mesma instância de memória.
        machine.Bus.Write32(0x0000_0100, 0xDEAD_BEEF);

        var image = CreateBiosImageWithInstructions(
            0x8C08_0100 // lw $t0, 0x100($zero)
        );
        machine.LoadBios(image);
        machine.Step();

        Assert.Equal(0xDEAD_BEEFu, machine.Cpu.GetRegister(8));
    }

    [Fact]
    public void ConstructorWithExternalBusUsesTheProvidedInstance()
    {
        var bus = new Bus();
        var machine = new PsxMachine(bus);

        Assert.Same(bus, machine.Bus);
    }
}