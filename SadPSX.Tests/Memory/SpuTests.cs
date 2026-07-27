using Xunit;
using SadPSX.Core.Interrupts;
using Bus = SadPSX.Core.Bus.Bus;
using SpuDevice = SadPSX.Core.Spu.Spu;

namespace SadPSX.Tests.Memory;

public sealed class SpuTests
{
    [Fact]
    public void RegistersRoundTripThroughBusAndAreHandled()
    {
        var bus = new Bus();

        bus.Write16(0x1F80_1C04, 0x3FFF);
        ushort value = bus.Read16(0x1F80_1C04);

        Assert.Equal(0x3FFF, value);
        Assert.All(
            bus.Mmio.AccessSummaries,
            summary => Assert.True(summary.Handled));
    }

    [Fact]
    public void ControlModeAppearsInStatusAfterShortDelay()
    {
        var spu = new SpuDevice();

        spu.Write16(SpuDevice.ControlRegister, 0x0020);
        spu.Tick(1);
        Assert.Equal(0, spu.Status & 0x003F);

        spu.Tick(1);

        Assert.Equal(0x0020, spu.Status & 0x003F);
        Assert.NotEqual(0, spu.Status & (1 << 7));
        Assert.NotEqual(0, spu.Status & (1 << 8));
    }

    [Fact]
    public void StopModeClearsTransferRequestBits()
    {
        var spu = new SpuDevice();
        spu.Write16(SpuDevice.ControlRegister, 0x0030);
        spu.Tick(2);

        spu.Write16(SpuDevice.ControlRegister, 0);
        spu.Tick(2);

        Assert.Equal(0, spu.Status & 0x03BF);
    }

    [Fact]
    public void ManualWriteFlushesFifoIntoSoundRam()
    {
        var spu = new SpuDevice();
        spu.Write16(SpuDevice.TransferAddressRegister, 1);
        spu.Write16(SpuDevice.TransferFifoRegister, 0x1234);

        spu.Write16(SpuDevice.ControlRegister, 0x0010);
        spu.Tick(2);

        Assert.Equal(0x34, spu.SoundRam[8]);
        Assert.Equal(0x12, spu.SoundRam[9]);
    }

    [Fact]
    public void StatusRegisterIgnoresSoftwareWrites()
    {
        var spu = new SpuDevice();

        spu.Write16(SpuDevice.StatusRegister, 0xFFFF);

        Assert.Equal(0, spu.Status);
    }

    [Fact]
    public void SoundRamTransferAtIrqAddressRaisesSpuInterrupt()
    {
        var bus = new Bus();
        bus.Write16(SpuDevice.InterruptAddressRegister, 1);
        bus.Write16(SpuDevice.TransferAddressRegister, 1);
        bus.Write16(SpuDevice.ControlRegister, 0x8040);

        bus.Spu.WriteDmaWord(0x1234_5678);

        Assert.NotEqual(0, bus.Spu.Status & (1 << 6));
        Assert.NotEqual(
            0,
            bus.InterruptController.Status &
            (1 << (int)InterruptSource.Spu));
    }

    [Fact]
    public void DisablingSpuIrqClearsItsStatusFlag()
    {
        var bus = new Bus();
        bus.Write16(SpuDevice.InterruptAddressRegister, 1);
        bus.Write16(SpuDevice.TransferAddressRegister, 1);
        bus.Write16(SpuDevice.ControlRegister, 0x8040);
        bus.Spu.WriteDmaWord(0x1234_5678);

        bus.Write16(SpuDevice.ControlRegister, 0x8000);

        Assert.Equal(0, bus.Spu.Status & (1 << 6));
    }

    [Fact]
    public void CaptureWriteAtIrqAddressRaisesSpuInterrupt()
    {
        var bus = new Bus();
        bus.Write16(SpuDevice.InterruptAddressRegister, 0);
        bus.Write16(SpuDevice.TransferControlRegister, 0x0004);
        bus.Write16(SpuDevice.ControlRegister, 0x8040);

        bus.Spu.Tick(SpuDevice.CpuCyclesPerSample);

        Assert.NotEqual(0, bus.Spu.Status & (1 << 6));
        Assert.NotEqual(
            0,
            bus.InterruptController.Status &
            (1 << (int)InterruptSource.Spu));
    }
}
