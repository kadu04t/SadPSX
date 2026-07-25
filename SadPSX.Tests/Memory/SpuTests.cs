using SadPSX.Core.Memory;
using Xunit;

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
        var spu = new Spu();

        spu.Write16(Spu.ControlRegister, 0x0020);
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
        var spu = new Spu();
        spu.Write16(Spu.ControlRegister, 0x0030);
        spu.Tick(2);

        spu.Write16(Spu.ControlRegister, 0);
        spu.Tick(2);

        Assert.Equal(0, spu.Status & 0x03BF);
    }

    [Fact]
    public void ManualWriteFlushesFifoIntoSoundRam()
    {
        var spu = new Spu();
        spu.Write16(Spu.TransferAddressRegister, 1);
        spu.Write16(Spu.TransferFifoRegister, 0x1234);

        spu.Write16(Spu.ControlRegister, 0x0010);
        spu.Tick(2);

        Assert.Equal(0x34, spu.SoundRam[8]);
        Assert.Equal(0x12, spu.SoundRam[9]);
    }

    [Fact]
    public void StatusRegisterIgnoresSoftwareWrites()
    {
        var spu = new Spu();

        spu.Write16(Spu.StatusRegister, 0xFFFF);

        Assert.Equal(0, spu.Status);
    }
}
