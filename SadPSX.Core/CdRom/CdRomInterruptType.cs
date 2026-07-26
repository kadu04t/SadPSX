namespace SadPSX.Core.CdRom;

public enum CdRomInterruptType : byte
{
    None = 0,
    DataReady = 1,
    Complete = 2,
    Acknowledge = 3,
    DataEnd = 4,
    DiskError = 5,
}
