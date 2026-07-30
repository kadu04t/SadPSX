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

    [Fact]
    public void SwitchingControllerPreservesCurrentInputState()
    {
        var digitalController = new DigitalController();
        var inputState = new ControllerInputState(digitalController);
        inputState.SetKeyboardButton(ControllerButton.Start, pressed: true);
        inputState.SetGamepadAxis(ControllerAxis.LeftX, 0x20);
        var analogController = new AnalogController();

        inputState.SetController(analogController);

        Assert.True(analogController.IsPressed(ControllerButton.Start));
        Assert.Equal(0x20, analogController.LeftX);
        Assert.False(digitalController.IsPressed(ControllerButton.Start));
    }

    [Fact]
    public void LeftStickAlsoDrivesDigitalDirectionalButtons()
    {
        var controller = new DigitalController();
        var inputState = new ControllerInputState(controller);

        inputState.SetGamepadAxis(ControllerAxis.LeftX, 0x20);
        inputState.SetGamepadAxis(ControllerAxis.LeftY, 0xE0);

        Assert.True(controller.IsPressed(ControllerButton.Left));
        Assert.True(controller.IsPressed(ControllerButton.Down));
        Assert.False(controller.IsPressed(ControllerButton.Right));
        Assert.False(controller.IsPressed(ControllerButton.Up));

        inputState.SetGamepadAxis(ControllerAxis.LeftX, 0x80);
        inputState.SetGamepadAxis(ControllerAxis.LeftY, 0x80);

        Assert.False(controller.IsPressed(ControllerButton.Left));
        Assert.False(controller.IsPressed(ControllerButton.Down));
    }

    [Fact]
    public void PhysicalDpadRemainsPressedWhenStickReturnsToCenter()
    {
        var controller = new DigitalController();
        var inputState = new ControllerInputState(controller);
        inputState.SetGamepadButton(ControllerButton.Left, pressed: true);
        inputState.SetGamepadAxis(ControllerAxis.LeftX, 0x20);

        inputState.SetGamepadAxis(ControllerAxis.LeftX, 0x80);

        Assert.True(controller.IsPressed(ControllerButton.Left));
    }
}
