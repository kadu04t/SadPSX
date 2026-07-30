using SadPSX.Core.Controllers;
using Xunit;

namespace SadPSX.Tests.Controllers;

public sealed class AnalogControllerTests
{
    [Fact]
    public void AnalogPollReturnsSticksAndActiveLowButtons()
    {
        var controller = new AnalogController();
        controller.SetAnalogMode(true);
        controller.SetButton(ControllerButton.Cross, pressed: true);
        controller.SetAxis(ControllerAxis.RightX, 0x10);
        controller.SetAxis(ControllerAxis.RightY, 0x20);
        controller.SetAxis(ControllerAxis.LeftX, 0x30);
        controller.SetAxis(ControllerAxis.LeftY, 0x40);

        byte[] response = TransferPacket(
            controller,
            [0x01, 0x42, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Equal(
            [0xFF, 0x73, 0x5A, 0xFF, 0xBF, 0x10, 0x20, 0x30, 0x40],
            response);
    }

    [Fact]
    public void ConfigurationCommandsEnableAnalogMode()
    {
        var controller = new AnalogController();

        TransferPacket(controller, [0x01, 0x43, 0, 1, 0]);
        byte[] setModeResponse = TransferPacket(
            controller,
            [0x01, 0x44, 0, 1, 0, 0, 0, 0, 0]);
        TransferPacket(
            controller,
            [0x01, 0x43, 0, 0, 0, 0, 0, 0, 0]);
        byte[] pollResponse = TransferPacket(
            controller,
            [0x01, 0x42, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Equal(0xF3, setModeResponse[1]);
        Assert.True(controller.AnalogMode);
        Assert.Equal(0x73, pollResponse[1]);
    }

    [Fact]
    public void DigitalModeKeepsStandardFiveBytePacket()
    {
        var controller = new AnalogController();

        ControllerTransferResult[] response =
        [
            controller.Transfer(0x01),
            controller.Transfer(0x42),
            controller.Transfer(0),
            controller.Transfer(0),
            controller.Transfer(0),
        ];

        Assert.Equal([0xFF, 0x41, 0x5A, 0xFF, 0xFF],
            response.Select(result => result.Data));
        Assert.False(response[^1].Acknowledge);
    }

    [Fact]
    public void ConfigurationResponsesDependOnCommandArgument()
    {
        var controller = new AnalogController();
        TransferPacket(controller, [0x01, 0x43, 0, 1, 0]);

        byte[] first = TransferPacket(
            controller,
            [0x01, 0x46, 0, 0, 0, 0, 0, 0, 0]);
        byte[] second = TransferPacket(
            controller,
            [0x01, 0x46, 0, 1, 0, 0, 0, 0, 0]);
        byte[] actuator = TransferPacket(
            controller,
            [0x01, 0x4C, 0, 1, 0, 0, 0, 0, 0]);

        Assert.Equal(
            [0xFF, 0xF3, 0x5A, 0, 0, 1, 2, 0, 10],
            first);
        Assert.Equal(
            [0xFF, 0xF3, 0x5A, 0, 0, 1, 1, 1, 20],
            second);
        Assert.Equal(
            [0xFF, 0xF3, 0x5A, 0, 0, 0, 7, 0, 0],
            actuator);
    }

    [Fact]
    public void RumbleMappingControlsPollInputBytes()
    {
        var controller = new AnalogController();
        TransferPacket(controller, [0x01, 0x43, 0, 1, 0]);

        byte[] mappingResponse = TransferPacket(
            controller,
            [0x01, 0x4D, 0, 0, 1, 0xFF, 0xFF, 0xFF, 0xFF]);
        TransferPacket(
            controller,
            [0x01, 0x43, 0, 0, 0, 0, 0, 0, 0]);
        TransferPacket(controller, [0x01, 0x42, 0, 1, 0x80]);

        Assert.Equal(
            [0xFF, 0xF3, 0x5A, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            mappingResponse);
        Assert.Equal(1, controller.SmallMotor);
        Assert.Equal(0x80, controller.LargeMotor);
    }

    [Fact]
    public void RumbleMappingCanExtendDigitalPacket()
    {
        var controller = new AnalogController();
        TransferPacket(controller, [0x01, 0x43, 0, 1, 0]);
        TransferPacket(
            controller,
            [0x01, 0x4D, 0, 0xFF, 0xFF, 0, 1, 0xFF, 0xFF]);
        TransferPacket(
            controller,
            [0x01, 0x43, 0, 0, 0, 0, 0, 0, 0]);

        ControllerTransferResult[] response =
        [
            controller.Transfer(0x01),
            controller.Transfer(0x42),
            controller.Transfer(0),
            controller.Transfer(0),
            controller.Transfer(0),
            controller.Transfer(1),
            controller.Transfer(0x70),
        ];

        Assert.Equal(0x42, response[1].Data);
        Assert.False(response[^1].Acknowledge);
        Assert.Equal(1, controller.SmallMotor);
        Assert.Equal(0x70, controller.LargeMotor);
    }

    private static byte[] TransferPacket(
        AnalogController controller,
        byte[] packet) =>
        packet.Select(value => controller.Transfer(value).Data).ToArray();
}
