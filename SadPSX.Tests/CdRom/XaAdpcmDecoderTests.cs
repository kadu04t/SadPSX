using SadPSX.Core.CdRom.Audio;
using Xunit;

namespace SadPSX.Tests.CdRom;

public sealed class XaAdpcmDecoderTests
{
    [Fact]
    public void StereoSectorDecodesAndResamplesToOneCdSector()
    {
        var decoder = new XaAdpcmDecoder();
        byte[] sector = CreateSector(codingInfo: 0x01);

        short[] samples = decoder.DecodeSector(sector);

        Assert.Equal(2_352 * 2, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void MonoHalfRateSectorDuplicatesChannelsAndUpsamples()
    {
        var decoder = new XaAdpcmDecoder();
        byte[] sector = CreateSector(codingInfo: 0x04);
        for (int group = 0; group < 18; group++)
            sector[24 + group * 128 + 16] = 0x07;

        short[] samples = decoder.DecodeSector(sector);

        Assert.Equal(9_408 * 2, samples.Length);
        Assert.Contains(samples, sample => sample != 0);
        for (int frame = 0; frame < samples.Length / 2; frame++)
            Assert.Equal(samples[frame * 2], samples[frame * 2 + 1]);
    }

    [Fact]
    public void EightBitStereoUsesAllFourSoundUnits()
    {
        var decoder = new XaAdpcmDecoder();
        byte[] sector = CreateSector(codingInfo: 0x11);
        for (int group = 0; group < 18; group++)
        {
            int dataOffset = 24 + group * 128 + 16;
            sector[dataOffset] = 0x20;
            sector[dataOffset + 1] = 0xE0;
            sector[dataOffset + 2] = 0x10;
            sector[dataOffset + 3] = 0xF0;
        }

        short[] samples = decoder.DecodeSector(sector);

        Assert.Equal(1_176 * 2, samples.Length);
        Assert.Contains(samples.Where((_, index) => (index & 1) == 0), sample => sample > 0);
        Assert.Contains(samples.Where((_, index) => (index & 1) != 0), sample => sample < 0);
    }

    private static byte[] CreateSector(byte codingInfo)
    {
        var sector = new byte[2_352];
        sector[15] = 2;
        sector[18] = 0x44;
        sector[19] = codingInfo;
        return sector;
    }
}
