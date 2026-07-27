using System.Buffers.Binary;
using SadPSX.Core.Bus;
using SadPSX.Core.Interrupts;

namespace SadPSX.Core.Spu;

public sealed class Spu : IClockedDevice, IMmioDevice
{
    public const uint BaseAddress = 0x1F80_1C00;
    public const uint EndAddress = 0x1F80_1DFF;
    public const uint TransferAddressRegister = 0x1F80_1DA6;
    public const uint TransferFifoRegister = 0x1F80_1DA8;
    public const uint ControlRegister = 0x1F80_1DAA;
    public const uint TransferControlRegister = 0x1F80_1DAC;
    public const uint StatusRegister = 0x1F80_1DAE;
    public const uint MainVolumeLeftRegister = 0x1F80_1D80;
    public const uint MainVolumeRightRegister = 0x1F80_1D82;
    public const uint CdVolumeLeftRegister = 0x1F80_1DB0;
    public const uint CdVolumeRightRegister = 0x1F80_1DB2;
    public const uint KeyOnLowRegister = 0x1F80_1D88;
    public const uint KeyOnHighRegister = 0x1F80_1D8A;
    public const uint KeyOffLowRegister = 0x1F80_1D8C;
    public const uint KeyOffHighRegister = 0x1F80_1D8E;
    public const uint PitchModulationLowRegister = 0x1F80_1D90;
    public const uint PitchModulationHighRegister = 0x1F80_1D92;
    public const uint NoiseModeLowRegister = 0x1F80_1D94;
    public const uint NoiseModeHighRegister = 0x1F80_1D96;
    public const uint EndFlagsLowRegister = 0x1F80_1D9C;
    public const uint EndFlagsHighRegister = 0x1F80_1D9E;
    public const uint InterruptAddressRegister = 0x1F80_1DA4;
    public const uint CpuCyclesPerSample = 768;
    public const int SampleRate = 44_100;

    private const int RegisterCount = 0x100;
    private const int VoiceCount = 24;
    private const int SoundRamSize = 512 * 1024;
    private const uint ControlApplyDelayCycles = 2;
    private const int MaximumQueuedSampleFrames = 8_192;
    private const int CaptureBufferSize = 0x400;
    private const int CdLeftCaptureBase = 0x000;
    private const int CdRightCaptureBase = 0x400;
    private const int Voice1CaptureBase = 0x800;
    private const int Voice3CaptureBase = 0xC00;

    private static readonly int[] PositiveFilter = [0, 60, 115, 98, 122];
    private static readonly int[] NegativeFilter = [0, 0, -52, -55, -60];

    private readonly ushort[] _registers = new ushort[RegisterCount];
    private readonly byte[] _soundRam = new byte[SoundRamSize];
    private readonly Queue<ushort> _transferFifo = new(32);
    private readonly Queue<StereoSample> _sampleQueue = new();
    private readonly Queue<StereoSample> _cdAudioQueue = new();
    private readonly SpuVoice[] _voices =
        Enumerable.Range(0, VoiceCount).Select(_ => new SpuVoice()).ToArray();
    private readonly InterruptController? _interruptController;

    private uint _currentTransferAddress;
    private uint _controlApplyCycles;
    private ushort _pendingStatusMode;
    private ulong _sampleCycleAccumulator;
    private uint _endFlags;
    private ushort _noiseLevel;
    private int _noiseTimer;
    private int _captureBufferPosition;

    public Spu(InterruptController? interruptController = null)
    {
        _interruptController = interruptController;
        Reset();
    }

    public ushort Control => ReadRawRegister(ControlRegister);

    public ushort Status => ReadRawRegister(StatusRegister);

    public byte[] SoundRam => _soundRam;
    public int QueuedSampleFrames => _sampleQueue.Count;
    public ulong GeneratedSampleFrames { get; private set; }
    public uint EndFlags => _endFlags;

    public void Reset()
    {
        Array.Clear(_registers);
        Array.Clear(_soundRam);
        _transferFifo.Clear();
        _sampleQueue.Clear();
        _cdAudioQueue.Clear();
        foreach (SpuVoice voice in _voices)
            voice.Reset();
        _currentTransferAddress = 0;
        _controlApplyCycles = 0;
        _pendingStatusMode = 0;
        _sampleCycleAccumulator = 0;
        _endFlags = 0;
        _noiseLevel = 1;
        _noiseTimer = 0;
        _captureBufferPosition = 0;
        GeneratedSampleFrames = 0;
    }

    public void Tick(uint cycles)
    {
        if (cycles == 0)
            return;

        if (_controlApplyCycles > 0)
        {
            if (cycles < _controlApplyCycles)
                _controlApplyCycles -= cycles;
            else
            {
                _controlApplyCycles = 0;
                ApplyControlToStatus();
            }
        }

        _sampleCycleAccumulator += cycles;
        while (_sampleCycleAccumulator >= CpuCyclesPerSample)
        {
            _sampleCycleAccumulator -= CpuCyclesPerSample;
            GenerateSample();
        }
    }

    public bool Handles(uint address) =>
        address >= BaseAddress && address <= EndAddress;

    public byte Read8(uint address)
    {
        ushort value = Read16(address & ~1u);
        int shift = (int)((address & 1) * 8);
        return (byte)(value >> shift);
    }

    public ushort Read16(uint address)
    {
        ValidateHalfwordAddress(address);
        return ReadRawRegister(address);
    }

    public uint Read32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada na SPU: 0x{address:X8}.");

        return (uint)(Read16(address) | (Read16(address + 2) << 16));
    }

    public void Write8(uint address, byte value)
    {
        uint registerAddress = address & ~1u;
        ushort currentValue = ReadRawRegister(registerAddress);
        int shift = (int)((address & 1) * 8);
        ushort mask = (ushort)(0xFF << shift);
        ushort mergedValue = (ushort)(
            (currentValue & ~mask) |
            (value << shift));
        Write16(registerAddress, mergedValue);
    }

    public void Write16(uint address, ushort value)
    {
        ValidateHalfwordAddress(address);

        switch (address)
        {
            case StatusRegister:
            case EndFlagsLowRegister:
            case EndFlagsHighRegister:
                break;

            case TransferAddressRegister:
                WriteRawRegister(address, value);
                _currentTransferAddress =
                    ((uint)value * 8) % SoundRamSize;
                break;

            case TransferFifoRegister:
                WriteRawRegister(address, value);
                if (_transferFifo.Count == 32)
                    _transferFifo.Dequeue();
                _transferFifo.Enqueue(value);
                break;

            case ControlRegister:
                WriteRawRegister(address, value);
                if ((value & (1 << 6)) == 0)
                    ClearIrqFlag();
                _pendingStatusMode = (ushort)(value & 0x003F);
                _controlApplyCycles = ControlApplyDelayCycles;
                break;

            case KeyOnLowRegister:
                WriteRawRegister(address, value);
                KeyOn(value, 0);
                break;

            case KeyOnHighRegister:
                WriteRawRegister(address, value);
                KeyOn(value, 16);
                break;

            case KeyOffLowRegister:
                WriteRawRegister(address, value);
                KeyOff(value, 0);
                break;

            case KeyOffHighRegister:
                WriteRawRegister(address, value);
                KeyOff(value, 16);
                break;

            default:
                WriteRawRegister(address, value);
                UpdateVoiceRegister(address, value);
                break;
        }
    }

    public void Write32(uint address, uint value)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Escrita de 32 bits desalinhada na SPU: 0x{address:X8}.");

        Write16(address, (ushort)value);
        Write16(address + 2, (ushort)(value >> 16));
    }

    public string GetRegisterName(uint address)
    {
        uint registerAddress = address & ~1u;
        return registerAddress switch
        {
            PitchModulationLowRegister => "SPU_PMON_LOW",
            PitchModulationHighRegister => "SPU_PMON_HIGH",
            NoiseModeLowRegister => "SPU_NON_LOW",
            NoiseModeHighRegister => "SPU_NON_HIGH",
            InterruptAddressRegister => "SPU_IRQ_ADDR",
            TransferAddressRegister => "SPU_TRANSFER_ADDR",
            TransferFifoRegister => "SPU_TRANSFER_FIFO",
            ControlRegister => "SPUCNT",
            TransferControlRegister => "SPU_TRANSFER_CTRL",
            StatusRegister => "SPUSTAT",
            _ when registerAddress < 0x1F80_1D80 => "SPU_VOICE",
            _ => "SPU_REG",
        };
    }

    public void WriteDmaWord(uint value)
    {
        WriteSoundRamHalfword((ushort)value);
        WriteSoundRamHalfword((ushort)(value >> 16));
    }

    public uint ReadDmaWord()
    {
        ushort low = ReadSoundRamHalfword();
        ushort high = ReadSoundRamHalfword();
        return low | ((uint)high << 16);
    }

    public int DrainSamples(Span<short> destination)
    {
        int frameCapacity = destination.Length / 2;
        int frames = Math.Min(frameCapacity, _sampleQueue.Count);
        for (int frame = 0; frame < frames; frame++)
        {
            StereoSample sample = _sampleQueue.Dequeue();
            destination[frame * 2] = sample.Left;
            destination[frame * 2 + 1] = sample.Right;
        }

        return frames;
    }

    public void QueueCdAudioSector(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < 4 || (sector.Length & 3) != 0)
        {
            throw new ArgumentException(
                "O setor CD-DA deve conter amostras estéreo de 16 bits.",
                nameof(sector));
        }

        for (int offset = 0; offset < sector.Length; offset += 4)
        {
            if (_cdAudioQueue.Count == MaximumQueuedSampleFrames)
                _cdAudioQueue.Dequeue();
            _cdAudioQueue.Enqueue(new StereoSample(
                BinaryPrimitives.ReadInt16LittleEndian(
                    sector.Slice(offset, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(
                    sector.Slice(offset + 2, 2))));
        }
    }

    private void ApplyControlToStatus()
    {
        ushort status = ReadRawRegister(StatusRegister);
        status = (ushort)((status & ~0x03BF) | _pendingStatusMode);

        int transferMode = (_pendingStatusMode >> 4) & 3;
        if ((_pendingStatusMode & 0x20) != 0)
            status |= 1 << 7;

        if (transferMode == 2)
            status |= 1 << 8;
        else if (transferMode == 3)
            status |= 1 << 9;

        WriteRawRegister(StatusRegister, status);

        if (transferMode == 1)
            FlushTransferFifo();
    }

    private void FlushTransferFifo()
    {
        while (_transferFifo.TryDequeue(out ushort value))
            WriteSoundRamHalfword(value);
    }

    private ushort ReadRawRegister(uint address)
    {
        if (address == EndFlagsLowRegister)
            return (ushort)_endFlags;
        if (address == EndFlagsHighRegister)
            return (ushort)(_endFlags >> 16);

        int index = (int)((address - BaseAddress) / 2);
        return _registers[index];
    }

    private void WriteRawRegister(uint address, ushort value)
    {
        int index = (int)((address - BaseAddress) / 2);
        _registers[index] = value;
    }

    private static void ValidateHalfwordAddress(uint address)
    {
        if ((address & 1) != 0 ||
            address < BaseAddress ||
            address > EndAddress - 1)
        {
            throw new InvalidOperationException(
                $"Endereço inválido de registrador SPU: 0x{address:X8}.");
        }
    }

    private void KeyOn(ushort flags, int voiceBase)
    {
        for (int bit = 0; bit < 16; bit++)
        {
            int voiceIndex = voiceBase + bit;
            if (voiceIndex >= VoiceCount || (flags & (1 << bit)) == 0)
                continue;

            SpuVoice voice = _voices[voiceIndex];
            uint voiceAddress = BaseAddress + (uint)voiceIndex * 0x10;
            voice.Reset();
            voice.Active = true;
            voice.CurrentAddress =
                (uint)ReadRawRegister(voiceAddress + 6) * 8 % SoundRamSize;
            voice.RepeatAddress =
                (uint)ReadRawRegister(voiceAddress + 14) * 8 % SoundRamSize;
            voice.EnvelopePhase = EnvelopePhase.Attack;
            _endFlags &= ~(1u << voiceIndex);
            WriteRawRegister(voiceAddress + 12, 0);
        }
    }

    private void KeyOff(ushort flags, int voiceBase)
    {
        for (int bit = 0; bit < 16; bit++)
        {
            int voiceIndex = voiceBase + bit;
            if (voiceIndex < VoiceCount && (flags & (1 << bit)) != 0)
                _voices[voiceIndex].EnvelopePhase = EnvelopePhase.Release;
        }
    }

    private void UpdateVoiceRegister(uint address, ushort value)
    {
        if (address >= BaseAddress + VoiceCount * 0x10)
            return;

        int voiceIndex = (int)((address - BaseAddress) / 0x10);
        uint offset = (address - BaseAddress) % 0x10;
        if (offset == 12)
            _voices[voiceIndex].EnvelopeLevel = value;
        else if (offset == 14)
            _voices[voiceIndex].RepeatAddress =
                (uint)value * 8 % SoundRamSize;
    }

    private void GenerateSample()
    {
        AdvanceNoise();

        long left = 0;
        long right = 0;
        int previousVoiceOutput = 0;
        int voice1Output = 0;
        int voice3Output = 0;

        for (int voiceIndex = 0; voiceIndex < VoiceCount; voiceIndex++)
        {
            int sample = GenerateVoiceSample(
                voiceIndex,
                previousVoiceOutput);
            previousVoiceOutput = sample;
            if (voiceIndex == 1)
                voice1Output = sample;
            else if (voiceIndex == 3)
                voice3Output = sample;

            uint voiceAddress = BaseAddress + (uint)voiceIndex * 0x10;
            int volumeLeft = DecodeVolume(ReadRawRegister(voiceAddress));
            int volumeRight = DecodeVolume(ReadRawRegister(voiceAddress + 2));
            left += (long)sample * volumeLeft / 0x8000;
            right += (long)sample * volumeRight / 0x8000;
        }

        StereoSample cdAudio = _cdAudioQueue.TryDequeue(
            out StereoSample queuedCdAudio)
            ? queuedCdAudio
            : default;
        CaptureSamples(cdAudio, voice1Output, voice3Output);

        bool outputEnabled = (Control & 0xC000) == 0xC000;
        if (!outputEnabled)
        {
            left = 0;
            right = 0;
        }

        if ((Control & 1) != 0)
        {
            left += (long)cdAudio.Left *
                DecodeSignedVolume(ReadRawRegister(CdVolumeLeftRegister)) /
                0x8000;
            right += (long)cdAudio.Right *
                DecodeSignedVolume(ReadRawRegister(CdVolumeRightRegister)) /
                0x8000;
        }

        left = left * DecodeVolume(ReadRawRegister(MainVolumeLeftRegister)) / 0x8000;
        right = right * DecodeVolume(ReadRawRegister(MainVolumeRightRegister)) / 0x8000;

        if (_sampleQueue.Count == MaximumQueuedSampleFrames)
            _sampleQueue.Dequeue();
        _sampleQueue.Enqueue(new StereoSample(
            (short)Math.Clamp(left, short.MinValue, short.MaxValue),
            (short)Math.Clamp(right, short.MinValue, short.MaxValue)));
        GeneratedSampleFrames++;
    }

    private int GenerateVoiceSample(
        int voiceIndex,
        int previousVoiceOutput)
    {
        SpuVoice voice = _voices[voiceIndex];
        if (!voice.Active)
            return 0;

        uint voiceAddress = BaseAddress + (uint)voiceIndex * 0x10;
        if (voice.DecodedIndex >= voice.DecodedSamples.Length)
        {
            if (voice.StopAfterBlock)
            {
                voice.Active = false;
                voice.EnvelopeLevel = 0;
                voice.EnvelopePhase = EnvelopePhase.Off;
                WriteRawRegister(voiceAddress + 12, 0);
                return 0;
            }

            DecodeAdpcmBlock(voiceIndex);
        }

        AdvanceEnvelope(voiceIndex);
        int sample = IsVoiceFlagSet(
            NoiseModeLowRegister,
            NoiseModeHighRegister,
            voiceIndex)
            ? (short)_noiseLevel
            : InterpolateVoiceSample(voice);
        sample = sample * voice.EnvelopeLevel / 0x8000;

        uint pitch = CalculatePitch(voiceIndex, previousVoiceOutput);
        voice.PitchCounter += pitch;
        while (voice.PitchCounter >= 0x1000)
        {
            voice.PitchCounter -= 0x1000;
            voice.DecodedIndex++;
        }

        return sample;
    }

    private uint CalculatePitch(int voiceIndex, int previousVoiceOutput)
    {
        uint voiceAddress = BaseAddress + (uint)voiceIndex * 0x10;
        ushort rawPitch = ReadRawRegister(voiceAddress + 4);
        uint pitch = rawPitch;

        if (voiceIndex > 0 &&
            IsVoiceFlagSet(
                PitchModulationLowRegister,
                PitchModulationHighRegister,
                voiceIndex))
        {
            int factor = Math.Clamp(
                previousVoiceOutput,
                short.MinValue,
                short.MaxValue) + 0x8000;
            int modulatedPitch = ((short)rawPitch * factor) >> 15;
            pitch = (uint)modulatedPitch & 0xFFFF;
        }

        return Math.Min(pitch, 0x4000u);
    }

    private static int InterpolateVoiceSample(SpuVoice voice)
    {
        int current = voice.DecodedSamples[voice.DecodedIndex];
        int nextIndex = voice.DecodedIndex + 1;
        int next = nextIndex < voice.DecodedSamples.Length
            ? voice.DecodedSamples[nextIndex]
            : current;
        int fraction = (int)(voice.PitchCounter & 0x0FFF);
        return current + ((next - current) * fraction >> 12);
    }

    private bool IsVoiceFlagSet(
        uint lowRegister,
        uint highRegister,
        int voiceIndex)
    {
        if (voiceIndex < 16)
            return (ReadRawRegister(lowRegister) & (1 << voiceIndex)) != 0;

        return (ReadRawRegister(highRegister) &
                (1 << (voiceIndex - 16))) != 0;
    }

    private void AdvanceNoise()
    {
        int noiseShift = (Control >> 10) & 0x0F;
        int noiseStep = 4 + ((Control >> 8) & 3);
        _noiseTimer -= noiseStep;

        int parity =
            ((_noiseLevel >> 15) ^
             (_noiseLevel >> 12) ^
             (_noiseLevel >> 11) ^
             (_noiseLevel >> 10) ^
             1) & 1;

        if (_noiseTimer >= 0)
            return;

        _noiseLevel = (ushort)((_noiseLevel << 1) | parity);
        int reload = 0x20_000 >> noiseShift;
        _noiseTimer += reload;
        if (_noiseTimer < 0)
            _noiseTimer += reload;
    }

    private void CaptureSamples(
        StereoSample cdAudio,
        int voice1Output,
        int voice3Output)
    {
        WriteCaptureSample(CdLeftCaptureBase, cdAudio.Left);
        WriteCaptureSample(CdRightCaptureBase, cdAudio.Right);
        WriteCaptureSample(
            Voice1CaptureBase,
            (short)Math.Clamp(
                voice1Output,
                short.MinValue,
                short.MaxValue));
        WriteCaptureSample(
            Voice3CaptureBase,
            (short)Math.Clamp(
                voice3Output,
                short.MinValue,
                short.MaxValue));

        _captureBufferPosition =
            (_captureBufferPosition + 2) % CaptureBufferSize;
        UpdateCaptureStatus();
    }

    private void WriteCaptureSample(int bufferBase, short sample)
    {
        int address = bufferBase + _captureBufferPosition;
        _soundRam[address] = (byte)sample;
        _soundRam[address + 1] = (byte)(sample >> 8);

        if ((ReadRawRegister(TransferControlRegister) & 0x000C) != 0)
            CheckIrqAccess((uint)address, 2);
    }

    private void UpdateCaptureStatus()
    {
        ushort status = ReadRawRegister(StatusRegister);
        bool reportCaptureHalf =
            (ReadRawRegister(TransferControlRegister) & 0x000C) != 0;
        if (reportCaptureHalf && _captureBufferPosition >= CaptureBufferSize / 2)
            status |= 1 << 11;
        else
            status &= unchecked((ushort)~(1 << 11));
        WriteRawRegister(StatusRegister, status);
    }

    private void DecodeAdpcmBlock(int voiceIndex)
    {
        SpuVoice voice = _voices[voiceIndex];
        int address = (int)(voice.CurrentAddress % SoundRamSize);
        CheckIrqAccess((uint)address, 16);
        byte header = _soundRam[address];
        byte flags = _soundRam[(address + 1) % SoundRamSize];
        int shift = Math.Min(header & 0x0F, 12);
        int filter = Math.Min(header >> 4, 4);

        for (int sampleIndex = 0; sampleIndex < 28; sampleIndex++)
        {
            byte packed = _soundRam[(address + 2 + sampleIndex / 2) % SoundRamSize];
            int nibble = (sampleIndex & 1) == 0 ? packed & 0x0F : packed >> 4;
            if ((nibble & 8) != 0)
                nibble -= 16;

            int sample = (nibble << 12) >> shift;
            sample += (voice.PreviousSample * PositiveFilter[filter] +
                       voice.PreviousSample2 * NegativeFilter[filter] + 32) >> 6;
            sample = Math.Clamp(sample, short.MinValue, short.MaxValue);
            voice.DecodedSamples[sampleIndex] = (short)sample;
            voice.PreviousSample2 = voice.PreviousSample;
            voice.PreviousSample = sample;
        }

        if ((flags & 4) != 0)
        {
            voice.RepeatAddress = voice.CurrentAddress;
            uint voiceAddress = BaseAddress + (uint)voiceIndex * 0x10;
            WriteRawRegister(voiceAddress + 14, (ushort)(voice.RepeatAddress / 8));
        }

        voice.CurrentAddress = (voice.CurrentAddress + 16) % SoundRamSize;
        if ((flags & 1) != 0)
        {
            _endFlags |= 1u << voiceIndex;
            if ((flags & 2) != 0)
                voice.CurrentAddress = voice.RepeatAddress;
            else
            {
                voice.StopAfterBlock = true;
                voice.EnvelopePhase = EnvelopePhase.Release;
            }
        }

        voice.DecodedIndex = 0;
    }

    private void AdvanceEnvelope(int voiceIndex)
    {
        SpuVoice voice = _voices[voiceIndex];
        uint voiceAddress = BaseAddress + (uint)voiceIndex * 0x10;
        ushort low = ReadRawRegister(voiceAddress + 8);
        ushort high = ReadRawRegister(voiceAddress + 10);

        switch (voice.EnvelopePhase)
        {
            case EnvelopePhase.Attack:
                ApplyEnvelopeStep(
                    voice,
                    (low >> 10) & 0x1F,
                    (low >> 8) & 3,
                    decreasing: false,
                    exponential: (low & 0x8000) != 0);
                if (voice.EnvelopeLevel >= 0x7FFF)
                    voice.EnvelopePhase = EnvelopePhase.Decay;
                break;

            case EnvelopePhase.Decay:
                ApplyEnvelopeStep(
                    voice,
                    (low >> 4) & 0x0F,
                    0,
                    decreasing: true,
                    exponential: true);
                int sustainLevel = Math.Min(((low & 0x0F) + 1) * 0x800, 0x7FFF);
                if (voice.EnvelopeLevel <= sustainLevel)
                    voice.EnvelopePhase = EnvelopePhase.Sustain;
                break;

            case EnvelopePhase.Sustain:
                ApplyEnvelopeStep(
                    voice,
                    (high >> 8) & 0x1F,
                    (high >> 6) & 3,
                    decreasing: (high & 0x4000) != 0,
                    exponential: (high & 0x8000) != 0);
                break;

            case EnvelopePhase.Release:
                ApplyEnvelopeStep(
                    voice,
                    high & 0x1F,
                    0,
                    decreasing: true,
                    exponential: (high & 0x20) != 0);
                if (voice.EnvelopeLevel <= 0)
                {
                    voice.Active = false;
                    voice.EnvelopePhase = EnvelopePhase.Off;
                }
                break;
        }

        WriteRawRegister(voiceAddress + 12, (ushort)voice.EnvelopeLevel);
    }

    private static void ApplyEnvelopeStep(
        SpuVoice voice,
        int shift,
        int stepValue,
        bool decreasing,
        bool exponential)
    {
        int step = 7 - stepValue;
        if (decreasing)
            step = ~step;
        step <<= Math.Max(0, 11 - shift);

        int counterIncrement = 0x8000 >> Math.Max(0, shift - 11);
        if (exponential && !decreasing && voice.EnvelopeLevel > 0x6000)
        {
            if (shift < 10)
                step >>= 2;
            else if (shift >= 11)
                counterIncrement >>= 2;
            else
            {
                step >>= 1;
                counterIncrement >>= 1;
            }
        }
        else if (exponential && decreasing)
        {
            step = step * voice.EnvelopeLevel / 0x8000;
        }

        voice.EnvelopeCounter += Math.Max(counterIncrement, 1);
        if ((voice.EnvelopeCounter & 0x8000) == 0)
            return;

        voice.EnvelopeCounter &= 0x7FFF;
        voice.EnvelopeLevel = Math.Clamp(
            voice.EnvelopeLevel + step,
            0,
            0x7FFF);
    }

    private void WriteSoundRamHalfword(ushort value)
    {
        int address = (int)_currentTransferAddress;
        _soundRam[address] = (byte)value;
        _soundRam[(address + 1) % SoundRamSize] = (byte)(value >> 8);
        CheckIrqAccess((uint)address, 2);
        _currentTransferAddress = (_currentTransferAddress + 2) % SoundRamSize;
    }

    private ushort ReadSoundRamHalfword()
    {
        int address = (int)_currentTransferAddress;
        ushort value = (ushort)(
            _soundRam[address] |
            (_soundRam[(address + 1) % SoundRamSize] << 8));
        CheckIrqAccess((uint)address, 2);
        _currentTransferAddress = (_currentTransferAddress + 2) % SoundRamSize;
        return value;
    }

    private void CheckIrqAccess(uint address, int byteCount)
    {
        if ((Control & 0x8040) != 0x8040)
            return;

        uint irqAddress =
            (uint)ReadRawRegister(InterruptAddressRegister) * 8 %
            SoundRamSize;
        for (int offset = 0; offset < byteCount; offset++)
        {
            if ((address + (uint)offset) % SoundRamSize != irqAddress)
                continue;

            ushort status = ReadRawRegister(StatusRegister);
            if ((status & (1 << 6)) != 0)
                return;

            WriteRawRegister(
                StatusRegister,
                (ushort)(status | (1 << 6)));
            _interruptController?.Request(InterruptSource.Spu);
            return;
        }
    }

    private void ClearIrqFlag()
    {
        ushort status = ReadRawRegister(StatusRegister);
        WriteRawRegister(
            StatusRegister,
            (ushort)(status & ~(1 << 6)));
    }

    private static int DecodeVolume(ushort value)
    {
        int volume = value & 0x7FFF;
        if ((volume & 0x4000) != 0)
            volume |= ~0x7FFF;
        return Math.Clamp(volume * 2, short.MinValue, short.MaxValue);
    }

    private static int DecodeSignedVolume(ushort value) => (short)value;
}

public readonly record struct StereoSample(short Left, short Right);
