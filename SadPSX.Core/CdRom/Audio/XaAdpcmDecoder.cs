namespace SadPSX.Core.CdRom.Audio;

public sealed class XaAdpcmDecoder
{
    private const int SectorAudioOffset = 24;
    private const int SoundGroupCount = 18;
    private const int SoundGroupSize = 128;

    private static readonly int[] PositiveFilter = [0, 60, 115, 98];
    private static readonly int[] NegativeFilter = [0, 0, -52, -55];

    private readonly ChannelState _left = new();
    private readonly ChannelState _right = new();
    private readonly XaResampler _leftResampler = new();
    private readonly XaResampler _rightResampler = new();

    public void Reset()
    {
        _left.Reset();
        _right.Reset();
        _leftResampler.Reset();
        _rightResampler.Reset();
    }

    public short[] DecodeSector(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < SectorAudioOffset + SoundGroupCount * SoundGroupSize)
        {
            throw new ArgumentException(
                "The XA sector does not contain a complete audio payload.",
                nameof(sector));
        }

        byte codingInfo = sector[19];
        bool stereo = (codingInfo & 1) != 0;
        bool halfRate = (codingInfo & 4) != 0;
        bool eightBit = (codingInfo & 0x10) != 0;
        var leftSamples = new List<short>(4_704);
        var rightSamples = new List<short>(4_704);

        for (int groupIndex = 0; groupIndex < SoundGroupCount; groupIndex++)
        {
            ReadOnlySpan<byte> group = sector.Slice(
                SectorAudioOffset + groupIndex * SoundGroupSize,
                SoundGroupSize);
            if (eightBit)
                DecodeEightBitGroup(group, stereo, leftSamples, rightSamples);
            else
                DecodeFourBitGroup(group, stereo, leftSamples, rightSamples);
        }

        if (!stereo)
            rightSamples.AddRange(leftSamples);

        short[] resampledLeft = _leftResampler.Resample(leftSamples, halfRate);
        short[] resampledRight = _rightResampler.Resample(rightSamples, halfRate);
        int frameCount = Math.Min(resampledLeft.Length, resampledRight.Length);
        short[] interleaved = new short[frameCount * 2];
        for (int frame = 0; frame < frameCount; frame++)
        {
            interleaved[frame * 2] = resampledLeft[frame];
            interleaved[frame * 2 + 1] = resampledRight[frame];
        }

        return interleaved;
    }

    private void DecodeFourBitGroup(
        ReadOnlySpan<byte> group,
        bool stereo,
        List<short> left,
        List<short> right)
    {
        for (int block = 0; block < 4; block++)
        {
            if (stereo)
            {
                DecodeFourBitUnit(group, block, 0, _left, left);
                DecodeFourBitUnit(group, block, 1, _right, right);
            }
            else
            {
                DecodeFourBitUnit(group, block, 0, _left, left);
                DecodeFourBitUnit(group, block, 1, _left, left);
            }
        }
    }

    private void DecodeEightBitGroup(
        ReadOnlySpan<byte> group,
        bool stereo,
        List<short> left,
        List<short> right)
    {
        for (int unit = 0; unit < 4; unit++)
        {
            ChannelState channel = !stereo || (unit & 1) == 0
                ? _left
                : _right;
            List<short> destination = !stereo || (unit & 1) == 0
                ? left
                : right;
            DecodeEightBitUnit(group, unit, channel, destination);
        }
    }

    private static void DecodeFourBitUnit(
        ReadOnlySpan<byte> group,
        int block,
        int nibble,
        ChannelState channel,
        List<short> destination)
    {
        byte header = group[4 + block * 2 + nibble];
        int shift = NormalizeShift(header & 0x0F);
        int filter = Math.Min((header >> 4) & 3, 3);
        for (int sampleIndex = 0; sampleIndex < 28; sampleIndex++)
        {
            int packed = group[16 + block + sampleIndex * 4];
            int encoded = (packed >> (nibble * 4)) & 0x0F;
            if ((encoded & 8) != 0)
                encoded -= 16;
            int sample = (encoded * 4096 >> shift) +
                ((channel.Previous * PositiveFilter[filter] +
                  channel.Previous2 * NegativeFilter[filter] +
                  32) >> 6);
            short decoded = (short)Math.Clamp(
                sample,
                short.MinValue,
                short.MaxValue);
            destination.Add(decoded);
            channel.Previous2 = channel.Previous;
            channel.Previous = decoded;
        }
    }

    private static void DecodeEightBitUnit(
        ReadOnlySpan<byte> group,
        int unit,
        ChannelState channel,
        List<short> destination)
    {
        byte header = group[4 + unit];
        int shift = NormalizeShift(header & 0x0F);
        int filter = Math.Min((header >> 4) & 3, 3);
        for (int sampleIndex = 0; sampleIndex < 28; sampleIndex++)
        {
            int encoded = unchecked((sbyte)group[16 + unit + sampleIndex * 4]);
            int sample = (encoded * 256 >> shift) +
                ((channel.Previous * PositiveFilter[filter] +
                  channel.Previous2 * NegativeFilter[filter] +
                  32) >> 6);
            short decoded = (short)Math.Clamp(
                sample,
                short.MinValue,
                short.MaxValue);
            destination.Add(decoded);
            channel.Previous2 = channel.Previous;
            channel.Previous = decoded;
        }
    }

    private static int NormalizeShift(int shift) => shift > 12 ? 9 : shift;

    private sealed class ChannelState
    {
        public int Previous;
        public int Previous2;

        public void Reset()
        {
            Previous = 0;
            Previous2 = 0;
        }
    }

    private sealed class XaResampler
    {
        private static readonly short[,] ZigZagTable =
        {
            { 0, 0, 0, 0, -0x0001, 0x0002, -0x0005 },
            { 0, 0, 0, -0x0001, 0x0003, -0x0008, 0x0011 },
            { 0, 0, -0x0001, 0x0003, -0x0008, 0x0010, -0x0023 },
            { 0, -0x0002, 0x0003, -0x0008, 0x0011, -0x0023, 0x0046 },
            { 0, 0, -0x0002, 0x0006, -0x0010, 0x002B, -0x0017 },
            { -0x0002, 0x0003, -0x0005, 0x0005, 0x000A, 0x001A, -0x0044 },
            { 0x000A, -0x0013, 0x001F, -0x001B, 0x006B, -0x00EB, 0x015B },
            { -0x0022, 0x003C, -0x004A, 0x00A6, -0x016D, 0x027B, -0x0347 },
            { 0x0041, -0x004B, 0x00B3, -0x01A8, 0x0350, -0x0548, 0x080E },
            { -0x0054, 0x00A2, -0x0192, 0x0372, -0x0623, 0x0AFA, -0x1249 },
            { 0x0034, -0x00E3, 0x02B1, -0x05BF, 0x0BCD, -0x16FA, 0x3C07 },
            { 0x0009, 0x0132, -0x039E, 0x09B8, -0x1780, 0x53E0, 0x53E0 },
            { -0x010A, -0x0043, 0x04F8, -0x11B4, 0x6794, 0x3C07, -0x16FA },
            { 0x0400, -0x0267, -0x05A6, 0x74BB, 0x234C, -0x1249, 0x0AFA },
            { -0x0A78, 0x0C9D, 0x7939, 0x0C9D, -0x0A78, 0x080E, -0x0548 },
            { 0x234C, 0x74BB, -0x05A6, -0x0267, 0x0400, -0x0347, 0x027B },
            { 0x6794, -0x11B4, 0x04F8, -0x0043, -0x010A, 0x015B, -0x00EB },
            { -0x1780, 0x09B8, -0x039E, 0x0132, 0x0009, -0x0044, 0x001A },
            { 0x0BCD, -0x05BF, 0x02B1, -0x00E3, 0x0034, -0x0017, 0x002B },
            { -0x0623, 0x0372, -0x0192, 0x00A2, -0x0054, 0x0046, -0x0023 },
            { 0x0350, -0x01A8, 0x00B3, -0x004B, 0x0041, -0x0023, 0x0010 },
            { -0x016D, 0x00A6, -0x004A, 0x003C, -0x0022, 0x0011, -0x0008 },
            { 0x006B, -0x001B, 0x001F, -0x0013, 0x000A, -0x0005, 0x0002 },
            { 0x000A, 0x0005, -0x0005, 0x0003, -0x0001, 0, 0 },
            { -0x0010, 0x0006, -0x0002, 0, 0, 0, 0 },
            { 0x0011, -0x0008, 0x0003, -0x0002, 0x0001, 0, 0 },
            { -0x0008, 0x0003, -0x0001, 0, 0, 0, 0 },
            { 0x0003, -0x0001, 0, 0, 0, 0, 0 },
            { -0x0001, 0, 0, 0, 0, 0, 0 },
        };

        private readonly short[] _ring = new short[32];
        private int _position;
        private int _sixStep = 6;

        public void Reset()
        {
            Array.Clear(_ring);
            _position = 0;
            _sixStep = 6;
        }

        public short[] Resample(IReadOnlyList<short> samples, bool halfRate)
        {
            int inputMultiplier = halfRate ? 2 : 1;
            var output = new List<short>(
                samples.Count * inputMultiplier * 7 / 6);
            foreach (short sample in samples)
            {
                Push(sample, output);
                if (halfRate)
                    Push(sample, output);
            }

            return output.ToArray();
        }

        private void Push(short sample, List<short> output)
        {
            _ring[_position & 0x1F] = sample;
            _position++;
            _sixStep--;
            if (_sixStep != 0)
                return;

            _sixStep = 6;
            for (int phase = 0; phase < 7; phase++)
                output.Add(Interpolate(phase));
        }

        private short Interpolate(int phase)
        {
            long sum = 0;
            for (int index = 1; index <= 29; index++)
            {
                sum += (long)_ring[(_position - index) & 0x1F] *
                       ZigZagTable[index - 1, phase];
            }

            return (short)Math.Clamp(
                sum >> 15,
                short.MinValue,
                short.MaxValue);
        }
    }
}
