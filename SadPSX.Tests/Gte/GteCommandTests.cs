using Xunit;

namespace SadPSX.Tests.Gte;

public sealed class GteCommandTests
{
    private const uint ShiftFraction = 1u << 19;

    [Fact]
    public void RtpsTransformsAndProjectsOneVertex()
    {
        var gte = CreateIdentityGte();
        gte.WriteControlRegister(26, 1000);
        gte.WriteDataRegister(0, 100u | (50u << 16));
        gte.WriteDataRegister(1, 1000);

        Assert.True(gte.ExecuteCommand(ShiftFraction | 0x01));

        Assert.Equal(100u, gte.ReadDataRegister(9));
        Assert.Equal(50u, gte.ReadDataRegister(10));
        Assert.Equal(1000u, gte.ReadDataRegister(11));
        Assert.Equal(1000u, gte.ReadDataRegister(19));
        Assert.Equal(100u | (50u << 16), gte.ReadDataRegister(14));
    }

    [Fact]
    public void RtptTransformsThreeVerticesAndAdvancesFifos()
    {
        var gte = CreateIdentityGte();
        gte.WriteControlRegister(26, 100);
        WriteVector(gte, 0, 10, 20, 100);
        WriteVector(gte, 1, 30, 40, 100);
        WriteVector(gte, 2, 50, 60, 100);

        Assert.True(gte.ExecuteCommand(ShiftFraction | 0x30));

        Assert.Equal(10u | (20u << 16), gte.ReadDataRegister(12));
        Assert.Equal(30u | (40u << 16), gte.ReadDataRegister(13));
        Assert.Equal(50u | (60u << 16), gte.ReadDataRegister(14));
        Assert.Equal(100u, gte.ReadDataRegister(17));
        Assert.Equal(100u, gte.ReadDataRegister(18));
        Assert.Equal(100u, gte.ReadDataRegister(19));
    }

    [Fact]
    public void NclipCalculatesSignedTriangleArea()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteDataRegister(12, 0);
        gte.WriteDataRegister(13, 10);
        gte.WriteDataRegister(14, 10u << 16);

        Assert.True(gte.ExecuteCommand(0x06));

        Assert.Equal(100u, gte.ReadDataRegister(24));
    }

    [Fact]
    public void AverageDepthCommandsUpdateOtz()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteControlRegister(29, 0x1000);
        gte.WriteControlRegister(30, 0x1000);
        gte.WriteDataRegister(16, 100);
        gte.WriteDataRegister(17, 200);
        gte.WriteDataRegister(18, 300);
        gte.WriteDataRegister(19, 400);

        Assert.True(gte.ExecuteCommand(0x2D));
        Assert.Equal(900u, gte.ReadDataRegister(7));

        Assert.True(gte.ExecuteCommand(0x2E));
        Assert.Equal(1000u, gte.ReadDataRegister(7));
    }

    [Fact]
    public void MvmvaUsesSelectedMatrixAndVector()
    {
        var gte = CreateIdentityGte();
        WriteVector(gte, 0, 123, -456, 789);
        uint noTranslation = 3u << 13;

        Assert.True(gte.ExecuteCommand(ShiftFraction | noTranslation | 0x12));

        Assert.Equal(123u, gte.ReadDataRegister(9));
        Assert.Equal(unchecked((uint)-456), gte.ReadDataRegister(10));
        Assert.Equal(789u, gte.ReadDataRegister(11));
    }

    private static SadPSX.Core.Gte.Gte CreateIdentityGte()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteControlRegister(0, 0x0000_1000);
        gte.WriteControlRegister(2, 0x0000_1000);
        gte.WriteControlRegister(4, 0x0000_1000);
        return gte;
    }

    private static void WriteVector(
        SadPSX.Core.Gte.Gte gte,
        int index,
        short x,
        short y,
        short z)
    {
        gte.WriteDataRegister(
            index * 2,
            (uint)(ushort)x | ((uint)(ushort)y << 16));
        gte.WriteDataRegister(index * 2 + 1, (uint)(ushort)z);
    }
}
