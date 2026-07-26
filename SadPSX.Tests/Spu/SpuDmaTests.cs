using SadPSX.Core.Dma;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;
using SpuDevice = SadPSX.Core.Spu.Spu;

namespace SadPSX.Tests.Spu;

public sealed class SpuDmaTests
{
    private const uint Channel4Base = DmaController.ChannelBaseAddress + 0x40;

    [Fact]
    public void Dma4WritesRamWordsIntoSoundRam()
    {
        var bus = new Bus();
        bus.Ram.Write32(0, 0x4433_2211);
        bus.Write16(SpuDevice.TransferAddressRegister, 1);

        StartDma(bus, fromRam: true, ramAddress: 0);

        Assert.Equal(0x11, bus.Spu.SoundRam[8]);
        Assert.Equal(0x22, bus.Spu.SoundRam[9]);
        Assert.Equal(0x33, bus.Spu.SoundRam[10]);
        Assert.Equal(0x44, bus.Spu.SoundRam[11]);
    }

    [Fact]
    public void Dma4ReadsSoundRamWordsIntoRam()
    {
        var bus = new Bus();
        bus.Spu.SoundRam[8] = 0x78;
        bus.Spu.SoundRam[9] = 0x56;
        bus.Spu.SoundRam[10] = 0x34;
        bus.Spu.SoundRam[11] = 0x12;
        bus.Write16(SpuDevice.TransferAddressRegister, 1);

        StartDma(bus, fromRam: false, ramAddress: 4);

        Assert.Equal(0x1234_5678u, bus.Ram.Read32(4));
    }

    private static void StartDma(Bus bus, bool fromRam, uint ramAddress)
    {
        bus.Write32(
            DmaController.ControlAddress,
            bus.Dma.Control | (8u << 16));
        bus.Write32(Channel4Base, ramAddress);
        bus.Write32(Channel4Base + 4, 1);
        uint channelControl =
            (1u << 24) |
            (1u << 28) |
            (fromRam ? 1u : 0u);
        bus.Write32(Channel4Base + 8, channelControl);
    }
}
