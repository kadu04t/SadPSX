namespace SadPSX.Core.Controllers;

public interface ISioPeripheral
{
    ControllerTransferResult Transfer(byte value);
    void ResetTransfer();
}

public readonly record struct ControllerTransferResult(
    byte Data,
    bool Acknowledge);
