namespace SadPSX.Core.Controllers;

public readonly record struct MemoryCardCommandTrace(
    ulong Sequence,
    byte Command,
    ushort? Sector,
    byte? ExpectedChecksum,
    byte? ReceivedChecksum,
    byte Result,
    bool Success);
