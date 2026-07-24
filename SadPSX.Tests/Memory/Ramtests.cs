using Xunit;
using SadPSX.Core.Memory;

namespace SadPSX.Tests.Memory;

public sealed class RamTests
{
    [Fact]
    public void Write32ThenRead32RoundTrips()
    {
        var ram = new Ram();

        ram.Write32(0, 0x1234_5678);

        Assert.Equal(0x1234_5678u, ram.Read32(0));
    }

    [Fact]
    public void Write32StoresBytesInLittleEndianOrder()
    {
        var ram = new Ram();

        ram.Write32(0, 0x1234_5678);

        // Little-endian: byte menos significativo primeiro.
        Assert.Equal(0x78, ram.Read8(0));
        Assert.Equal(0x56, ram.Read8(1));
        Assert.Equal(0x34, ram.Read8(2));
        Assert.Equal(0x12, ram.Read8(3));
    }

    [Fact]
    public void Write16ThenRead16RoundTrips()
    {
        var ram = new Ram();

        ram.Write16(10, 0xABCD);

        Assert.Equal(0xABCD, ram.Read16(10));
    }

    [Fact]
    public void Write8DoesNotAffectNeighborBytes()
    {
        var ram = new Ram();

        ram.Write8(5, 0xFF);

        Assert.Equal(0xFF, ram.Read8(5));
        Assert.Equal(0, ram.Read8(4));
        Assert.Equal(0, ram.Read8(6));
    }
}