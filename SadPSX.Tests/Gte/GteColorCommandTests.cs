using Xunit;

namespace SadPSX.Tests.Gte;

public sealed class GteColorCommandTests
{
    private const uint ShiftFraction = 1u << 19;
    private const uint LimitMode = 1u << 10;

    [Fact]
    public void DpcsAndIntplInterpolateTowardFarColor()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteControlRegister(21, 0x0400);
        gte.WriteControlRegister(22, 0x0500);
        gte.WriteControlRegister(23, 0x0600);
        gte.WriteDataRegister(6, 0x2C30_2010);
        gte.WriteDataRegister(8, 0x1000);

        Assert.True(gte.ExecuteCommand(ShiftFraction | LimitMode | 0x10));
        Assert.Equal(0x2C60_5040u, gte.ReadDataRegister(22));

        gte.WriteDataRegister(9, 0x0100);
        gte.WriteDataRegister(10, 0x0200);
        gte.WriteDataRegister(11, 0x0300);

        Assert.True(gte.ExecuteCommand(ShiftFraction | LimitMode | 0x11));
        Assert.Equal(0x2C60_5040u, gte.ReadDataRegister(22));
    }

    [Fact]
    public void CcAndCdpApplyColorMatrixAndDepthCue()
    {
        var gte = CreateIdentityColorGte();
        gte.WriteDataRegister(6, 0x2C30_2010);
        gte.WriteDataRegister(9, 0x1000);
        gte.WriteDataRegister(10, 0x1000);
        gte.WriteDataRegister(11, 0x1000);

        Assert.True(gte.ExecuteCommand(ShiftFraction | LimitMode | 0x1C));
        Assert.Equal(0x2C30_2010u, gte.ReadDataRegister(22));

        gte.WriteControlRegister(21, 0x0400);
        gte.WriteControlRegister(22, 0x0500);
        gte.WriteControlRegister(23, 0x0600);
        gte.WriteDataRegister(8, 0x1000);

        Assert.True(gte.ExecuteCommand(ShiftFraction | LimitMode | 0x14));
        Assert.Equal(0x2C60_5040u, gte.ReadDataRegister(22));
    }

    [Fact]
    public void DcplAndDpctUseLightAndColorFifoInputs()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteDataRegister(6, 0x2C30_2010);
        gte.WriteDataRegister(8, 0);
        gte.WriteDataRegister(9, 0x1000);
        gte.WriteDataRegister(10, 0x1000);
        gte.WriteDataRegister(11, 0x1000);

        Assert.True(gte.ExecuteCommand(ShiftFraction | LimitMode | 0x29));
        Assert.Equal(0x2C30_2010u, gte.ReadDataRegister(22));

        gte.WriteDataRegister(20, 0x2C03_0201);
        gte.WriteDataRegister(21, 0x2C06_0504);
        gte.WriteDataRegister(22, 0x2C09_0807);

        Assert.True(gte.ExecuteCommand(ShiftFraction | LimitMode | 0x2A));
        Assert.Equal(0x2C03_0201u, gte.ReadDataRegister(20));
        Assert.Equal(0x2C06_0504u, gte.ReadDataRegister(21));
        Assert.Equal(0x2C09_0807u, gte.ReadDataRegister(22));
    }

    [Fact]
    public void GpfAndGplScaleAndAccumulateMacValues()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteDataRegister(6, 0x2C00_0000);
        gte.WriteDataRegister(8, 0x0800);
        gte.WriteDataRegister(9, 0x0100);
        gte.WriteDataRegister(10, 0x0200);
        gte.WriteDataRegister(11, 0x0300);

        Assert.True(gte.ExecuteCommand(ShiftFraction | 0x3D));
        Assert.Equal(0x2C18_1008u, gte.ReadDataRegister(22));

        gte.WriteDataRegister(8, 0x1000);
        gte.WriteDataRegister(9, 0x0020);
        gte.WriteDataRegister(10, 0x0020);
        gte.WriteDataRegister(11, 0x0020);
        gte.WriteDataRegister(25, 0x0100);
        gte.WriteDataRegister(26, 0x0100);
        gte.WriteDataRegister(27, 0x0100);

        Assert.True(gte.ExecuteCommand(ShiftFraction | 0x3E));
        Assert.Equal(0x2C12_1212u, gte.ReadDataRegister(22));
    }

    [Fact]
    public void MacOverflowFlagsUseHardwareAccumulatorWidths()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteControlRegister(0, 0x0000_7FFF);
        gte.WriteControlRegister(5, 0x7FFF_FFFF);
        gte.WriteDataRegister(0, 0x0000_7FFF);

        Assert.True(gte.ExecuteCommand(0x12));
        Assert.NotEqual(0u, gte.ReadControlRegister(31) & (1u << 30));
        Assert.NotEqual(0u, gte.ReadControlRegister(31) & (1u << 31));

        gte.WriteDataRegister(12, 0x8000_8000);
        gte.WriteDataRegister(13, 0x8000_7FFF);
        gte.WriteDataRegister(14, 0x7FFF_7FFF);

        Assert.True(gte.ExecuteCommand(0x06));
        Assert.NotEqual(0u, gte.ReadControlRegister(31) & (1u << 16));
    }

    [Fact]
    public void UnrDivisionSaturatesApproximationWithoutOverflowFlag()
    {
        var gte = CreateIdentityRotationGte();
        gte.WriteControlRegister(26, 0xFE3F);
        gte.WriteDataRegister(1, 0x7F20);

        Assert.True(gte.ExecuteCommand(ShiftFraction | 0x01));

        Assert.Equal(0u, gte.ReadControlRegister(31) & (1u << 17));
    }

    private static SadPSX.Core.Gte.Gte CreateIdentityColorGte()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteControlRegister(16, 0x0000_1000);
        gte.WriteControlRegister(18, 0x0000_1000);
        gte.WriteControlRegister(20, 0x0000_1000);
        return gte;
    }

    private static SadPSX.Core.Gte.Gte CreateIdentityRotationGte()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteControlRegister(0, 0x0000_1000);
        gte.WriteControlRegister(2, 0x0000_1000);
        gte.WriteControlRegister(4, 0x0000_1000);
        return gte;
    }
}
