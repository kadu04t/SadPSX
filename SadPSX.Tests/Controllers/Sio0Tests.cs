using SadPSX.Core.Controllers;
using SadPSX.Core.Interrupts;
using Xunit;
using Bus = SadPSX.Core.Bus.Bus;

namespace SadPSX.Tests.Controllers;

public sealed class Sio0Tests
{
    [Fact]
    public void RegistersRoundTripThroughMmio()
    {
        var bus = new Bus();

        bus.Write16(Sio0.ModeAddress, 0x000D);
        bus.Write16(Sio0.ControlAddress, 0x0003);
        bus.Write16(Sio0.BaudAddress, 0x0088);

        Assert.Equal(0x000D, bus.Read16(Sio0.ModeAddress));
        Assert.Equal(0x0003, bus.Read16(Sio0.ControlAddress));
        Assert.Equal(0x0088, bus.Read16(Sio0.BaudAddress));
        Assert.DoesNotContain(
            bus.Mmio.AccessSummaries,
            summary => !summary.Handled);
    }

    [Fact]
    public void SerialPollReturnsControllerPacket()
    {
        var bus = CreateConfiguredBus();
        bus.Sio0.ControllerPort1.SetButton(ControllerButton.Cross, true);

        byte[] response =
        [
            Transfer(bus, 0x01),
            Transfer(bus, 0x42),
            Transfer(bus, 0x00),
            Transfer(bus, 0x00),
            Transfer(bus, 0x00),
        ];

        Assert.Equal([0xFF, 0x41, 0x5A, 0xFF, 0xBF], response);
    }

    [Fact]
    public void DsrInterruptRequestsControllerIrq()
    {
        var bus = CreateConfiguredBus(control: 0x1003);

        bus.Write8(Sio0.DataAddress, 0x01);
        bus.Sio0.Tick(2000);

        Assert.True(bus.Sio0.InterruptRequest);
        Assert.NotEqual(0, bus.InterruptController.Status &
                           (1 << (int)InterruptSource.Controller));
        Assert.NotEqual(0u, bus.Read32(Sio0.StatusAddress) & (1u << 9));

        bus.Write16(Sio0.ControlAddress, 0x1013);
        Assert.False(bus.Sio0.InterruptRequest);
    }

    [Fact]
    public void SelectingPortTwoBehavesAsDisconnected()
    {
        var bus = CreateConfiguredBus(control: 0x2003);

        byte response = Transfer(bus, 0x01);

        Assert.Equal(0xFF, response);
        Assert.False(bus.Sio0.DsrAsserted);
    }

    private static Bus CreateConfiguredBus(ushort control = 0x0003)
    {
        var bus = new Bus();
        bus.Write16(Sio0.ModeAddress, 0x000D);
        bus.Write16(Sio0.BaudAddress, 0x0088);
        bus.Write16(Sio0.ControlAddress, control);
        return bus;
    }

    private static byte Transfer(Bus bus, byte value)
    {
        bus.Write8(Sio0.DataAddress, value);
        bus.Sio0.Tick(2000);
        return bus.Read8(Sio0.DataAddress);
    }
}
