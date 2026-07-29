namespace SadPSX.Core.Controllers;

public interface IController : ISioPeripheral
{
    ushort Buttons { get; }

    bool IsPressed(ControllerButton button);
    void SetButton(ControllerButton button, bool pressed);
    void SetAxis(ControllerAxis axis, byte value);
    void ReleaseAll();
}
