using SadPSX.Core.Dma;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Mdec;

public sealed class MdecDmaTests
{
    [Fact]
    public void DmaChannelsDecodeRamDataBackIntoRam()
    {
        var bus = new Bus();
        bus.Ram.Write32(0, 0x2800_0001);
        bus.Ram.Write32(4, 0xFE00_0040);

        EnableChannel(bus, 0);
        StartDma(
            bus,
            channel: 0,
            ramAddress: 0,
            words: 2,
            fromRam: true);

        Assert.Equal(16, bus.Mdec.OutputWordCount);

        EnableChannel(bus, 1);
        StartDma(
            bus,
            channel: 1,
            ramAddress: 0x100,
            words: 16,
            fromRam: false);

        for (uint address = 0x100; address < 0x140; address += 4)
            Assert.Equal(0x9090_9090u, bus.Ram.Read32(address));
        Assert.Equal(0, bus.Mdec.OutputWordCount);
        Assert.Equal(2ul, bus.Dma.CompletedTransfers);
    }

    private static void EnableChannel(Bus bus, int channel)
    {
        bus.Write32(
            DmaController.ControlAddress,
            bus.Dma.Control | (8u << (channel * 4)));
    }

    private static void StartDma(
        Bus bus,
        int channel,
        uint ramAddress,
        ushort words,
        bool fromRam)
    {
        uint channelBase =
            DmaController.ChannelBaseAddress + (uint)(channel * 0x10);
        bus.Write32(channelBase, ramAddress);
        bus.Write32(channelBase + 4, words);
        bus.Write32(
            channelBase + 8,
            (1u << 24) |
            (1u << 28) |
            (fromRam ? 1u : 0u));
    }
}
