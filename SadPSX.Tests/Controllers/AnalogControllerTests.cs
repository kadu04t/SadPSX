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

    private static byte[] TransferPacket(
        AnalogController controller,
        byte[] packet) =>
        packet.Select(value => controller.Transfer(value).Data).ToArray();
}
