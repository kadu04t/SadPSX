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

    [Fact]
    public void FractionalPitchInterpolatesBetweenAdpcmSamples()
    {
        var spu = CreateConfiguredVoice(
            packedSamples: 0x70,
            pitch: 0x0800);

        spu.Tick(SpuDevice.CpuCyclesPerSample * 2);
        Span<short> samples = stackalloc short[4];

        Assert.Equal(2, spu.DrainSamples(samples));
        Assert.Equal(0, samples[0]);
        Assert.True(samples[2] > 0);
    }

    [Fact]
    public void NoiseModeProducesAudioWithoutAdpcmAmplitude()
    {
        var spu = CreateConfiguredVoice(packedSamples: 0x00);
        spu.Write16(SpuDevice.NoiseModeLowRegister, 1);
        spu.Write16(SpuDevice.ControlRegister, 0xFF00);

        spu.Tick(SpuDevice.CpuCyclesPerSample * 32);
        var samples = new short[64];

        Assert.Equal(32, spu.DrainSamples(samples));
        Assert.Contains(samples, sample => sample != 0);
    }

    [Fact]
    public void PitchModulationChangesTheFollowingVoicePitch()
    {
        short[] normal = GeneratePitchModulationSamples(enabled: false);
        short[] modulated = GeneratePitchModulationSamples(enabled: true);

        Assert.False(normal.SequenceEqual(modulated));
    }

    [Fact]
    public void MixerWritesCdAndVoiceCaptureBuffers()
    {
        var spu = new SpuDevice();
        ConfigureVoice(
            spu,
            voiceIndex: 1,
            soundRamAddress: 0x100,
            packedSamples: 0x77,
            pitch: 0x1000,
            audible: true);
        spu.Write16(SpuDevice.MainVolumeLeftRegister, 0x3FFF);
        spu.Write16(SpuDevice.MainVolumeRightRegister, 0x3FFF);
        spu.Write16(SpuDevice.ControlRegister, 0xC000);
        spu.Write16(SpuDevice.KeyOnLowRegister, 1 << 1);
        spu.QueueCdAudioSector(
        [
            0x34, 0x12,
            0xCC, 0xED,
        ]);

        spu.Tick(SpuDevice.CpuCyclesPerSample);

        Assert.Equal(0x34, spu.SoundRam[0x000]);
        Assert.Equal(0x12, spu.SoundRam[0x001]);
        Assert.Equal(0xCC, spu.SoundRam[0x400]);
        Assert.Equal(0xED, spu.SoundRam[0x401]);
        Assert.NotEqual(0, spu.SoundRam[0x800] | spu.SoundRam[0x801]);
    }

    [Fact]
    public void CaptureStatusTracksTheActiveBufferHalf()
    {
        var spu = new SpuDevice();
        spu.Write16(SpuDevice.TransferControlRegister, 0x0004);

        spu.Tick(SpuDevice.CpuCyclesPerSample * 256);

        Assert.NotEqual(0, spu.Status & (1 << 11));

        spu.Tick(SpuDevice.CpuCyclesPerSample * 256);

        Assert.Equal(0, spu.Status & (1 << 11));
    }

    private static short[] GeneratePitchModulationSamples(bool enabled)
    {
        var spu = new SpuDevice();
        ConfigureVoice(
            spu,
            voiceIndex: 0,
            soundRamAddress: 0x100,
            packedSamples: 0x77,
            pitch: 0x1000,
            audible: false);
        ConfigureVoice(
            spu,
            voiceIndex: 1,
            soundRamAddress: 0x200,
            packedSamples: 0x70,
            pitch: 0x0800,
            audible: true);
        spu.Write16(SpuDevice.MainVolumeLeftRegister, 0x3FFF);
        spu.Write16(SpuDevice.MainVolumeRightRegister, 0x3FFF);
        spu.Write16(SpuDevice.ControlRegister, 0xC000);
        if (enabled)
            spu.Write16(SpuDevice.PitchModulationLowRegister, 1 << 1);
        spu.Write16(SpuDevice.KeyOnLowRegister, 0x0003);
        spu.Tick(SpuDevice.CpuCyclesPerSample * 16);

        var samples = new short[32];
        Assert.Equal(16, spu.DrainSamples(samples));
        return samples;
    }

    private static SpuDevice CreateConfiguredVoice(
        byte packedSamples = 0x77,
        ushort pitch = 0x1000)
    {
        var spu = new SpuDevice();
        ConfigureVoice(
            spu,
            voiceIndex: 0,
            soundRamAddress: 0x100,
            packedSamples,
            pitch,
            audible: true);
        spu.Write16(SpuDevice.MainVolumeLeftRegister, 0x3FFF);
        spu.Write16(SpuDevice.MainVolumeRightRegister, 0x3FFF);
        spu.Write16(SpuDevice.ControlRegister, 0xC000);
        spu.Write16(SpuDevice.KeyOnLowRegister, 1);
        return spu;
    }

    private static void ConfigureVoice(
        SpuDevice spu,
        int voiceIndex,
        int soundRamAddress,
        byte packedSamples,
        ushort pitch,
        bool audible)
    {
        spu.SoundRam[soundRamAddress] = 0x00;
        spu.SoundRam[soundRamAddress + 1] = 0x03;
        for (int index = 2; index < 16; index++)
            spu.SoundRam[soundRamAddress + index] = packedSamples;

        uint voiceAddress =
            SpuDevice.BaseAddress + (uint)voiceIndex * 0x10;
        ushort volume = audible ? (ushort)0x3FFF : (ushort)0;
        spu.Write16(voiceAddress, volume);
        spu.Write16(voiceAddress + 2, volume);
        spu.Write16(voiceAddress + 4, pitch);
        spu.Write16(
            voiceAddress + 6,
            (ushort)(soundRamAddress / 8));
        spu.Write16(voiceAddress + 8, 0x000F);
        spu.Write16(voiceAddress + 10, 0x0000);
        spu.Write16(
            voiceAddress + 14,
            (ushort)(soundRamAddress / 8));
    }
}
