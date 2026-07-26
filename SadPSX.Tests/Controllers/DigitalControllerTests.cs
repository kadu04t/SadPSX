using SadPSX.Core.Controllers;
using Xunit;

namespace SadPSX.Tests.Controllers;

public sealed class DigitalControllerTests
{
    [Fact]
    public void PollReturnsDigitalIdAndActiveLowButtons()
    {
        var controller = new DigitalController();
        controller.SetButton(ControllerButton.Start, pressed: true);
        controller.SetButton(ControllerButton.Cross, pressed: true);

        ControllerTransferResult[] replies =
        [
            controller.Transfer(0x01),
            controller.Transfer(0x42),
            controller.Transfer(0x00),
            controller.Transfer(0x00),
            controller.Transfer(0x00),
        ];

        Assert.Equal(
            [0xFF, 0x41, 0x5A, 0xF7, 0xBF],
            replies.Select(reply => reply.Data));
        Assert.True(replies[0].Acknowledge);
        Assert.True(replies[3].Acknowledge);
        Assert.False(replies[4].Acknowledge);
    }

    [Fact]
    public void ReleasingButtonsRestoresAllBits()
    {
        var controller = new DigitalController();
        controller.SetButton(ControllerButton.Left, pressed: true);
        controller.SetButton(ControllerButton.Square, pressed: true);

        controller.ReleaseAll();

        Assert.Equal(ushort.MaxValue, controller.Buttons);
    }

    [Fact]
    public void InvalidAddressDoesNotBeginPoll()
    {
        var controller = new DigitalController();

        ControllerTransferResult reply = controller.Transfer(0x81);

        Assert.Equal(0xFF, reply.Data);
        Assert.False(reply.Acknowledge);
    }
}
