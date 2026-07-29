using SadPSX.Core.Spu;
using Xunit;

namespace SadPSX.Tests.Spu;

public sealed class SpuVolumeTests
{
    [Fact]
    public void FixedVolumeUsesSignedFifteenBitLevel()
    {
        var volume = new SpuVolume();

        volume.Configure(0x3FFF);
        Assert.Equal(0x7FFE, volume.Tick());

        volume.Configure(0x4000);
        Assert.Equal(short.MinValue, volume.Tick());
    }

    [Fact]
    public void LinearSweepAdvancesFromTheCurrentLevel()
    {
        var volume = new SpuVolume();
        volume.Configure(0x1000);
        int initial = volume.Tick();

        volume.Configure(0x8000);
        for (int sample = 0; sample < 64; sample++)
            volume.Tick();

        Assert.True(volume.Level > initial);
    }

    [Fact]
    public void NegativeDecreaseSweepMovesTowardSilence()
    {
        var volume = new SpuVolume();
        volume.Configure(0x5000);
        int initialMagnitude = Math.Abs(volume.Tick());

        volume.Configure(0xB000);
        for (int sample = 0; sample < 64; sample++)
            volume.Tick();

        Assert.True(Math.Abs(volume.Level) < initialMagnitude);
        Assert.True(volume.Level <= 0);
    }
}
