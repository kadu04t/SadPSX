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

        Sio0TransferTrace[] trace = [.. bus.Sio0.TransferHistory];
        Assert.Equal(5, trace.Length);
        Assert.All(
            trace,
            entry =>
            {
                Assert.Equal(1, entry.Port);
                Assert.Equal(
                    Sio0PeripheralKind.Controller,
                    entry.Peripheral);
                Assert.True(entry.Connected);
                Assert.Equal(trace[0].Transaction, entry.Transaction);
            });
        Assert.Equal([0, 1, 2, 3, 4],
            trace.Select(entry => entry.ByteIndex));
        Assert.Equal(0x01, trace[0].Transmit);
        Assert.Equal(0xFF, trace[0].Receive);
        Assert.Equal(0ul, trace[0].StartCycle);
        Assert.Equal(1088ul, trace[0].EndCycle);
        Assert.Equal(1188ul, trace[0].AcknowledgeCycle);
    }

    [Fact]
    public void DsrInterruptRequestsControllerIrq()
    {
        var bus = CreateConfiguredBus(control: 0x1003);

        bus.Write8(Sio0.DataAddress, 0x01);
        bus.Sio0.Tick(1088);

        Assert.False(bus.Sio0.DsrAsserted);
        Assert.False(bus.Sio0.InterruptRequest);

        bus.Sio0.Tick(100);

        Assert.True(bus.Sio0.InterruptRequest);
        Assert.True(bus.Sio0.DsrAsserted);
        Assert.NotEqual(0, bus.InterruptController.Status &
                           (1 << (int)InterruptSource.Controller));
        Assert.NotEqual(0u, bus.Read32(Sio0.StatusAddress) & (1u << 9));

        bus.Write16(Sio0.ControlAddress, 0x1013);
        Assert.False(bus.Sio0.InterruptRequest);
    }

    [Fact]
    public void TransmitRegisterQueuesTheNextByte()
    {
        var bus = CreateConfiguredBus();

        bus.Write8(Sio0.DataAddress, 0x01);
        bus.Sio0.Tick(1);

        Assert.NotEqual(0u, bus.Read32(Sio0.StatusAddress) & 1);

        bus.Write8(Sio0.DataAddress, 0x42);

        Assert.Equal(0u, bus.Read32(Sio0.StatusAddress) & 1);

        bus.Sio0.Tick(3000);

        Assert.Equal(2, bus.Sio0.ReceiveCount);
        Assert.Equal(0xFF, bus.Read8(Sio0.DataAddress));
        Assert.Equal(0x41, bus.Read8(Sio0.DataAddress));
        Assert.False(bus.Sio0.TransferHistory.First().Queued);
        Assert.True(bus.Sio0.TransferHistory.Last().Queued);
    }

    [Fact]
    public void SelectingPortTwoBehavesAsDisconnected()
    {
        var bus = CreateConfiguredBus(control: 0x2003);

        byte response = Transfer(bus, 0x01);

        Assert.Equal(0xFF, response);
        Assert.False(bus.Sio0.DsrAsserted);
    }

    [Fact]
    public void DeviceAddressRoutesMemoryCardOnSharedPort()
    {
        var bus = CreateConfiguredBus();

        byte[] response =
        [
            Transfer(bus, 0x81),
            Transfer(bus, 0x53),
            Transfer(bus, 0),
            Transfer(bus, 0),
            Transfer(bus, 0),
            Transfer(bus, 0),
            Transfer(bus, 0),
            Transfer(bus, 0),
            Transfer(bus, 0),
            Transfer(bus, 0),
        ];

        Assert.Equal(
            [0xFF, 0x08, 0x5A, 0x5D, 0x5C, 0x5D, 0x04, 0, 0, 0x80],
            response);
    }

    [Fact]
    public void PortTwoCanHostIndependentController()
    {
        var bus = CreateConfiguredBus(control: 0x2003);
        var controller = new DigitalController();
        controller.SetButton(ControllerButton.Start, true);
        bus.Sio0.AttachController(2, controller);

        byte[] response =
        [
            Transfer(bus, 0x01),
            Transfer(bus, 0x42),
            Transfer(bus, 0),
            Transfer(bus, 0),
            Transfer(bus, 0),
        ];

        Assert.Equal([0xFF, 0x41, 0x5A, 0xF7, 0xFF], response);
        Assert.All(
            bus.Sio0.TransferHistory,
            entry => Assert.Equal(2, entry.Port));
    }

    [Fact]
    public void DataWordReadDequeuesFourReceiveBytes()
    {
        var bus = CreateConfiguredBus();
        QueueTransfer(bus, 0x01);
        QueueTransfer(bus, 0x42);
        QueueTransfer(bus, 0x00);
        QueueTransfer(bus, 0x00);

        uint response = bus.Read32(Sio0.DataAddress);

        Assert.Equal(0xFF5A41FFu, response);
        Assert.Equal(0, bus.Sio0.ReceiveCount);
    }

    [Fact]
    public void DataHalfWordReadDequeuesOneReceiveByte()
    {
        var bus = CreateConfiguredBus();
        QueueTransfer(bus, 0x01);
        QueueTransfer(bus, 0x42);

        ushort response = bus.Read16(Sio0.DataAddress);

        Assert.Equal(0x41FF, response);
        Assert.Equal(1, bus.Sio0.ReceiveCount);
        Assert.Equal(0x41, bus.Read8(Sio0.DataAddress));
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

    private static void QueueTransfer(Bus bus, byte value)
    {
        bus.Write8(Sio0.DataAddress, value);
        bus.Sio0.Tick(2000);
    }
}
