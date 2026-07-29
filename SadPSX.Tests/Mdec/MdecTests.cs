using Xunit;
using Bus = SadPSX.Core.Bus.Bus;
using MdecDevice = SadPSX.Core.Mdec.Mdec;

namespace SadPSX.Tests.Mdec;

public sealed class MdecTests
{
    [Fact]
    public void ResetExposesIdleStatusAndControlEnablesDmaRequests()
    {
        var bus = new Bus();

        Assert.Equal(0x8004_0000u, bus.Read32(MdecDevice.StatusAddress));

        bus.Write32(MdecDevice.DataAddress, 0x2800_0001);
        bus.Write32(MdecDevice.StatusAddress, 0x6000_0000);

        uint inputStatus = bus.Read32(MdecDevice.StatusAddress);
        Assert.NotEqual(0u, inputStatus & (1u << 29));
        Assert.NotEqual(0u, inputStatus & (1u << 28));

        bus.Write32(MdecDevice.DataAddress, 0xFE00_0040);

        uint outputStatus = bus.Read32(MdecDevice.StatusAddress);
        Assert.Equal(0u, outputStatus & (1u << 29));
        Assert.NotEqual(0u, outputStatus & (1u << 27));
        Assert.Equal(16, bus.Mdec.OutputWordCount);
    }

    [Fact]
    public void QuantAndScaleCommandsLoadTheirTables()
    {
        var bus = new Bus();

        bus.Write32(MdecDevice.DataAddress, 0x4000_0001);
        for (uint word = 0; word < 32; word++)
        {
            uint value =
                word * 4 |
                ((word * 4 + 1) << 8) |
                ((word * 4 + 2) << 16) |
                ((word * 4 + 3) << 24);
            bus.Write32(MdecDevice.DataAddress, value);
        }

        Assert.Equal(
            Enumerable.Range(0, 64).Select(value => (byte)value),
            bus.Mdec.GetQuantTable(color: false));
        Assert.Equal(
            Enumerable.Range(64, 64).Select(value => (byte)value),
            bus.Mdec.GetQuantTable(color: true));

        bus.Write32(MdecDevice.DataAddress, 0x6000_0000);
        for (uint word = 0; word < 32; word++)
        {
            uint first = word * 2;
            bus.Write32(
                MdecDevice.DataAddress,
                first | ((first + 1) << 16));
        }

        Assert.Equal(
            Enumerable.Range(0, 64).Select(value => (short)value),
            bus.Mdec.GetScaleTable());
        Assert.Equal(
            0u,
            bus.Read32(MdecDevice.StatusAddress) & 0x0780_0000);
    }

    [Fact]
    public void DecodeCommandProducesUnsignedEightBitPixels()
    {
        var bus = new Bus();

        bus.Write32(MdecDevice.DataAddress, 0x2800_0001);
        bus.Write32(MdecDevice.DataAddress, 0xFE00_0040);

        Assert.Equal(16, bus.Mdec.OutputWordCount);
        for (int word = 0; word < 16; word++)
            Assert.Equal(0x9090_9090u, bus.Read32(MdecDevice.DataAddress));
    }

    [Fact]
    public void DecodeCommandProducesNeutralFifteenBitMacroblock()
    {
        var bus = new Bus();

        bus.Write32(MdecDevice.DataAddress, 0x3800_0006);
        for (int block = 0; block < 6; block++)
            bus.Write32(MdecDevice.DataAddress, 0xFE00_0000);

        Assert.Equal(128, bus.Mdec.OutputWordCount);
        for (int word = 0; word < 128; word++)
            Assert.Equal(0x4210_4210u, bus.Read32(MdecDevice.DataAddress));
    }

    [Fact]
    public void TwentyFourBitOutputUsesPackedBlueGreenRedBytes()
    {
        var bus = new Bus();

        bus.Write32(MdecDevice.DataAddress, 0x3000_0006);
        bus.Write32(MdecDevice.DataAddress, 0xFE00_0000);
        bus.Write32(MdecDevice.DataAddress, 0xFE00_0040);
        for (int block = 0; block < 4; block++)
            bus.Write32(MdecDevice.DataAddress, 0xFE00_0000);

        Assert.Equal(192, bus.Mdec.OutputWordCount);
        Assert.Equal(0x9C80_7B9Cu, bus.Read32(MdecDevice.DataAddress));
    }

    [Fact]
    public void DecodeUsesTheUploadedScaleTable()
    {
        var bus = new Bus();

        bus.Write32(MdecDevice.DataAddress, 0x6000_0000);
        for (int word = 0; word < 32; word++)
            bus.Write32(MdecDevice.DataAddress, 0);

        bus.Write32(MdecDevice.DataAddress, 0x2800_0001);
        bus.Write32(MdecDevice.DataAddress, 0xFE00_0040);

        Assert.Equal(16, bus.Mdec.OutputWordCount);
        for (int word = 0; word < 16; word++)
            Assert.Equal(0x8080_8080u, bus.Read32(MdecDevice.DataAddress));
    }

    [Fact]
    public void StatusTracksColorOutputBlockUntilTheFifoDrains()
    {
        var bus = new Bus();

        bus.Write32(MdecDevice.DataAddress, 0x3800_0006);
        for (int block = 0; block < 6; block++)
            bus.Write32(MdecDevice.DataAddress, 0xFE00_0000);

        Assert.Equal(0u, (bus.Mdec.Status >> 16) & 7);
        for (int word = 0; word < 32; word++)
            bus.Mdec.ReadDmaWord();
        Assert.Equal(1u, (bus.Mdec.Status >> 16) & 7);
        while (bus.Mdec.OutputWordCount > 0)
            bus.Mdec.ReadDmaWord();
        Assert.Equal(4u, (bus.Mdec.Status >> 16) & 7);
    }
}
