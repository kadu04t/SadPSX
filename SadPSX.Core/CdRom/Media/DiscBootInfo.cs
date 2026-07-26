namespace SadPSX.Core.CdRom.Media;

public sealed record DiscBootInfo(
    string ExecutablePath,
    int LogicalBlockAddress,
    int FileSize);
