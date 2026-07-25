namespace SadPSX.Core;

public interface IClockedDevice
{
    void Tick(uint cycles);
}
