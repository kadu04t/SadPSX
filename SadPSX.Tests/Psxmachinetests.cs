using SadPSX.Core;
using SadPSX.Core.Bios;
using SadPSX.Core.Dma;
using SadPSX.Core.Memory;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

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

    private static uint ChannelRegister(int channel, uint offset) =>
        DmaController.ChannelBaseAddress +
        (uint)(channel * 0x10) +
        offset;

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
            0x8C08_0100, // lw $t0, 0x100($zero)
            0x0000_0000  // nop (load delay)
        );
        machine.LoadBios(image);
        machine.Step();
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

    [Fact]
    public void RunTicksRegisteredDevicesWithElapsedClockCycles()
    {
        var machine = new PsxMachine();
        var device = new RecordingClockedDevice();
        machine.RegisterClockedDevice(device);
        machine.LoadBios(CreateBiosImageWithInstructions(
            0x0000_0000,
            0x0000_0000));

        machine.Run(2);

        Assert.Equal(58ul, machine.ClockCycles);
        Assert.Equal(machine.Cpu.ClockCycles, machine.Timing.ClockCycles);
        Assert.Equal(58ul, device.TotalCycles);
        Assert.Equal(2, device.TickCount);
    }

    [Fact]
    public void StepWaitsForActiveGpuLinkedListBeforeExecutingCpu()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(0));
        uint control = machine.Bus.Dma.Control | (1u << 11);
        machine.Bus.Write32(DmaController.ControlAddress, control);
        machine.Bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        machine.Bus.Ram.Write32(0x100, 0x0180_0000);
        machine.Bus.Ram.Write32(0x104, 0xE100_0000);
        machine.Bus.Write32(ChannelRegister(2, 0), 0x100);
        machine.Bus.Write32(ChannelRegister(2, 8), 0x0100_0401);

        machine.Step();

        Assert.Equal(1ul, machine.Cpu.Cycles);
        Assert.False(machine.Bus.Dma.GetChannelRuntime(2).Busy);
        Assert.Equal(machine.Cpu.ClockCycles, machine.ClockCycles);
        Assert.True(machine.ClockCycles > machine.Cpu.LastStepCycles);
    }

    [Fact]
    public void RegisterClockedDeviceDoesNotRegisterSameInstanceTwice()
    {
        var machine = new PsxMachine();
        var device = new RecordingClockedDevice();
        machine.RegisterClockedDevice(device);
        machine.RegisterClockedDevice(device);
        machine.LoadBios(CreateBiosImageWithInstructions(0x0000_0000));

        machine.Step();

        Assert.Equal(1, device.TickCount);
    }

    [Fact]
    public void ResetClearsCpuAndCentralTimingClocks()
    {
        var machine = new PsxMachine();
        machine.LoadBios(CreateBiosImageWithInstructions(0x0000_0000));
        machine.Step();

        Assert.NotEqual(0ul, machine.Cpu.ClockCycles);
        Assert.NotEqual(0ul, machine.Timing.ClockCycles);

        machine.Reset();

        Assert.Equal(0ul, machine.Cpu.ClockCycles);
        Assert.Equal(0ul, machine.Timing.ClockCycles);
    }

    private sealed class RecordingClockedDevice : IClockedDevice
    {
        public ulong TotalCycles { get; private set; }
        public int TickCount { get; private set; }

        public void Tick(uint cycles)
        {
            TotalCycles += cycles;
            TickCount++;
        }
    }
}
