using Xunit;
using SpuDevice = SadPSX.Core.Spu.Spu;

namespace SadPSX.Tests.Spu;

public sealed class SpuAudioTests
{
    [Fact]
    public void TickProducesStereoFramesAt44100Hertz()
    {
        var spu = new SpuDevice();

        spu.Tick(SpuDevice.CpuCyclesPerSample - 1);
        Assert.Equal(0, spu.QueuedSampleFrames);

        spu.Tick(1);

        Assert.Equal(1, spu.QueuedSampleFrames);
        Assert.Equal(1ul, spu.GeneratedSampleFrames);
    }

    [Fact]
    public void KeyOnDecodesAdpcmAndProducesAudibleSamples()
    {
        var spu = CreateConfiguredVoice();

        spu.Tick(SpuDevice.CpuCyclesPerSample * 4);
        Span<short> samples = stackalloc short[8];
        int frames = spu.DrainSamples(samples);

        Assert.Equal(4, frames);
        Assert.Contains(samples.ToArray(), sample => sample != 0);
        Assert.Equal(0, spu.QueuedSampleFrames);
    }

    [Fact]
    public void LoopEndSetsEndFlag()
    {
        var spu = CreateConfiguredVoice();

        spu.Tick(SpuDevice.CpuCyclesPerSample * 29);

        Assert.NotEqual(0u, spu.EndFlags & 1);
        Assert.Equal(1, spu.Read16(SpuDevice.EndFlagsLowRegister) & 1);
    }

    [Fact]
    public void CdAudioSamplesAreMixedIntoStereoOutput()
    {
        var spu = new SpuDevice();
        spu.Write16(SpuDevice.MainVolumeLeftRegister, 0x3FFF);
        spu.Write16(SpuDevice.MainVolumeRightRegister, 0x3FFF);
        spu.Write16(SpuDevice.CdVolumeLeftRegister, 0x7FFF);
        spu.Write16(SpuDevice.CdVolumeRightRegister, 0x7FFF);
        spu.Write16(SpuDevice.ControlRegister, 0x0001);
        spu.QueueCdAudioSector(
        [
            0x10, 0x27,
            0xF0, 0xD8,
        ]);

        spu.Tick(SpuDevice.CpuCyclesPerSample);
        Span<short> samples = stackalloc short[2];

        Assert.Equal(1, spu.DrainSamples(samples));
        Assert.True(samples[0] > 0);
        Assert.True(samples[1] < 0);
    }

    private static SpuDevice CreateConfiguredVoice()
    {
        var spu = new SpuDevice();
        const int soundRamAddress = 0x100;
        spu.SoundRam[soundRamAddress] = 0x00;
        spu.SoundRam[soundRamAddress + 1] = 0x03;
        for (int index = 2; index < 16; index++)
            spu.SoundRam[soundRamAddress + index] = 0x77;

        spu.Write16(SpuDevice.MainVolumeLeftRegister, 0x3FFF);
        spu.Write16(SpuDevice.MainVolumeRightRegister, 0x3FFF);
        spu.Write16(SpuDevice.BaseAddress, 0x3FFF);
        spu.Write16(SpuDevice.BaseAddress + 2, 0x3FFF);
        spu.Write16(SpuDevice.BaseAddress + 4, 0x1000);
        spu.Write16(SpuDevice.BaseAddress + 6, soundRamAddress / 8);
        spu.Write16(SpuDevice.BaseAddress + 8, 0x000F);
        spu.Write16(SpuDevice.BaseAddress + 10, 0x0000);
        spu.Write16(SpuDevice.BaseAddress + 14, soundRamAddress / 8);
        spu.Write16(SpuDevice.ControlRegister, 0xC000);
        spu.Write16(SpuDevice.KeyOnLowRegister, 1);
        return spu;
    }
}
