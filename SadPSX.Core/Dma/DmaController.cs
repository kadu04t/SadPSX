using SadPSX.Core.Bus;
using SadPSX.Core.CdRom;
using SadPSX.Core.Interrupts;
using SadPSX.Core.Memory;
using GpuDevice = SadPSX.Core.Gpu.Gpu;
using SpuDevice = SadPSX.Core.Spu.Spu;

namespace SadPSX.Core.Dma;

public sealed class DmaController : IMmioDevice
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
    private const int GpuChannel = 2;
    private const int CdRomChannel = 3;
    private const int SpuChannel = 4;
    private const int OtcChannel = 6;

    private readonly InterruptController _interruptController;
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

    public DmaController(
        InterruptController interruptController,
        GpuDevice gpu,
        CdRomController cdRom,
        SpuDevice spu,
        Ram? ram = null)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        _gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
        _cdRom = cdRom ?? throw new ArgumentNullException(nameof(cdRom));
        _spu = spu ?? throw new ArgumentNullException(nameof(spu));
        _ram = ram ?? new Ram();
        Reset();
    }

    public uint Control => _control;

    public uint Interrupt => _interrupt;

    public ulong CompletedTransfers { get; private set; }

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

    public void Reset()
    {
        foreach (ChannelState channel in _channels)
            channel.Reset();

        _control = ResetControl;
        _interrupt = 0;
        CompletedTransfers = 0;
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
                TryStartChannel(channel);
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
        for (int channel = 0; channel < _channels.Length; channel++)
            TryStartChannel(channel);
    }

    private void TryStartChannel(int channel)
    {
        ChannelState state = _channels[channel];
        uint channelControl = ComposeChannelControl(channel, state);

        if ((channelControl & BusyBit) == 0 || !IsChannelEnabled(channel))
            return;

        uint synchronizationMode = (channelControl >> 9) & 3;
        if (synchronizationMode == 0 &&
            (channelControl & TriggerBit) == 0)
        {
            return;
        }

        switch (channel)
        {
            case GpuChannel:
                TransferGpu(state, channelControl, synchronizationMode);
                CompleteChannel(channel);
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
        }
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

    private void TransferGpu(
        ChannelState channel,
        uint channelControl,
        uint synchronizationMode)
    {
        bool fromRam = (channelControl & 1) != 0;
        bool decrement = (channelControl & 2) != 0;

        if (synchronizationMode == 2)
        {
            if (!fromRam)
            {
                SignalBusError();
                return;
            }

            TransferGpuLinkedList(channel);
            return;
        }

        ulong wordCount = GetWordCount(channel.BlockControl, synchronizationMode);
        if (wordCount > MaximumTransferWords)
        {
            SignalBusError();
            return;
        }

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
            {
                _gpu.Write32(
                    GpuDevice.Gp0Address,
                    _ram.Read32(address & RamAddressMask));
            }
            else
            {
                _ram.Write32(
                    address & RamAddressMask,
                    _gpu.Read32(GpuDevice.Gp0Address));
            }

            address = (address + step) & DmaAddressMask;
        }

        if (synchronizationMode == 1)
        {
            channel.BaseAddress = address & 0x00FF_FFFF;
            channel.BlockControl &= 0x0000_FFFF;
        }
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

    private void TransferGpuLinkedList(ChannelState channel)
    {
        uint nodeAddress = channel.BaseAddress & DmaAddressMask;
        ulong transferredWords = 0;

        while (true)
        {
            if (!IsRamDmaAddress(nodeAddress) ||
                transferredWords >= MaximumTransferWords)
            {
                SignalBusError();
                break;
            }

            uint header = _ram.Read32(nodeAddress & RamAddressMask);
            uint commandCount = header >> 24;
            uint commandAddress = (nodeAddress + 4) & DmaAddressMask;

            for (uint command = 0; command < commandCount; command++)
            {
                if (!IsRamDmaAddress(commandAddress))
                {
                    SignalBusError();
                    return;
                }

                _gpu.Write32(
                    GpuDevice.Gp0Address,
                    _ram.Read32(commandAddress & RamAddressMask));
                commandAddress = (commandAddress + 4) & DmaAddressMask;
                transferredWords++;
            }

            uint nextAddress = header & 0x00FF_FFFF;
            channel.BaseAddress = nextAddress;
            transferredWords++;

            if ((nextAddress & 0x0080_0000) != 0)
                break;

            nodeAddress = nextAddress & DmaAddressMask;
        }
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

    private static InvalidOperationException UnknownRegister(uint address) =>
        new($"Endereço 0x{address:X8} não pertence ao DMA.");

    private sealed class ChannelState
    {
        public uint BaseAddress;
        public uint BlockControl;
        public uint ChannelControl;

        public void Reset()
        {
            BaseAddress = 0;
            BlockControl = 0;
            ChannelControl = 0;
        }
    }
}

public readonly record struct DmaChannelSnapshot(
    uint BaseAddress,
    uint BlockControl,
    uint ChannelControl);
