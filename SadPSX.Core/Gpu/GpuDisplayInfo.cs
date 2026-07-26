namespace SadPSX.Core.Gpu;

public readonly record struct GpuDisplayInfo(
    int VramX,
    int VramY,
    int Width,
    int Height,
    bool Enabled,
    bool Is24BitColor,
    bool IsPal,
    bool IsInterlaced);
