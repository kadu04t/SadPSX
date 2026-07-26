namespace SadPSX.Core.Spu;

internal sealed class SpuVoice
{
    public readonly short[] DecodedSamples = new short[28];

    public bool Active;
    public bool StopAfterBlock;
    public uint CurrentAddress;
    public uint RepeatAddress;
    public int DecodedIndex;
    public uint PitchCounter;
    public int PreviousSample;
    public int PreviousSample2;
    public int EnvelopeLevel;
    public int EnvelopeCounter;
    public EnvelopePhase EnvelopePhase;

    public void Reset()
    {
        Array.Clear(DecodedSamples);
        Active = false;
        StopAfterBlock = false;
        CurrentAddress = 0;
        RepeatAddress = 0;
        DecodedIndex = DecodedSamples.Length;
        PitchCounter = 0;
        PreviousSample = 0;
        PreviousSample2 = 0;
        EnvelopeLevel = 0;
        EnvelopeCounter = 0;
        EnvelopePhase = EnvelopePhase.Off;
    }
}

internal enum EnvelopePhase
{
    Off,
    Attack,
    Decay,
    Sustain,
    Release,
}
