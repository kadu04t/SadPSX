using SadPSX.Core.Controllers;
using SadPSX.Frontend.Input;
using Xunit;

namespace SadPSX.Tests.Controllers;

public sealed class ControllerInputStateTests
{
    [Fact]
    public void KeyboardAndGamepadStatesAreCombined()
    {
        var controller = new DigitalController();
        var inputState = new ControllerInputState(controller);

        inputState.SetKeyboardButton(ControllerButton.Cross, pressed: true);
        inputState.SetGamepadButton(ControllerButton.Cross, pressed: true);
        inputState.SetKeyboardButton(ControllerButton.Cross, pressed: false);

        Assert.True(controller.IsPressed(ControllerButton.Cross));

        inputState.ReleaseGamepad();

        Assert.False(controller.IsPressed(ControllerButton.Cross));
    }

    [Fact]
    public void DisconnectOnlyReleasesGamepadButtons()
    {
        var controller = new DigitalController();
        var inputState = new ControllerInputState(controller);

        inputState.SetKeyboardButton(ControllerButton.Start, pressed: true);
        inputState.SetGamepadButton(ControllerButton.Cross, pressed: true);

        inputState.ReleaseGamepad();

        Assert.True(controller.IsPressed(ControllerButton.Start));
        Assert.False(controller.IsPressed(ControllerButton.Cross));
    }

    [Fact]
    public void GamepadAxesReachAnalogControllerAndResetOnDisconnect()
    {
        var controller = new AnalogController();
        var inputState = new ControllerInputState(controller);

        inputState.SetGamepadAxis(ControllerAxis.LeftX, 0x20);
        inputState.SetGamepadAxis(ControllerAxis.RightY, 0xE0);

        Assert.Equal(0x20, controller.LeftX);
        Assert.Equal(0xE0, controller.RightY);

        inputState.ReleaseGamepad();

        Assert.Equal(0x80, controller.LeftX);
        Assert.Equal(0x80, controller.RightY);
    }
}
