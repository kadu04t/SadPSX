using SadPSX.Core.Bus;
using SadPSX.Core.CdRom.Media;
using SadPSX.Core.Interrupts;

namespace SadPSX.Core.CdRom;

public sealed class CdRomController : IMmioDevice, IClockedDevice
{
    public const uint BaseAddress = 0x1F80_1800;
    public const uint EndAddress = 0x1F80_1803;
    public const uint SingleSpeedSectorCycles = 33_868_800 / 75;
    public const uint DoubleSpeedSectorCycles = SingleSpeedSectorCycles / 2;
    public const uint DefaultCommandDelayCycles = 20_000;
    public const uint InitializationCommandDelayCycles = 80_000;
    public const uint SecondResponseDelayCycles = 20_000;
    public const uint SeekResponseDelayCycles = 100_000;

    private const int FifoCapacity = 16;
    private const int MaximumSectorBuffers = 8;
    private const byte NoDiscStatus = 1 << 4;

    private readonly InterruptController _interruptController;
    private readonly Queue<byte> _parameters = new(FifoCapacity);
    private readonly Queue<byte> _results = new(FifoCapacity);
    private readonly Queue<byte> _data = new();
    private readonly Queue<Response> _responses = new();
    private readonly Queue<SectorBuffer> _sectorBuffers = new();

    private byte _index;
    private byte _interruptEnable;
    private byte _interruptFlags;
    private byte _mode;
    private byte _filterFile;
    private byte _filterChannel;
    private bool _commandBusy;
    private uint _commandCyclesRemaining;
    private ulong _clockCycles;
    private DiscImage? _disc;
    private int _pendingLogicalBlockAddress;
    private bool _reading;
    private bool _playing;
    private bool _muted;
    private bool _motorOn;
    private bool _seeking;
    private uint _readCyclesRemaining;
    private uint _playCyclesRemaining;
    private SectorBuffer? _lastSector;

    public CdRomController(InterruptController interruptController)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        Reset();
    }

    public byte Index => _index;
    public byte InterruptEnable => _interruptEnable;
    public byte InterruptFlags => _interruptFlags;
    public byte Mode => _mode;
    public byte LastCommand { get; private set; }
    public int ParameterCount => _parameters.Count;
    public int ResultCount => _results.Count;
    public bool CommandBusy => _commandBusy;
    public bool HasDisc => _disc is not null;
    public DiscBootInfo? BootInfo { get; private set; }
    public int DataCount => _data.Count;
    public int BufferedSectorCount => _sectorBuffers.Count;
    public bool IsReading => _reading;
    public bool IsPlaying => _playing;
    public bool IsSeeking => _seeking;
    public int CurrentLogicalBlockAddress => _pendingLogicalBlockAddress;
    public ulong CommandCount { get; private set; }

    public event Action<ReadOnlyMemory<byte>>? CdAudioSectorReady;

    public void LoadDisc(string path)
    {
        LoadDisc(DiscImage.Open(path));
    }

    public void LoadDisc(DiscImage disc)
    {
        ArgumentNullException.ThrowIfNull(disc);
        _disc?.Dispose();
        _disc = disc;
        BootInfo = disc.TryGetBootInfo(out DiscBootInfo? bootInfo)
            ? bootInfo
            : null;
        StopReading(clearBuffers: true);
        StopPlaying();
        _data.Clear();
        _motorOn = false;
        _seeking = false;
    }

    public void EjectDisc()
    {
        _disc?.Dispose();
        _disc = null;
        BootInfo = null;
        StopReading(clearBuffers: true);
        StopPlaying();
        _data.Clear();
        _motorOn = false;
        _seeking = false;
    }

    public void Reset()
    {
        _parameters.Clear();
        _results.Clear();
        _data.Clear();
        _responses.Clear();
        _sectorBuffers.Clear();
        _index = 0;
        _interruptEnable = 0;
        _interruptFlags = 0;
        _mode = 0;
        _filterFile = 0;
        _filterChannel = 0;
        _commandBusy = false;
        _commandCyclesRemaining = 0;
        _clockCycles = 0;
        _pendingLogicalBlockAddress = 0;
        _reading = false;
        _playing = false;
        _muted = false;
        _motorOn = false;
        _seeking = false;
        _readCyclesRemaining = 0;
        _playCyclesRemaining = 0;
        _lastSector = null;
        LastCommand = 0;
        CommandCount = 0;
    }

    public void Tick(uint cycles)
    {
        _clockCycles += cycles;

        if (_commandBusy && _interruptFlags == 0)
        {
            if (cycles < _commandCyclesRemaining)
            {
                _commandCyclesRemaining -= cycles;
            }
            else
            {
                _commandCyclesRemaining = 0;
                _commandBusy = false;
                ExecuteCommand(LastCommand);
            }
        }

        TickReading(cycles);
        TickPlaying(cycles);
        TryPresentResponse();
    }

    public bool Handles(uint address) =>
        address is >= BaseAddress and <= EndAddress;

    public byte Read8(uint address)
    {
        ValidateAddress(address);
        return (address - BaseAddress) switch
        {
            0 => ReadStatus(),
            1 => ReadResult(),
            2 => ReadDataByte(),
            3 => _index is 0 or 2
                ? (byte)(0xE0 | _interruptEnable)
                : (byte)(0xE0 | _interruptFlags),
            _ => 0,
        };
    }

    public ushort Read16(uint address)
    {
        EnsureAligned(address, 2, "Leitura");
        return (ushort)(Read8(address) | Read8(address + 1) << 8);
    }

    public uint Read32(uint address)
    {
        EnsureAligned(address, 4, "Leitura");
        return (uint)(Read8(address) |
            Read8(address + 1) << 8 |
            Read8(address + 2) << 16 |
            Read8(address + 3) << 24);
    }

    public void Write8(uint address, byte value)
    {
        ValidateAddress(address);
        switch (address - BaseAddress)
        {
            case 0:
                _index = (byte)(value & 3);
                break;

            case 1 when _index == 0:
                StartCommand(value);
                break;

            case 2 when _index == 0:
                if (_parameters.Count < FifoCapacity)
                    _parameters.Enqueue(value);
                break;

            case 2 when _index == 1:
                WriteInterruptEnable(value);
                break;

            case 3 when _index == 1:
                AcknowledgeInterrupt(value);
                break;

            case 3 when _index == 0:
                WriteRequest(value);
                break;
        }
    }

    public void Write16(uint address, ushort value)
    {
        EnsureAligned(address, 2, "Escrita");
        Write8(address, (byte)value);
        Write8(address + 1, (byte)(value >> 8));
    }

    public void Write32(uint address, uint value)
    {
        EnsureAligned(address, 4, "Escrita");
        Write8(address, (byte)value);
        Write8(address + 1, (byte)(value >> 8));
        Write8(address + 2, (byte)(value >> 16));
        Write8(address + 3, (byte)(value >> 24));
    }

    public string GetRegisterName(uint address)
    {
        ValidateAddress(address);
        return (address - BaseAddress) switch
        {
            0 => "CDROM_INDEX_STATUS",
            1 => _index == 0 ? "CDROM_COMMAND_RESULT" : "CDROM_RESPONSE",
            2 => _index == 0 ? "CDROM_PARAMETER_DATA" : "CDROM_IRQ_ENABLE",
            3 => _index == 1 ? "CDROM_IRQ_FLAGS" : "CDROM_REQUEST",
            _ => "CDROM_UNKNOWN",
        };
    }

    private byte ReadStatus()
    {
        byte value = _index;
        if (_parameters.Count == 0)
            value |= 1 << 3;
        if (_parameters.Count < FifoCapacity)
            value |= 1 << 4;
        if (_results.Count > 0)
            value |= 1 << 5;
        if (_commandBusy)
            value |= 1 << 7;
        if (_data.Count > 0)
            value |= 1 << 6;
        return value;
    }

    private byte ReadResult() =>
        _results.TryDequeue(out byte value) ? value : (byte)0;

    private void StartCommand(byte command)
    {
        if (_commandBusy)
            return;

        LastCommand = command;
        _commandBusy = true;
        _commandCyclesRemaining = command is 0x0A or 0x1E
            ? InitializationCommandDelayCycles
            : DefaultCommandDelayCycles;
    }

    private void ExecuteCommand(byte command)
    {
        CommandCount++;
        byte[] parameters = DrainParameters();
        byte status = GetStatus();
        switch (command)
        {
            case 0x01:
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x02:
                if (SetLocation(parameters))
                    QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x03:
                Play(parameters);
                break;

            case 0x0B:
                _muted = true;
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x0C:
                _muted = false;
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x1C:
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x07:
                MotorOn();
                break;

            case 0x08:
                Stop();
                break;

            case 0x09:
                Pause();
                break;

            case 0x15:
            case 0x16:
                Seek(command == 0x15);
                break;

            case 0x06:
            case 0x1B:
                StartRead();
                break;

            case 0x0A:
                StopReading(clearBuffers: true);
                StopPlaying();
                _mode = 0x20;
                _motorOn = _disc is not null;
                _seeking = false;
                QueueResponse(
                    CdRomInterruptType.Acknowledge,
                    GetStatus());
                QueueResponseAfter(
                    SecondResponseDelayCycles,
                    CdRomInterruptType.Complete,
                    [GetStatus()]);
                break;

            case 0x0D:
                _filterFile = ParameterAt(parameters, 0);
                _filterChannel = ParameterAt(parameters, 1);
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x0E:
                _mode = ParameterAt(parameters, 0);
                QueueResponse(CdRomInterruptType.Acknowledge, status);
                break;

            case 0x0F:
                QueueResponse(
                    CdRomInterruptType.Acknowledge,
                    status,
                    _mode,
                    0,
                    _filterFile,
                    _filterChannel);
                break;

            case 0x13:
                GetTrackNumbers(status);
                break;

            case 0x14:
                GetTrackDuration(parameters, status);
                break;

            case 0x10:
                GetLocationHeader(status);
                break;

            case 0x11:
                GetLocationPosition(status);
                break;

            case 0x12:
                SetSession(parameters);
                break;

            case 0x19:
                ExecuteTest(parameters);
                break;

            case 0x1A:
                GetId(status);
                break;

            case 0x1E:
                ReadTableOfContents();
                break;

            default:
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(NoDiscStatus | 1),
                    0x40);
                break;
        }
    }

    public uint ReadDmaWord()
    {
        uint value = 0;
        for (int index = 0; index < 4; index++)
            value |= (uint)ReadDataByte() << (index * 8);
        return value;
    }

    private bool SetLocation(byte[] parameters)
    {
        if (parameters.Length != 3 ||
            !IsValidBcd(parameters[0]) ||
            !IsValidBcd(parameters[1]) ||
            !IsValidBcd(parameters[2]) ||
            parameters[1] >= 0x60 ||
            parameters[2] >= 0x75)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(GetStatus() | 1),
                0x10);
            return false;
        }

        int minutes = FromBcd(ParameterAt(parameters, 0));
        int seconds = FromBcd(ParameterAt(parameters, 1));
        int frames = FromBcd(ParameterAt(parameters, 2));
        int logicalBlockAddress =
            (minutes * 60 + seconds) * 75 + frames - 150;
        if (logicalBlockAddress < 0)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(GetStatus() | 1),
                0x10);
            return false;
        }

        _pendingLogicalBlockAddress = logicalBlockAddress;
        return true;
    }

    private void StartRead()
    {
        byte status = GetStatus();
        if (_disc is null ||
            (uint)_pendingLogicalBlockAddress >= _disc.SectorCount)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        StopPlaying();
        _motorOn = true;
        _seeking = false;
        _reading = true;
        _readCyclesRemaining = GetSectorInterval();
        QueueResponse(CdRomInterruptType.Acknowledge, GetStatus());
    }

    private void MotorOn()
    {
        byte status = GetStatus();
        if (_disc is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        if (_motorOn)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x20);
            return;
        }

        QueueResponse(CdRomInterruptType.Acknowledge, status);
        QueueResponseAfter(
            SecondResponseDelayCycles,
            CdRomInterruptType.Complete,
            [(byte)(status | (1 << 1))],
            () => _motorOn = true);
    }

    private void Stop()
    {
        StopReading(clearBuffers: false);
        StopPlaying();
        _seeking = false;
        byte status = GetStatus();
        QueueResponse(CdRomInterruptType.Acknowledge, status);
        QueueResponseAfter(
            SecondResponseDelayCycles,
            CdRomInterruptType.Complete,
            [(byte)(status & ~(1 << 1))],
            () =>
            {
                _motorOn = false;
                _pendingLogicalBlockAddress = 0;
            });
    }

    private void Pause()
    {
        byte activeStatus = GetStatus();
        QueueResponse(CdRomInterruptType.Acknowledge, activeStatus);
        StopReading(clearBuffers: false);
        StopPlaying();
        _seeking = false;
        QueueResponseAfter(
            SecondResponseDelayCycles,
            CdRomInterruptType.Complete,
            [GetStatus()]);
    }

    private void Seek(bool logical)
    {
        byte status = GetStatus();
        if (_disc is null ||
            (uint)_pendingLogicalBlockAddress >= _disc.SectorCount)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        if (logical &&
            _disc.GetTrackAt(_pendingLogicalBlockAddress).Mode ==
                DiscTrackMode.Audio)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x04);
            return;
        }

        StopReading(clearBuffers: false);
        StopPlaying();
        _motorOn = true;
        _seeking = true;
        QueueResponse(CdRomInterruptType.Acknowledge, GetStatus());
        QueueResponseAfter(
            SeekResponseDelayCycles,
            CdRomInterruptType.Complete,
            [(byte)(GetStatus() & ~(1 << 6))],
            () => _seeking = false);
    }

    private void SetSession(byte[] parameters)
    {
        byte status = GetStatus();
        if (parameters.Length != 1 ||
            parameters[0] != 1 ||
            _disc is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                parameters.Length == 1 && parameters[0] == 0
                    ? (byte)0x10
                    : (byte)0x80);
            return;
        }

        if (_reading)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        StopPlaying();
        _motorOn = true;
        _seeking = true;
        QueueResponse(CdRomInterruptType.Acknowledge, GetStatus());
        QueueResponseAfter(
            SeekResponseDelayCycles,
            CdRomInterruptType.Complete,
            [(byte)(GetStatus() & ~(1 << 6))],
            () => _seeking = false);
    }

    private void ReadTableOfContents()
    {
        byte status = GetStatus();
        if (_disc is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        _motorOn = true;
        QueueResponse(CdRomInterruptType.Acknowledge, GetStatus());
        QueueResponseAfter(
            SecondResponseDelayCycles,
            CdRomInterruptType.Complete,
            [GetStatus()]);
    }

    private void Play(byte[] parameters)
    {
        byte status = GetStatus();
        if (_disc is null || parameters.Length > 1)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                parameters.Length > 1 ? (byte)0x10 : (byte)0x80);
            return;
        }

        byte requestedTrack = ParameterAt(parameters, 0);
        if (requestedTrack != 0)
        {
            if (!IsValidBcd(requestedTrack))
            {
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(status | 1),
                    0x10);
                return;
            }

            try
            {
                _pendingLogicalBlockAddress =
                    _disc.GetTrack((byte)FromBcd(requestedTrack))
                        .StartLogicalBlockAddress;
            }
            catch (ArgumentOutOfRangeException)
            {
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(status | 1),
                    0x10);
                return;
            }
        }

        if ((uint)_pendingLogicalBlockAddress >= _disc.SectorCount)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        StopReading(clearBuffers: false);
        _motorOn = true;
        _seeking = false;
        _playing = true;
        _playCyclesRemaining = GetSectorInterval();
        QueueResponse(CdRomInterruptType.Acknowledge, GetStatus());
    }

    private void WriteRequest(byte value)
    {
        if ((value & 0x80) == 0)
        {
            _data.Clear();
            return;
        }

        if (_sectorBuffers.Count == 0)
            return;

        SectorBuffer sector = _sectorBuffers.Dequeue();
        _data.Clear();
        bool rawSector = (_mode & (1 << 5)) != 0;
        int offset = rawSector
            ? 12
            : sector.Mode == DiscTrackMode.Mode1 ? 16 : 24;
        int length = rawSector ? 2340 : 2048;
        for (int index = 0; index < length; index++)
            _data.Enqueue(sector.Data[offset + index]);
    }

    private byte ReadDataByte() =>
        _data.TryDequeue(out byte value) ? value : (byte)0;

    private void GetTrackDuration(byte[] parameters, byte status)
    {
        if (_disc is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0,
                0);
            return;
        }

        byte trackNumber = ParameterAt(parameters, 0);
        int logicalBlockAddress;
        if (trackNumber == 0)
        {
            logicalBlockAddress = _disc.SectorCount;
        }
        else
        {
            try
            {
                logicalBlockAddress =
                    _disc.GetTrack((byte)FromBcd(trackNumber))
                        .StartLogicalBlockAddress;
            }
            catch (ArgumentOutOfRangeException)
            {
                QueueResponse(
                    CdRomInterruptType.DiskError,
                    (byte)(status | 1),
                    0x10);
                return;
            }
        }

        int absoluteFrames = logicalBlockAddress + 150;
        QueueResponse(
            CdRomInterruptType.Acknowledge,
            status,
            ToBcd(absoluteFrames / (60 * 75)),
            ToBcd(absoluteFrames / 75 % 60));
    }

    private void GetTrackNumbers(byte status)
    {
        if (_disc is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                1,
                1);
            return;
        }

        QueueResponse(
            CdRomInterruptType.Acknowledge,
            status,
            ToBcd(_disc.Tracks[0].Number),
            ToBcd(_disc.Tracks[^1].Number));
    }

    private void GetLocationHeader(byte status)
    {
        if (_lastSector is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        byte[] sector = _lastSector.Data;
        QueueResponse(
            CdRomInterruptType.Acknowledge,
            sector[12],
            sector[13],
            sector[14],
            sector[15],
            sector[16],
            sector[17],
            sector[18],
            sector[19]);
    }

    private void GetLocationPosition(byte status)
    {
        if (_disc is null || _lastSector is null)
        {
            QueueResponse(
                CdRomInterruptType.DiskError,
                (byte)(status | 1),
                0x80);
            return;
        }

        DiscTrack track = _disc.GetTrackAt(_lastSector.LogicalBlockAddress);
        int relativeFrames =
            _lastSector.LogicalBlockAddress - track.StartLogicalBlockAddress;
        int absoluteFrames = _lastSector.LogicalBlockAddress + 150;
        QueueResponse(
            CdRomInterruptType.Acknowledge,
            ToBcd(track.Number),
            1,
            ToBcd(relativeFrames / (60 * 75)),
            ToBcd(relativeFrames / 75 % 60),
            ToBcd(relativeFrames % 75),
            ToBcd(absoluteFrames / (60 * 75)),
            ToBcd(absoluteFrames / 75 % 60),
            ToBcd(absoluteFrames % 75));
    }

    private void GetId(byte status)
    {
        QueueResponse(CdRomInterruptType.Acknowledge, status);
        if (_disc is null)
        {
            QueueResponseAfter(
                SecondResponseDelayCycles,
                CdRomInterruptType.DiskError,
                [0x08, 0x40, 0, 0, 0, 0, 0, 0]);
            return;
        }

        _motorOn = true;
        QueueResponseAfter(
            SecondResponseDelayCycles,
            CdRomInterruptType.Complete,
            [
                GetStatus(),
                0,
                0x20,
                0,
                (byte)'S',
                (byte)'C',
                (byte)'E',
                (byte)'A',
            ]);
    }

    private void ExecuteTest(byte[] parameters)
    {
        switch (ParameterAt(parameters, 0))
        {
            case 0x20:
                QueueResponse(
                    CdRomInterruptType.Acknowledge,
                    0x98,
                    0x06,
                    0x10,
                    0xC3);
                break;

            default:
                QueueResponse(CdRomInterruptType.Acknowledge, GetStatus());
                break;
        }
    }

    private void QueueResponse(
        CdRomInterruptType interrupt,
        params byte[] result)
    {
        QueueResponseAfter(0, interrupt, result);
    }

    private void QueueResponseAfter(
        uint delayCycles,
        CdRomInterruptType interrupt,
        byte[] result,
        Action? onReady = null)
    {
        _responses.Enqueue(new Response(
            interrupt,
            result,
            _clockCycles + delayCycles,
            onReady));
    }

    private void TryPresentResponse()
    {
        if (_interruptFlags != 0 ||
            !_responses.TryPeek(out Response? response) ||
            response.ReadyAtCycle > _clockCycles)
        {
            return;
        }

        _responses.Dequeue();
        response.OnReady?.Invoke();
        _results.Clear();
        foreach (byte value in response.Result)
        {
            if (_results.Count < FifoCapacity)
                _results.Enqueue(value);
        }

        _interruptFlags = (byte)response.Interrupt;
        RequestInterruptIfEnabled();
    }

    private void WriteInterruptEnable(byte value)
    {
        _interruptEnable = (byte)(value & 0x1F);
        RequestInterruptIfEnabled();
    }

    private void AcknowledgeInterrupt(byte value)
    {
        _interruptFlags &= (byte)~(value & 0x1F);
        if ((value & 0x40) != 0)
            _parameters.Clear();
        if (_interruptFlags == 0)
            TryPresentResponse();
        RequestInterruptIfEnabled();
    }

    private void RequestInterruptIfEnabled()
    {
        if ((_interruptEnable & _interruptFlags) != 0)
            _interruptController.Request(InterruptSource.CdRom);
    }

    private byte[] DrainParameters()
    {
        byte[] values = _parameters.ToArray();
        _parameters.Clear();
        return values;
    }

    private static byte ParameterAt(byte[] parameters, int index) =>
        index < parameters.Length ? parameters[index] : (byte)0;

    private void TickReading(uint cycles)
    {
        if (!_reading || _disc is null)
            return;

        uint remainingCycles = cycles;
        while (_reading && remainingCycles >= _readCyclesRemaining)
        {
            remainingCycles -= _readCyclesRemaining;
            ReadNextSector();
            _readCyclesRemaining = GetSectorInterval();
        }

        if (_reading)
            _readCyclesRemaining -= remainingCycles;
    }

    private void TickPlaying(uint cycles)
    {
        if (!_playing || _disc is null)
            return;

        uint remainingCycles = cycles;
        while (_playing && remainingCycles >= _playCyclesRemaining)
        {
            remainingCycles -= _playCyclesRemaining;
            PlayNextSector();
            _playCyclesRemaining = GetSectorInterval();
        }

        if (_playing)
            _playCyclesRemaining -= remainingCycles;
    }

    private void PlayNextSector()
    {
        if (_disc is null)
            return;

        if ((uint)_pendingLogicalBlockAddress >= _disc.SectorCount)
        {
            CompletePlayback(endOfDisc: true);
            return;
        }

        DiscTrack currentTrack =
            _disc.GetTrackAt(_pendingLogicalBlockAddress);
        var sector = new byte[DiscImage.RawSectorSize];
        _disc.ReadSector(_pendingLogicalBlockAddress, sector);
        if (!_muted && currentTrack.Mode == DiscTrackMode.Audio)
            CdAudioSectorReady?.Invoke(sector);

        _pendingLogicalBlockAddress++;
        if (_pendingLogicalBlockAddress >= _disc.SectorCount)
        {
            CompletePlayback(endOfDisc: true);
            return;
        }

        DiscTrack nextTrack =
            _disc.GetTrackAt(_pendingLogicalBlockAddress);
        bool autoPause = (_mode & (1 << 1)) != 0;
        if (autoPause && nextTrack.Number != currentTrack.Number)
        {
            _pendingLogicalBlockAddress--;
            CompletePlayback(endOfDisc: false);
        }
    }

    private void CompletePlayback(bool endOfDisc)
    {
        _playing = false;
        _playCyclesRemaining = 0;
        if (endOfDisc)
            _motorOn = false;
        QueueResponse(CdRomInterruptType.DataEnd, GetStatus());
        TryPresentResponse();
    }

    private void ReadNextSector()
    {
        if (_disc is null)
            return;

        if ((uint)_pendingLogicalBlockAddress >= _disc.SectorCount)
        {
            _reading = false;
            QueueResponse(CdRomInterruptType.DataEnd, GetStatus());
            TryPresentResponse();
            return;
        }

        var data = new byte[DiscImage.RawSectorSize];
        _disc.ReadSector(_pendingLogicalBlockAddress, data);
        DiscTrackMode mode =
            _disc.GetTrackAt(_pendingLogicalBlockAddress).Mode;
        var sector = new SectorBuffer(
            _pendingLogicalBlockAddress,
            mode,
            data);
        _pendingLogicalBlockAddress++;
        _lastSector = sector;

        if (_sectorBuffers.Count == MaximumSectorBuffers)
            _sectorBuffers.Dequeue();
        _sectorBuffers.Enqueue(sector);
        QueueResponse(CdRomInterruptType.DataReady, GetStatus());
        TryPresentResponse();
    }

    private void StopReading(bool clearBuffers)
    {
        _reading = false;
        _readCyclesRemaining = 0;
        if (clearBuffers)
        {
            _sectorBuffers.Clear();
            _data.Clear();
            _lastSector = null;
        }
    }

    private void StopPlaying()
    {
        _playing = false;
        _playCyclesRemaining = 0;
    }

    private uint GetSectorInterval() =>
        (_mode & 0x80) != 0
            ? DoubleSpeedSectorCycles
            : SingleSpeedSectorCycles;

    private byte GetStatus()
    {
        if (_disc is null)
            return NoDiscStatus;

        byte status = 0;
        if (_motorOn)
            status |= 1 << 1;
        if (_reading)
            status |= 1 << 5;
        if (_seeking)
            status |= 1 << 6;
        if (_playing)
            status |= 1 << 7;
        return status;
    }

    private static int FromBcd(byte value) =>
        (value >> 4) * 10 + (value & 0x0F);

    private static byte ToBcd(int value) =>
        (byte)(((value / 10) << 4) | (value % 10));

    private static bool IsValidBcd(byte value) =>
        (value & 0x0F) < 10 && (value >> 4) < 10;

    private static void EnsureAligned(uint address, uint width, string operation)
    {
        if ((address & (width - 1)) != 0)
        {
            throw new InvalidOperationException(
                $"{operation} de {width * 8} bits desalinhada no CD-ROM: 0x{address:X8}.");
        }
    }

    private static void ValidateAddress(uint address)
    {
        if (address is < BaseAddress or > EndAddress)
        {
            throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence ao CD-ROM.");
        }
    }

    private sealed record Response(
        CdRomInterruptType Interrupt,
        byte[] Result,
        ulong ReadyAtCycle,
        Action? OnReady);

    private sealed record SectorBuffer(
        int LogicalBlockAddress,
        DiscTrackMode Mode,
        byte[] Data);
}
