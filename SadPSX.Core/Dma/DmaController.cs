using SadPSX.Core.Bus;
using SadPSX.Core.CdRom;
using SadPSX.Core.Interrupts;
using SadPSX.Core.Memory;
using GpuDevice = SadPSX.Core.Gpu.Gpu;
using MdecDevice = SadPSX.Core.Mdec.Mdec;
using SpuDevice = SadPSX.Core.Spu.Spu;

namespace SadPSX.Core.Dma;

public sealed class DmaController : IMmioDevice, IClockedDevice
{
    public const uint ChannelBaseAddress = 0x1F80_1080;
    public const uint ControlAddress = 0x1F80_10F0;
    public const uint InterruptAddress = 0x1F80_10F4;
    public const uint UnknownF8Address = 0x1F80_10F8;
    public const uint UnknownFcAddress = 0x1F80_10FC;
    public const uint ResetControl = 0x0765_4321;

    private const uint ChannelStride = 0x10;
    private const uint BaseAddressOffset = 0;
    private const uint BlockControlOffset = 4;
    private const uint ChannelControlOffset = 8;
    private const uint BusyBit = 1u << 24;
    private const uint TriggerBit = 1u << 28;
    private const uint MasterInterruptEnableBit = 1u << 23;
    private const uint MasterInterruptFlagBit = 1u << 31;
    private const uint BusErrorBit = 1u << 15;
    private const uint ChannelInterruptFlagsMask = 0x7F00_0000;
    private const uint DicrControlMask = 0x00FF_807F;
    private const uint RamAddressMask = 0x001F_FFFC;
    private const uint DmaAddressMask = 0x00FF_FFFC;
    private const uint DmaAddressLimit = 0x0080_0000;
    private const ulong MaximumTransferWords = 0x0100_0000;
    private const int MdecInputChannel = 0;
    private const int MdecOutputChannel = 1;
    private const int GpuChannel = 2;
    private const int CdRomChannel = 3;
    private const int SpuChannel = 4;
    private const int OtcChannel = 6;

    private readonly InterruptController _interruptController;
    private readonly MdecDevice _mdec;
    private readonly GpuDevice _gpu;
    private readonly CdRomController _cdRom;
    private readonly SpuDevice _spu;
    private readonly ChannelState[] _channels =
    [
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
    ];

    private Ram _ram;
    private uint _control;
    private uint _interrupt;
    private GpuTransferState? _gpuTransfer;
    private ulong _clockCycles;
    private ulong _gpuWaitStartCycle;
    private DmaGpuWaitReason _gpuWaitReason;

    public DmaController(
        InterruptController interruptController,
        MdecDevice mdec,
        GpuDevice gpu,
        CdRomController cdRom,
        SpuDevice spu,
        Ram? ram = null)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        _mdec = mdec ?? throw new ArgumentNullException(nameof(mdec));
        _gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
        _cdRom = cdRom ?? throw new ArgumentNullException(nameof(cdRom));
        _spu = spu ?? throw new ArgumentNullException(nameof(spu));
        _ram = ram ?? new Ram();
        Reset();
    }

    public uint Control => _control;

    public uint Interrupt => _interrupt;

    public ulong CompletedTransfers { get; private set; }

    public DmaGpuWaitReason GpuWaitReason => _gpuWaitReason;

    public ulong GpuWaitCycles =>
        _gpuWaitReason == DmaGpuWaitReason.None
            ? 0
            : _clockCycles - _gpuWaitStartCycle;

    public bool CpuBusHeld
    {
        get
        {
            GpuTransferState? transfer = _gpuTransfer;
            if (transfer is null ||
                transfer.Halted ||
                transfer.CpuWindowCyclesRemaining > 0)
            {
                return false;
            }

            ChannelState channel = _channels[GpuChannel];
            if (transfer.Forced &&
                (channel.ChannelControl & (1u << 29)) != 0)
            {
                return false;
            }

            if (transfer.LinkedList)
            {
                return transfer.RequestAccepted ||
                       GpuDmaRequestAsserted(fromRam: true);
            }

            if (transfer.WordsInBlockRemaining > 0)
                return true;

            return transfer.Forced ||
                   GpuDmaRequestAsserted(transfer.FromRam);
        }
    }

    public void ConnectRam(Ram ram)
    {
        _ram = ram ?? throw new ArgumentNullException(nameof(ram));
    }

    public DmaChannelSnapshot GetChannel(int channel)
    {
        ChannelState state = GetChannelState(channel);
        return new DmaChannelSnapshot(
            state.BaseAddress,
            state.BlockControl,
            ComposeChannelControl(channel, state));
    }

    public DmaChannelRuntimeSnapshot GetChannelRuntime(int channel)
    {
        ChannelState state = GetChannelState(channel);
        uint channelControl = ComposeChannelControl(channel, state);
        bool busy = (channelControl & BusyBit) != 0;
        ulong activeCycles = busy && state.Active
            ? _clockCycles - state.StartCycle
            : 0;
        return new DmaChannelRuntimeSnapshot(
            state.BaseAddress,
            state.BlockControl,
            channelControl,
            busy,
            activeCycles,
            state.LastTransferCycles,
            state.LongestTransferCycles,
            state.CompletedTransfers);
    }

    public DmaGpuTransferSnapshot GetGpuTransferRuntime()
    {
        GpuTransferState? transfer = _gpuTransfer;
        if (transfer is null)
            return default;

        return new DmaGpuTransferSnapshot(
            true,
            transfer.LinkedList,
            transfer.HeaderPending,
            transfer.StartAddress,
            transfer.HeaderAddress,
            transfer.CommandsInNode,
            transfer.CurrentAddress,
            transfer.CommandAddress,
            transfer.CommandsRemaining,
            transfer.NextAddress,
            transfer.TransferredWords);
    }

    public void Reset()
    {
        foreach (ChannelState channel in _channels)
            channel.Reset();

        _control = ResetControl;
        _interrupt = 0;
        _gpuTransfer = null;
        _clockCycles = 0;
        _gpuWaitStartCycle = 0;
        _gpuWaitReason = DmaGpuWaitReason.None;
        CompletedTransfers = 0;
    }

    public void Tick(uint cycles)
    {
        if (cycles == 0)
            return;

        ulong tickStartCycle = _clockCycles;
        _clockCycles += cycles;
        uint cyclesRemaining = cycles;
        while (cyclesRemaining > 0)
        {
            TryStartPendingChannels();
            if (_gpuTransfer is null)
            {
                SetGpuWait(DmaGpuWaitReason.None, tickStartCycle);
                return;
            }

            GpuTransferState transfer = _gpuTransfer;
            if (transfer.Halted)
            {
                TryStartPendingNonGpuChannels();
                SetGpuWait(DmaGpuWaitReason.Halted, tickStartCycle);
                return;
            }

            if (transfer.CpuWindowCyclesRemaining > 0)
            {
                TryStartPendingNonGpuChannels();
                SetGpuWait(DmaGpuWaitReason.CpuWindow, tickStartCycle);
                uint elapsed = Math.Min(
                    cyclesRemaining,
                    transfer.CpuWindowCyclesRemaining);
                transfer.CpuWindowCyclesRemaining -= elapsed;
                cyclesRemaining -= elapsed;
                continue;
            }

            ChannelState channel = _channels[GpuChannel];
            if (transfer.Forced &&
                (channel.ChannelControl & (1u << 29)) != 0)
            {
                TryStartPendingNonGpuChannels();
                SetGpuWait(DmaGpuWaitReason.Paused, tickStartCycle);
                return;
            }

            bool transferred = transfer.LinkedList
                ? TickGpuLinkedList()
                : TickGpuBlock();
            if (!transferred)
            {
                TryStartPendingNonGpuChannels();
                SetGpuWait(DmaGpuWaitReason.Request, tickStartCycle);
                return;
            }

            SetGpuWait(DmaGpuWaitReason.None, tickStartCycle);
            cyclesRemaining--;
            if (ReferenceEquals(_gpuTransfer, transfer))
                ApplyGpuChopping(transfer);
        }
    }

    public bool Handles(uint address)
    {
        uint registerAddress = address & ~3u;
        if (registerAddress is ControlAddress or InterruptAddress or
            UnknownF8Address or UnknownFcAddress)
        {
            return true;
        }

        if (registerAddress < ChannelBaseAddress ||
            registerAddress >= ChannelBaseAddress + ChannelStride * 7)
        {
            return false;
        }

        uint registerOffset =
            (registerAddress - ChannelBaseAddress) % ChannelStride;
        return registerOffset is BaseAddressOffset or BlockControlOffset or
            ChannelControlOffset;
    }

    public byte Read8(uint address)
    {
        uint value = ReadRegister(address & ~3u);
        int shift = (int)((address & 3) * 8);
        return (byte)(value >> shift);
    }

    public ushort Read16(uint address)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Leitura de 16 bits desalinhada no DMA: 0x{address:X8}.");

        uint value = ReadRegister(address & ~3u);
        int shift = (int)((address & 2) * 8);
        return (ushort)(value >> shift);
    }

    public uint Read32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada no DMA: 0x{address:X8}.");

        return ReadRegister(address);
    }

    public void Write8(uint address, byte value)
    {
        uint registerAddress = address & ~3u;
        int shift = (int)((address & 3) * 8);
        uint writeMask = 0xFFu << shift;
        WriteRegister(registerAddress, (uint)value << shift, writeMask);
    }

    public void Write16(uint address, ushort value)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Escrita de 16 bits desalinhada no DMA: 0x{address:X8}.");

        uint registerAddress = address & ~3u;
        int shift = (int)((address & 2) * 8);
        uint writeMask = 0xFFFFu << shift;
        WriteRegister(registerAddress, (uint)value << shift, writeMask);
    }

    public void Write32(uint address, uint value)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Escrita de 32 bits desalinhada no DMA: 0x{address:X8}.");

        WriteRegister(address, value, uint.MaxValue);
    }

    public string GetRegisterName(uint address)
    {
        uint registerAddress = address & ~3u;
        return registerAddress switch
        {
            ControlAddress => "DPCR",
            InterruptAddress => "DICR",
            UnknownF8Address => "DMA_UNK_F8",
            UnknownFcAddress => "DMA_UNK_FC",
            _ => GetChannelRegisterName(registerAddress),
        };
    }

    private uint ReadRegister(uint registerAddress)
    {
        return registerAddress switch
        {
            ControlAddress => _control,
            InterruptAddress => _interrupt,
            UnknownF8Address => 0x7FFA_C68B,
            UnknownFcAddress => 0x00FF_FFF7,
            _ => ReadChannelRegister(registerAddress),
        };
    }

    private uint ReadChannelRegister(uint registerAddress)
    {
        (int channel, uint registerOffset) =
            DecodeChannelAddress(registerAddress);
        ChannelState state = _channels[channel];

        return registerOffset switch
        {
            BaseAddressOffset => state.BaseAddress,
            BlockControlOffset => state.BlockControl,
            ChannelControlOffset => ComposeChannelControl(channel, state),
            _ => throw UnknownRegister(registerAddress),
        };
    }

    private void WriteRegister(
        uint registerAddress,
        uint value,
        uint writeMask)
    {
        switch (registerAddress)
        {
            case ControlAddress:
                _control = Merge(_control, value, writeMask);
                TryStartPendingChannels();
                return;

            case InterruptAddress:
                WriteInterrupt(value, writeMask);
                return;

            case UnknownF8Address:
            case UnknownFcAddress:
                return;
        }

        (int channel, uint registerOffset) =
            DecodeChannelAddress(registerAddress);
        ChannelState state = _channels[channel];

        switch (registerOffset)
        {
            case BaseAddressOffset:
                state.BaseAddress =
                    Merge(state.BaseAddress, value, writeMask) & 0x00FF_FFFF;
                break;

            case BlockControlOffset:
                state.BlockControl =
                    Merge(state.BlockControl, value, writeMask);
                break;

            case ChannelControlOffset:
                uint current = ComposeChannelControl(channel, state);
                uint merged = Merge(current, value, writeMask);
                state.ChannelControl = NormalizeChannelControl(channel, merged);
                UpdateChannelActivity(
                    state,
                    (current & BusyBit) != 0,
                    (state.ChannelControl & BusyBit) != 0);
                if (channel == GpuChannel &&
                    (state.ChannelControl & BusyBit) == 0)
                {
                    _gpuTransfer = null;
                    SetGpuWait(DmaGpuWaitReason.None, _clockCycles);
                }
                TryStartPendingChannels();
                break;

            default:
                throw UnknownRegister(registerAddress);
        }
    }

    private void WriteInterrupt(uint value, uint writeMask)
    {
        bool wasMasterPending = (_interrupt & MasterInterruptFlagBit) != 0;
        uint writableControlMask = DicrControlMask & writeMask;
        _interrupt =
            (_interrupt & ~writableControlMask) |
            (value & writableControlMask);

        uint acknowledgedFlags =
            value & writeMask & ChannelInterruptFlagsMask;
        _interrupt &= ~acknowledgedFlags;
        RecomputeMasterInterrupt(wasMasterPending);
    }

    private void TryStartPendingChannels()
    {
        while (_gpuTransfer is null)
        {
            int channel = GetHighestPriorityPendingChannel();
            if (channel < 0)
                return;

            TryStartChannel(channel);
        }
    }

    private void TryStartChannel(int channel)
    {
        if (_gpuTransfer is not null && channel == GpuChannel)
            return;

        ChannelState state = _channels[channel];
        uint channelControl = ComposeChannelControl(channel, state);

        if ((channelControl & BusyBit) == 0 || !IsChannelEnabled(channel))
            return;

        uint synchronizationMode = (channelControl >> 9) & 3;
        if (synchronizationMode == 3)
        {
            SignalBusError();
            CompleteChannel(channel);
            return;
        }

        if (synchronizationMode == 0 &&
            (channelControl & TriggerBit) == 0)
        {
            return;
        }

        switch (channel)
        {
            case MdecInputChannel:
                TransferMdecInput(state, channelControl, synchronizationMode);
                CompleteChannel(channel);
                break;

            case MdecOutputChannel:
                TransferMdecOutput(state, channelControl, synchronizationMode);
                CompleteChannel(channel);
                break;

            case GpuChannel:
                BeginGpuTransfer(
                    state,
                    channelControl,
                    synchronizationMode);
                break;

            case CdRomChannel:
                TransferCdRom(state, channelControl, synchronizationMode);
                CompleteChannel(channel);
                break;

            case SpuChannel:
                TransferSpu(state, channelControl, synchronizationMode);
                CompleteChannel(channel);
                break;

            case OtcChannel:
                TransferOtc(state, synchronizationMode);
                CompleteChannel(channel);
                break;

            default:
                SignalBusError();
                CompleteChannel(channel);
                break;
        }
    }

    private void TryStartPendingNonGpuChannels()
    {
        while (true)
        {
            int channel = GetHighestPriorityPendingChannel(
                excludeGpu: true);
            if (channel < 0)
                return;

            TryStartChannel(channel);
        }
    }

    private int GetHighestPriorityPendingChannel(bool excludeGpu = false)
    {
        int selectedChannel = -1;
        int selectedPriority = int.MaxValue;

        for (int channel = 0; channel < _channels.Length; channel++)
        {
            if (excludeGpu && channel == GpuChannel)
                continue;
            if (!IsChannelPending(channel))
                continue;

            int priority = (int)((_control >> (channel * 4)) & 7);
            if (priority < selectedPriority ||
                (priority == selectedPriority &&
                 channel > selectedChannel))
            {
                selectedChannel = channel;
                selectedPriority = priority;
            }
        }

        return selectedChannel;
    }

    private bool IsChannelPending(int channel)
    {
        uint channelControl =
            ComposeChannelControl(channel, _channels[channel]);
        if ((channelControl & BusyBit) == 0 || !IsChannelEnabled(channel))
            return false;

        uint synchronizationMode = (channelControl >> 9) & 3;
        return synchronizationMode != 0 ||
               (channelControl & TriggerBit) != 0;
    }

    private void TransferMdecInput(
        ChannelState channel,
        uint channelControl,
        uint synchronizationMode)
    {
        if ((channelControl & 1) == 0 || synchronizationMode == 2)
        {
            SignalBusError();
            return;
        }

        TransferMdecWords(
            channel,
            channelControl,
            synchronizationMode,
            address => _mdec.WriteDmaWord(
                _ram.Read32(address & RamAddressMask)));
    }

    private void TransferMdecOutput(
        ChannelState channel,
        uint channelControl,
        uint synchronizationMode)
    {
        if ((channelControl & 1) != 0 || synchronizationMode == 2)
        {
            SignalBusError();
            return;
        }

        TransferMdecWords(
            channel,
            channelControl,
            synchronizationMode,
            address => _ram.Write32(
                address & RamAddressMask,
                _mdec.ReadDmaWord()));
    }

    private void TransferMdecWords(
        ChannelState channel,
        uint channelControl,
        uint synchronizationMode,
        Action<uint> transferWord)
    {
        ulong wordCount = GetWordCount(
            channel.BlockControl,
            synchronizationMode);
        if (wordCount > MaximumTransferWords)
        {
            SignalBusError();
            return;
        }

        bool decrement = (channelControl & 2) != 0;
        uint address = channel.BaseAddress & DmaAddressMask;
        uint step = decrement ? unchecked((uint)-4) : 4;
        for (ulong word = 0; word < wordCount; word++)
        {
            if (!IsRamDmaAddress(address))
            {
                SignalBusError();
                break;
            }

            transferWord(address);
            address = (address + step) & DmaAddressMask;
        }

        channel.BaseAddress = address & 0x00FF_FFFF;
        if (synchronizationMode == 1)
            channel.BlockControl &= 0x0000_FFFF;
    }

    private void TransferCdRom(
        ChannelState channel,
        uint channelControl,
        uint synchronizationMode)
    {
        if ((channelControl & 1) != 0 || synchronizationMode == 2)
        {
            SignalBusError();
            return;
        }

        ulong wordCount = GetWordCount(channel.BlockControl, synchronizationMode);
        if (wordCount > MaximumTransferWords)
        {
            SignalBusError();
            return;
        }

        uint address = channel.BaseAddress & DmaAddressMask;
        for (ulong word = 0; word < wordCount; word++)
        {
            if (!IsRamDmaAddress(address))
            {
                SignalBusError();
                break;
            }

            _ram.Write32(
                address & RamAddressMask,
                _cdRom.ReadDmaWord());
            address = (address + 4) & DmaAddressMask;
        }

        channel.BaseAddress = address & 0x00FF_FFFF;
        if (synchronizationMode == 1)
            channel.BlockControl &= 0x0000_FFFF;
    }

    private void BeginGpuTransfer(
        ChannelState channel,
        uint channelControl,
        uint synchronizationMode)
    {
        if (_gpuTransfer is not null)
            return;

        bool fromRam = (channelControl & 1) != 0;
        bool decrement = (channelControl & 2) != 0;

        if (synchronizationMode == 2)
        {
            if (!fromRam)
            {
                SignalBusError();
                CompleteChannel(GpuChannel);
                return;
            }

            _gpuTransfer = new GpuTransferState
            {
                LinkedList = true,
                FromRam = true,
                StartAddress = channel.BaseAddress & DmaAddressMask,
                CurrentAddress = channel.BaseAddress & DmaAddressMask,
                HeaderPending = true,
                VisitedLinkedListAddresses = [],
            };
            return;
        }

        uint blockSize = DecodeCount((ushort)channel.BlockControl);
        uint blockCount = synchronizationMode == 0
            ? 1
            : DecodeCount((ushort)(channel.BlockControl >> 16));
        ulong wordCount = (ulong)blockSize * blockCount;
        if (wordCount > MaximumTransferWords)
        {
            SignalBusError();
            CompleteChannel(GpuChannel);
            return;
        }

        _gpuTransfer = new GpuTransferState
        {
            FromRam = fromRam,
            StartAddress = channel.BaseAddress & DmaAddressMask,
            CurrentAddress = channel.BaseAddress & DmaAddressMask,
            AddressStep = decrement ? unchecked((uint)-4) : 4,
            SynchronizationMode = synchronizationMode,
            BlockSize = blockSize,
            BlocksRemaining = blockCount,
            Forced = synchronizationMode == 0 &&
                     (channelControl & TriggerBit) != 0,
            Halted = synchronizationMode == 1 &&
                     (channelControl & (1u << 8)) != 0,
            ChoppingEnabled = synchronizationMode == 0 &&
                              (channelControl & (1u << 8)) != 0,
            DmaWindowWords =
                1u << (int)((channelControl >> 16) & 7),
            CpuWindowCycles =
                1u << (int)((channelControl >> 20) & 7),
        };
        _gpuTransfer.WordsUntilChop = _gpuTransfer.DmaWindowWords;
    }

    private bool TickGpuBlock()
    {
        GpuTransferState transfer = _gpuTransfer!;
        ChannelState channel = _channels[GpuChannel];

        if (transfer.WordsInBlockRemaining == 0)
        {
            if (transfer.BlocksRemaining == 0)
            {
                CompleteChannel(GpuChannel);
                return false;
            }

            if (!transfer.Forced && !GpuDmaRequestAsserted(transfer.FromRam))
                return false;

            transfer.Forced = false;
            channel.ChannelControl &= ~TriggerBit;
            transfer.WordsInBlockRemaining = transfer.BlockSize;
        }

        if (!IsRamDmaAddress(transfer.CurrentAddress))
        {
            AbortGpuTransfer();
            return false;
        }

        if (transfer.FromRam)
        {
            if (!_gpu.TryWriteDmaWord(
                    _ram.Read32(
                        transfer.CurrentAddress & RamAddressMask)))
            {
                return false;
            }
        }
        else
        {
            _ram.Write32(
                transfer.CurrentAddress & RamAddressMask,
                _gpu.Read32(GpuDevice.Gp0Address));
        }

        transfer.CurrentAddress =
            (transfer.CurrentAddress + transfer.AddressStep) & DmaAddressMask;
        transfer.WordsInBlockRemaining--;

        if (transfer.SynchronizationMode == 1)
            channel.BaseAddress = transfer.CurrentAddress & 0x00FF_FFFF;

        if (transfer.WordsInBlockRemaining != 0)
            return true;

        transfer.BlocksRemaining--;
        if (transfer.SynchronizationMode == 1)
        {
            channel.BlockControl =
                (channel.BlockControl & 0x0000_FFFF) |
                (transfer.BlocksRemaining << 16);
        }

        if (transfer.BlocksRemaining == 0)
            CompleteChannel(GpuChannel);

        return true;
    }

    private void TransferSpu(
        ChannelState channel,
        uint channelControl,
        uint synchronizationMode)
    {
        if (synchronizationMode == 2)
        {
            SignalBusError();
            return;
        }

        ulong wordCount = GetWordCount(channel.BlockControl, synchronizationMode);
        if (wordCount > MaximumTransferWords)
        {
            SignalBusError();
            return;
        }

        bool fromRam = (channelControl & 1) != 0;
        bool decrement = (channelControl & 2) != 0;
        uint address = channel.BaseAddress & DmaAddressMask;
        uint step = decrement ? unchecked((uint)-4) : 4;

        for (ulong word = 0; word < wordCount; word++)
        {
            if (!IsRamDmaAddress(address))
            {
                SignalBusError();
                break;
            }

            if (fromRam)
                _spu.WriteDmaWord(_ram.Read32(address & RamAddressMask));
            else
                _ram.Write32(address & RamAddressMask, _spu.ReadDmaWord());

            address = (address + step) & DmaAddressMask;
        }

        channel.BaseAddress = address & 0x00FF_FFFF;
        if (synchronizationMode == 1)
            channel.BlockControl &= 0x0000_FFFF;
    }

    private bool TickGpuLinkedList()
    {
        GpuTransferState transfer = _gpuTransfer!;
        ChannelState channel = _channels[GpuChannel];

        if (transfer.HeaderPending)
        {
            if (!transfer.RequestAccepted)
            {
                if (!GpuDmaRequestAsserted(fromRam: true))
                    return false;

                transfer.RequestAccepted = true;
            }

            if (!IsRamDmaAddress(transfer.CurrentAddress) ||
                transfer.TransferredWords >= MaximumTransferWords)
            {
                AbortGpuTransfer();
                return false;
            }

            uint physicalHeaderAddress =
                transfer.CurrentAddress & RamAddressMask;
            if (!transfer.VisitedLinkedListAddresses!.Add(
                    physicalHeaderAddress))
            {
                AbortGpuTransfer();
                return false;
            }

            uint header = _ram.Read32(
                physicalHeaderAddress);
            transfer.HeaderAddress = transfer.CurrentAddress;
            transfer.CommandsRemaining = header >> 24;
            transfer.CommandsInNode = transfer.CommandsRemaining;
            transfer.CommandAddress =
                (transfer.CurrentAddress + 4) & DmaAddressMask;
            transfer.NextAddress = header & 0x00FF_FFFF;
            transfer.HeaderPending = false;
            transfer.TransferredWords++;
            channel.ChannelControl &= ~TriggerBit;

            if (transfer.CommandsRemaining == 0)
                FinishGpuLinkedListNode(channel, transfer);

            return true;
        }

        if (!IsRamDmaAddress(transfer.CommandAddress) ||
            transfer.TransferredWords >= MaximumTransferWords)
        {
            AbortGpuTransfer();
            return false;
        }

        if (!_gpu.TryWriteDmaWord(
                _ram.Read32(
                    transfer.CommandAddress & RamAddressMask)))
        {
            return false;
        }
        transfer.CommandAddress =
            (transfer.CommandAddress + 4) & DmaAddressMask;
        transfer.CommandsRemaining--;
        transfer.TransferredWords++;

        if (transfer.CommandsRemaining == 0)
            FinishGpuLinkedListNode(channel, transfer);

        return true;
    }

    private void ApplyGpuChopping(GpuTransferState transfer)
    {
        if (!transfer.ChoppingEnabled)
            return;

        if (transfer.WordsUntilChop > 0)
            transfer.WordsUntilChop--;
        if (transfer.WordsUntilChop != 0)
            return;

        ChannelState channel = _channels[GpuChannel];
        channel.BaseAddress = transfer.CurrentAddress & 0x00FF_FFFF;
        channel.BlockControl =
            (channel.BlockControl & 0xFFFF_0000) |
            (transfer.WordsInBlockRemaining & 0xFFFF);

        transfer.WordsUntilChop = transfer.DmaWindowWords;
        transfer.CpuWindowCyclesRemaining = transfer.CpuWindowCycles;
    }

    private void FinishGpuLinkedListNode(
        ChannelState channel,
        GpuTransferState transfer)
    {
        channel.BaseAddress = transfer.NextAddress;
        if ((transfer.NextAddress & 0x0080_0000) != 0)
        {
            CompleteChannel(GpuChannel);
            return;
        }

        transfer.CurrentAddress = transfer.NextAddress & DmaAddressMask;
        transfer.HeaderPending = true;
    }

    private bool GpuDmaRequestAsserted(bool fromRam)
    {
        return fromRam
            ? _gpu.DmaDirection == 2 && _gpu.CanReceiveDmaBlock
            : _gpu.DmaDirection == 3 && _gpu.CanSendVramToCpu;
    }

    private void AbortGpuTransfer()
    {
        SignalBusError();
        CompleteChannel(GpuChannel);
    }

    private void TransferOtc(
        ChannelState channel,
        uint synchronizationMode)
    {
        if (synchronizationMode != 0)
        {
            SignalBusError();
            return;
        }

        ulong wordCount = DecodeCount((ushort)channel.BlockControl);
        uint address = channel.BaseAddress & DmaAddressMask;

        for (ulong word = 0; word < wordCount; word++)
        {
            if (!IsRamDmaAddress(address))
            {
                SignalBusError();
                break;
            }

            bool isLastWord = word + 1 == wordCount;
            uint value = isLastWord
                ? 0x00FF_FFFF
                : (address - 4) & DmaAddressMask;
            _ram.Write32(address & RamAddressMask, value);
            address = (address - 4) & DmaAddressMask;
        }
    }

    private void CompleteChannel(int channel)
    {
        ChannelState state = _channels[channel];
        state.ChannelControl &= ~(BusyBit | TriggerBit);
        if (state.Active)
        {
            ulong duration = _clockCycles - state.StartCycle;
            state.LastTransferCycles = duration;
            state.LongestTransferCycles = Math.Max(
                state.LongestTransferCycles,
                duration);
            state.CompletedTransfers++;
            state.Active = false;
        }
        if (channel == GpuChannel)
        {
            _gpuTransfer = null;
            SetGpuWait(DmaGpuWaitReason.None, _clockCycles);
        }
        CompletedTransfers++;

        uint channelInterruptEnable = 1u << (16 + channel);
        if ((_interrupt & MasterInterruptEnableBit) != 0 &&
            (_interrupt & channelInterruptEnable) != 0)
        {
            bool wasMasterPending =
                (_interrupt & MasterInterruptFlagBit) != 0;
            _interrupt |= 1u << (24 + channel);
            RecomputeMasterInterrupt(wasMasterPending);
        }
    }

    private void SignalBusError()
    {
        bool wasMasterPending = (_interrupt & MasterInterruptFlagBit) != 0;
        _interrupt |= BusErrorBit;
        RecomputeMasterInterrupt(wasMasterPending);
    }

    private void RecomputeMasterInterrupt(bool wasMasterPending)
    {
        bool masterPending =
            (_interrupt & BusErrorBit) != 0 ||
            ((_interrupt & MasterInterruptEnableBit) != 0 &&
             (_interrupt & ChannelInterruptFlagsMask) != 0);

        if (masterPending)
            _interrupt |= MasterInterruptFlagBit;
        else
            _interrupt &= ~MasterInterruptFlagBit;

        if (!wasMasterPending && masterPending)
            _interruptController.Request(InterruptSource.Dma);
    }

    private bool IsChannelEnabled(int channel) =>
        (_control & (1u << (channel * 4 + 3))) != 0;

    private static ulong GetWordCount(
        uint blockControl,
        uint synchronizationMode)
    {
        ulong blockSize = DecodeCount((ushort)blockControl);
        if (synchronizationMode == 0)
            return blockSize;

        ulong blockCount = DecodeCount((ushort)(blockControl >> 16));
        return blockSize * blockCount;
    }

    private static uint DecodeCount(ushort value) =>
        value == 0 ? 0x1_0000u : value;

    private static bool IsRamDmaAddress(uint address) =>
        address < DmaAddressLimit;

    private static uint ComposeChannelControl(
        int channel,
        ChannelState state) =>
        channel == OtcChannel
            ? (state.ChannelControl & 0x5100_0000) | 2
            : state.ChannelControl;

    private static uint NormalizeChannelControl(
        int channel,
        uint value) =>
        channel == OtcChannel
            ? value & 0x5100_0000
            : value & 0x7177_0703;

    private static uint Merge(uint current, uint value, uint writeMask) =>
        (current & ~writeMask) | (value & writeMask);

    private static (int Channel, uint RegisterOffset) DecodeChannelAddress(
        uint registerAddress)
    {
        if (registerAddress < ChannelBaseAddress ||
            registerAddress >= ChannelBaseAddress + ChannelStride * 7)
        {
            throw UnknownRegister(registerAddress);
        }

        uint offset = registerAddress - ChannelBaseAddress;
        return ((int)(offset / ChannelStride), offset % ChannelStride);
    }

    private static string GetChannelRegisterName(uint registerAddress)
    {
        (int channel, uint registerOffset) =
            DecodeChannelAddress(registerAddress);
        string suffix = registerOffset switch
        {
            BaseAddressOffset => "MADR",
            BlockControlOffset => "BCR",
            ChannelControlOffset => "CHCR",
            _ => "UNKNOWN",
        };

        return $"D{channel}_{suffix}";
    }

    private ChannelState GetChannelState(int channel)
    {
        if ((uint)channel >= _channels.Length)
            throw new ArgumentOutOfRangeException(nameof(channel));

        return _channels[channel];
    }

    private void UpdateChannelActivity(
        ChannelState state,
        bool wasBusy,
        bool isBusy)
    {
        if (!wasBusy && isBusy)
        {
            state.Active = true;
            state.StartCycle = _clockCycles;
        }
        else if (wasBusy && !isBusy)
        {
            state.Active = false;
        }
    }

    private void SetGpuWait(
        DmaGpuWaitReason reason,
        ulong startCycle)
    {
        if (reason == _gpuWaitReason)
            return;

        _gpuWaitReason = reason;
        _gpuWaitStartCycle = reason == DmaGpuWaitReason.None
            ? _clockCycles
            : startCycle;
    }

    private static InvalidOperationException UnknownRegister(uint address) =>
        new($"Endereço 0x{address:X8} não pertence ao DMA.");

    private sealed class ChannelState
    {
        public uint BaseAddress;
        public uint BlockControl;
        public uint ChannelControl;
        public bool Active;
        public ulong StartCycle;
        public ulong LastTransferCycles;
        public ulong LongestTransferCycles;
        public ulong CompletedTransfers;

        public void Reset()
        {
            BaseAddress = 0;
            BlockControl = 0;
            ChannelControl = 0;
            Active = false;
            StartCycle = 0;
            LastTransferCycles = 0;
            LongestTransferCycles = 0;
            CompletedTransfers = 0;
        }
    }

    private sealed class GpuTransferState
    {
        public bool LinkedList;
        public bool FromRam;
        public bool Forced;
        public bool Halted;
        public bool ChoppingEnabled;
        public bool HeaderPending;
        public bool RequestAccepted;
        public uint SynchronizationMode;
        public uint StartAddress;
        public uint HeaderAddress;
        public uint CommandsInNode;
        public uint CurrentAddress;
        public uint AddressStep;
        public uint BlockSize;
        public uint BlocksRemaining;
        public uint WordsInBlockRemaining;
        public uint CommandAddress;
        public uint CommandsRemaining;
        public uint NextAddress;
        public uint DmaWindowWords;
        public uint CpuWindowCycles;
        public uint WordsUntilChop;
        public uint CpuWindowCyclesRemaining;
        public ulong TransferredWords;
        public HashSet<uint>? VisitedLinkedListAddresses;
    }
}

public readonly record struct DmaChannelSnapshot(
    uint BaseAddress,
    uint BlockControl,
    uint ChannelControl);

public readonly record struct DmaChannelRuntimeSnapshot(
    uint BaseAddress,
    uint BlockControl,
    uint ChannelControl,
    bool Busy,
    ulong ActiveCycles,
    ulong LastTransferCycles,
    ulong LongestTransferCycles,
    ulong CompletedTransfers);

public readonly record struct DmaGpuTransferSnapshot(
    bool Active,
    bool LinkedList,
    bool HeaderPending,
    uint StartAddress,
    uint HeaderAddress,
    uint CommandsInNode,
    uint CurrentAddress,
    uint CommandAddress,
    uint CommandsRemaining,
    uint NextAddress,
    ulong TransferredWords);

public enum DmaGpuWaitReason
{
    None,
    Request,
    CpuWindow,
    Paused,
    Halted,
}
