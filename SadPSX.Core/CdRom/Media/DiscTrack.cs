namespace SadPSX.Core.CdRom.Media;

public readonly record struct DiscTrack(
    byte Number,
    int StartLogicalBlockAddress,
    DiscTrackMode Mode);
