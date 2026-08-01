using SadPSX.Core.CdRom;
using SadPSX.Core.CdRom.Media;
using SadPSX.Core.Dma;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;
using GpuDevice = SadPSX.Core.Gpu.Gpu;

namespace SadPSX.Tests.Dma;

public sealed class CdRomDmaTests
{
    [Fact]
    public void ReadCommandExposesMode2Payload()
    {
        var bus = new Bus();
        using var disc = new MemoryDiscImage();
        disc.Sector[24] = 0x11;
        disc.Sector[25] = 0x22;
        bus.CdRom.LoadDisc(disc);

        ReadFirstSector(bus);
        RequestData(bus);

        Assert.Equal(2048, bus.CdRom.DataCount);
        Assert.Equal(0x11, bus.Read8(CdRomController.BaseAddress + 2));
        Assert.Equal(0x22, bus.Read8(CdRomController.BaseAddress + 2));
    }

    [Fact]
    public void DmaChannelThreeCopiesCdRomWordsToRam()
    {
        var bus = new Bus();
        using var disc = new MemoryDiscImage();
        disc.Sector[24] = 0x44;
        disc.Sector[25] = 0x33;
        disc.Sector[26] = 0x22;
        disc.Sector[27] = 0x11;
        disc.Sector[28] = 0x88;
        disc.Sector[29] = 0x77;
        disc.Sector[30] = 0x66;
        disc.Sector[31] = 0x55;
        bus.CdRom.LoadDisc(disc);
        ReadFirstSector(bus);
        RequestData(bus);

        bus.Write32(
            DmaController.ControlAddress,
            bus.Dma.Control | (1u << 15));
        bus.Write32(ChannelRegister(3, 0), 0x1000);
        bus.Write32(ChannelRegister(3, 4), 0x0001_0002);

        bus.Write32(ChannelRegister(3, 8), 0x0100_0200);

        Assert.Equal(0x1122_3344u, bus.Ram.Read32(0x1000));
        Assert.Equal(0x5566_7788u, bus.Ram.Read32(0x1004));
        Assert.Equal(0x1008u, bus.Dma.GetChannel(3).BaseAddress);
        Assert.Equal(
            0u,
            bus.Dma.GetChannel(3).ChannelControl & (1u << 24));
    }

    [Fact]
    public void CdRomDmaRunsWhileGpuDmaWaitsForRequest()
    {
        var bus = new Bus();
        using var disc = new MemoryDiscImage();
        disc.Sector[24] = 0x44;
        disc.Sector[25] = 0x33;
        disc.Sector[26] = 0x22;
        disc.Sector[27] = 0x11;
        bus.CdRom.LoadDisc(disc);
        ReadFirstSector(bus);
        RequestData(bus);

        uint enabledChannels =
            bus.Dma.Control | (1u << 11) | (1u << 15);
        bus.Write32(DmaController.ControlAddress, enabledChannels);

        bus.Ram.Write32(0x2000, 0);
        bus.Write32(ChannelRegister(2, 0), 0x2000);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0001);
        bus.Write32(ChannelRegister(2, 8), 0x0100_0201);

        bus.Write32(ChannelRegister(3, 0), 0x1000);
        bus.Write32(ChannelRegister(3, 4), 0x0001_0001);
        bus.Write32(ChannelRegister(3, 8), 0x0100_0200);

        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(3).ChannelControl & (1u << 24));

        bus.Dma.Tick(1);

        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
        Assert.Equal(
            0u,
            bus.Dma.GetChannel(3).ChannelControl & (1u << 24));
        Assert.Equal(0x1122_3344u, bus.Ram.Read32(0x1000));
    }

    [Fact]
    public void CdRomDmaDoesNotPreemptActiveGpuBlock()
    {
        var bus = new Bus();
        using var disc = new MemoryDiscImage();
        disc.Sector[24] = 0x44;
        disc.Sector[25] = 0x33;
        disc.Sector[26] = 0x22;
        disc.Sector[27] = 0x11;
        bus.CdRom.LoadDisc(disc);
        ReadFirstSector(bus);
        RequestData(bus);

        uint control = bus.Dma.Control;
        control &= ~((0xFu << 8) | (0xFu << 12));
        control |= (0xFu << 8) | (0x8u << 12);
        bus.Write32(DmaController.ControlAddress, control);

        bus.Write32(GpuDevice.GpuStatusAddress, 0x0400_0002);
        bus.Ram.Write32(0x2000, 0);
        bus.Ram.Write32(0x2004, 0);
        bus.Write32(ChannelRegister(2, 0), 0x2000);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0002);
        bus.Write32(ChannelRegister(2, 8), 0x0100_0201);

        bus.Write32(ChannelRegister(3, 0), 0x1000);
        bus.Write32(ChannelRegister(3, 4), 0x0001_0001);
        bus.Write32(ChannelRegister(3, 8), 0x0100_0200);

        bus.Dma.Tick(1);

        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(3).ChannelControl & (1u << 24));
        Assert.NotEqual(
            0u,
            bus.Dma.GetChannel(2).ChannelControl & (1u << 24));
        Assert.Equal(0u, bus.Ram.Read32(0x1000));
        Assert.Equal(1, bus.Gpu.DmaFifoCount);
    }

    [Fact]
    public void RuntimeSnapshotReportsGpuRequestWaitAge()
    {
        var bus = new Bus();
        bus.Write32(
            DmaController.ControlAddress,
            bus.Dma.Control | (1u << 11));
        bus.Ram.Write32(0x2000, 0);
        bus.Write32(ChannelRegister(2, 0), 0x2000);
        bus.Write32(ChannelRegister(2, 4), 0x0001_0001);
        bus.Write32(ChannelRegister(2, 8), 0x0100_0201);

        bus.Dma.Tick(100);

        DmaChannelRuntimeSnapshot snapshot =
            bus.Dma.GetChannelRuntime(2);
        Assert.True(snapshot.Busy);
        Assert.Equal(100ul, snapshot.ActiveCycles);
        Assert.Equal(DmaGpuWaitReason.Request, bus.Dma.GpuWaitReason);
        Assert.Equal(100ul, bus.Dma.GpuWaitCycles);
    }

    private static void ReadFirstSector(Bus bus)
    {
        SetIndex(bus, 0);
        bus.Write8(CdRomController.BaseAddress + 2, 0x00);
        bus.Write8(CdRomController.BaseAddress + 2, 0x02);
        bus.Write8(CdRomController.BaseAddress + 2, 0x00);
        bus.Write8(CdRomController.BaseAddress + 1, 0x02);
        bus.CdRom.Tick(CdRomController.DefaultCommandDelayCycles);
        Acknowledge(bus);

        SetIndex(bus, 0);
        bus.Write8(CdRomController.BaseAddress + 1, 0x06);
        bus.CdRom.Tick(CdRomController.DefaultCommandDelayCycles);
        Acknowledge(bus);
        bus.CdRom.Tick(CdRomController.SingleSpeedSectorCycles);
        Assert.Equal((byte)CdRomInterruptType.DataReady, bus.CdRom.InterruptFlags);
    }

    private static void RequestData(Bus bus)
    {
        SetIndex(bus, 0);
        bus.Write8(CdRomController.BaseAddress + 3, 0x80);
    }

    private static void Acknowledge(Bus bus)
    {
        SetIndex(bus, 1);
        bus.Write8(CdRomController.BaseAddress + 3, 0x1F);
    }

    private static void SetIndex(Bus bus, byte index) =>
        bus.Write8(CdRomController.BaseAddress, index);

    private static uint ChannelRegister(int channel, uint offset) =>
        DmaController.ChannelBaseAddress + (uint)channel * 0x10 + offset;

    private sealed class MemoryDiscImage : DiscImage
    {
        public byte[] Sector { get; } = new byte[RawSectorSize];
        public override int SectorCount => 1;
        public override DiscTrackMode TrackMode => DiscTrackMode.Mode2;

        public override void ReadSector(
            int logicalBlockAddress,
            Span<byte> destination)
        {
            Assert.Equal(0, logicalBlockAddress);
            Sector.CopyTo(destination);
        }

        public override void Dispose()
        {
        }
    }
}
