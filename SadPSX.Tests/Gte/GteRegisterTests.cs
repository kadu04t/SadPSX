using SadPSX.Core.Gte;
using Xunit;

namespace SadPSX.Tests.Gte;

public sealed class GteRegisterTests
{
    [Fact]
    public void SignedAndUnsignedRegistersUseTheirHardwareWidths()
    {
        var gte = new SadPSX.Core.Gte.Gte();

        gte.WriteDataRegister(1, 0x0000_FFFF);
        gte.WriteDataRegister(7, 0xFFFF_FFFF);

        Assert.Equal(0xFFFF_FFFFu, gte.ReadDataRegister(1));
        Assert.Equal(0x0000_FFFFu, gte.ReadDataRegister(7));
    }

    [Fact]
    public void WritingSxypShiftsScreenCoordinateFifo()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        gte.WriteDataRegister(12, 0x0001_0001);
        gte.WriteDataRegister(13, 0x0002_0002);
        gte.WriteDataRegister(14, 0x0003_0003);

        gte.WriteDataRegister(15, 0x0004_0004);

        Assert.Equal(0x0002_0002u, gte.ReadDataRegister(12));
        Assert.Equal(0x0003_0003u, gte.ReadDataRegister(13));
        Assert.Equal(0x0004_0004u, gte.ReadDataRegister(14));
        Assert.Equal(0x0004_0004u, gte.ReadDataRegister(15));
    }

    [Fact]
    public void IrgbExpandsAndOrgbPacksColorChannels()
    {
        var gte = new SadPSX.Core.Gte.Gte();
        const uint packed = 3u | (12u << 5) | (27u << 10);

        gte.WriteDataRegister(28, packed);

        Assert.Equal(3 << 7, (int)gte.ReadDataRegister(9));
        Assert.Equal(12 << 7, (int)gte.ReadDataRegister(10));
        Assert.Equal(27 << 7, (int)gte.ReadDataRegister(11));
        Assert.Equal(packed, gte.ReadDataRegister(29));
    }

    [Theory]
    [InlineData(0x0000_0001u, 31u)]
    [InlineData(0x4000_0000u, 1u)]
    [InlineData(0xFFFF_FFFFu, 32u)]
    [InlineData(0xFFFF_FFFEu, 31u)]
    public void LzcsUpdatesLeadingBitCount(uint value, uint expected)
    {
        var gte = new SadPSX.Core.Gte.Gte();

        gte.WriteDataRegister(30, value);

        Assert.Equal(expected, gte.ReadDataRegister(31));
    }
}
