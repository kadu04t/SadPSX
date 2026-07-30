using SadPSX.Core.Bus;
using SadPSX.Core.Interrupts;

namespace SadPSX.Core.Controllers;

public sealed class Sio0 : IMmioDevice, IClockedDevice
{
    public const uint DataAddress = 0x1F80_1040;
    public const uint StatusAddress = 0x1F80_1044;
    public const uint ModeAddress = 0x1F80_1048;
    public const uint ControlAddress = 0x1F80_104A;
    public const uint BaudAddress = 0x1F80_104E;

    private const ushort TxEnable = 1 << 0;
    private const ushort Dtr = 1 << 1;
    private const ushort Acknowledge = 1 << 4;
    private const ushort ResetBit = 1 << 6;
    private const ushort TxInterruptEnable = 1 << 10;
    private const ushort RxInterruptEnable = 1 << 11;
    private const ushort DsrInterruptEnable = 1 << 12;
    private const ushort PortSelect = 1 << 13;
    private const uint AcknowledgeDelayCycles = 100;
    private const uint AcknowledgePulseCycles = 100;
    private const int MaximumTransferHistory = 512;

    private readonly InterruptController _interruptController;
    private readonly Queue<byte> _receiveFifo = new(8);
    private readonly Queue<Sio0TransferTrace> _transferHistory =
        new(MaximumTransferHistory);

    private ushort _mode;
    private ushort _control;
    private ushort _baud;
    private bool _interruptRequest;
    private bool _transferPending;
    private uint _transferCyclesRemaining;
    private ControllerTransferResult _pendingResult;
    private bool _transmitQueued;
    private byte _queuedTransmitByte;
    private uint _acknowledgeDelayRemaining;
    private uint _acknowledgeCyclesRemaining;
    private ISioPeripheral? _activePeripheral;
    private Sio0PeripheralKind _activePeripheralKind;
    private ulong _clockCycles;
    private ulong _transferSequence;
    private ulong _transactionSequence;
    private ulong _activeTransaction;
    private int _transactionByteIndex;
    private byte _pendingTransmitByte;
    private int _pendingPort;
    private Sio0PeripheralKind _pendingPeripheralKind;
    private bool _pendingPeripheralConnected;
    private bool _pendingWasQueued;
    private ulong _pendingTransferStartCycle;
    private ulong _pendingTransaction;
    private int _pendingTransactionByteIndex;
    private ushort _pendingControl;
    private ushort _pendingMode;
    private ushort _pendingBaud;

    public Sio0(InterruptController interruptController)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        ControllerPort1 = new AnalogController();
        MemoryCardPort1 = MemoryCard.CreateFormatted();
        Reset();
    }

    public IController ControllerPort1 { get; private set; }
    public IController? ControllerPort2 { get; private set; }
    public MemoryCard? MemoryCardPort1 { get; private set; }
    public MemoryCard? MemoryCardPort2 { get; private set; }
    public ushort Mode => _mode;
    public ushort Control => _control;
    public ushort Baud => _baud;
    public int ReceiveCount => _receiveFifo.Count;
    public bool TransferPending => _transferPending;
    public bool InterruptRequest => _interruptRequest;
    public bool DsrAsserted => _acknowledgeCyclesRemaining > 0;
    public ulong ClockCycles => _clockCycles;
    public IReadOnlyCollection<Sio0TransferTrace> TransferHistory =>
        _transferHistory;

    public void Reset()
    {
        _receiveFifo.Clear();
        _mode = 0;
        _control = 0;
        _baud = 0;
        _interruptRequest = false;
        _transferPending = false;
        _transferCyclesRemaining = 0;
        _pendingResult = default;
        _transmitQueued = false;
        _queuedTransmitByte = 0;
        _acknowledgeDelayRemaining = 0;
        _acknowledgeCyclesRemaining = 0;
        _activePeripheral = null;
        _activePeripheralKind = Sio0PeripheralKind.Unknown;
        _activeTransaction = 0;
        _transactionByteIndex = 0;
        _pendingTransmitByte = 0;
        _pendingPort = 1;
        _pendingPeripheralKind = Sio0PeripheralKind.Unknown;
        _pendingPeripheralConnected = false;
        _pendingWasQueued = false;
        _pendingTransferStartCycle = 0;
        _pendingTransaction = 0;
        _pendingTransactionByteIndex = 0;
        _pendingControl = 0;
        _pendingMode = 0;
        _pendingBaud = 0;
        ResetPeripheralTransfers();
        ControllerPort1.ReleaseAll();
        ControllerPort2?.ReleaseAll();
    }

    public void ClearTransferHistory()
    {
        _transferHistory.Clear();
    }

    public void AttachController(int port, IController? controller)
    {
        ValidatePort(port);
        _activePeripheral?.ResetTransfer();
        _activePeripheral = null;
        _activePeripheralKind = Sio0PeripheralKind.Unknown;

        if (port == 1)
        {
            ControllerPort1 = controller ??
                throw new ArgumentNullException(nameof(controller));
        }
        else
            ControllerPort2 = controller;
    }

    public void AttachMemoryCard(int port, MemoryCard? memoryCard)
    {
        ValidatePort(port);
        _activePeripheral?.ResetTransfer();
        _activePeripheral = null;
        _activePeripheralKind = Sio0PeripheralKind.Unknown;

        if (port == 1)
            MemoryCardPort1 = memoryCard;
        else
            MemoryCardPort2 = memoryCard;
    }

    public void Tick(uint cycles)
    {
        uint cyclesRemaining = cycles;
        while (cyclesRemaining > 0)
        {
            if (_transferPending)
            {
                uint elapsed = Math.Min(
                    cyclesRemaining,
                    _transferCyclesRemaining);
                cyclesRemaining -= elapsed;
                _transferCyclesRemaining -= elapsed;
                _clockCycles += elapsed;
                if (_transferCyclesRemaining == 0)
                {
                    _transferPending = false;
                    CompleteTransfer();
                }
                continue;
            }

            if (_acknowledgeDelayRemaining > 0)
            {
                uint elapsed = Math.Min(
                    cyclesRemaining,
                    _acknowledgeDelayRemaining);
                cyclesRemaining -= elapsed;
                _acknowledgeDelayRemaining -= elapsed;
                _clockCycles += elapsed;
                if (_acknowledgeDelayRemaining == 0)
                    BeginAcknowledgePulse();
                continue;
            }

            if (_acknowledgeCyclesRemaining > 0)
            {
                uint elapsed = Math.Min(
                    cyclesRemaining,
                    _acknowledgeCyclesRemaining);
                cyclesRemaining -= elapsed;
                _acknowledgeCyclesRemaining -= elapsed;
                _clockCycles += elapsed;
                if (_acknowledgeCyclesRemaining == 0)
                    TryStartQueuedTransfer();
                continue;
            }

            if (!TryStartQueuedTransfer())
            {
                _clockCycles += cyclesRemaining;
                break;
            }
        }
    }

    public bool Handles(uint address) =>
        address is >= DataAddress and <= BaudAddress;

    public byte Read8(uint address)
    {
        return address switch
        {
            DataAddress => ReadReceiveByte(),
            >= StatusAddress and <= StatusAddress + 3 =>
                (byte)(ReadStatus() >> (int)((address - StatusAddress) * 8)),
            ModeAddress => (byte)_mode,
            ModeAddress + 1 => (byte)(_mode >> 8),
            ControlAddress => (byte)_control,
            ControlAddress + 1 => (byte)(_control >> 8),
            BaudAddress => (byte)_baud,
            BaudAddress + 1 => (byte)(_baud >> 8),
            _ => 0,
        };
    }

    public ushort Read16(uint address)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Leitura SIO0 de 16 bits desalinhada: 0x{address:X8}.");

        return address switch
        {
            DataAddress => (ushort)ReadReceivePreview(2, dequeueCount: 1),
            StatusAddress => (ushort)ReadStatus(),
            StatusAddress + 2 => (ushort)(ReadStatus() >> 16),
            ModeAddress => _mode,
            ControlAddress => _control,
            BaudAddress => _baud,
            _ => 0,
        };
    }

    public uint Read32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura SIO0 de 32 bits desalinhada: 0x{address:X8}.");

        return address switch
        {
            DataAddress => ReadReceivePreview(4, dequeueCount: 4),
            StatusAddress => ReadStatus(),
            ModeAddress => _mode | ((uint)_control << 16),
            _ => 0,
        };
    }

    public uint Peek32(uint address)
    {
        if ((address & 3) != 0)
            return 0;

        return address switch
        {
            DataAddress => ReadReceivePreview(4, dequeueCount: 0),
            StatusAddress => ReadStatus(),
            ModeAddress => _mode | ((uint)_control << 16),
            _ => 0,
        };
    }

    public void Write8(uint address, byte value)
    {
        switch (address)
        {
            case DataAddress:
                StartTransfer(value);
                break;

            case ModeAddress:
                WriteMode((ushort)((_mode & 0xFF00) | value));
                break;

            case ModeAddress + 1:
                WriteMode((ushort)((_mode & 0x00FF) | (value << 8)));
                break;

            case ControlAddress:
                WriteControl((ushort)((_control & 0xFF00) | value));
                break;

            case ControlAddress + 1:
                WriteControl((ushort)((_control & 0x00FF) | (value << 8)));
                break;

            case BaudAddress:
                _baud = (ushort)((_baud & 0xFF00) | value);
                break;

            case BaudAddress + 1:
                _baud = (ushort)((_baud & 0x00FF) | (value << 8));
                break;
        }
    }

    public void Write16(uint address, ushort value)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Escrita SIO0 de 16 bits desalinhada: 0x{address:X8}.");

        switch (address)
        {
            case DataAddress:
                StartTransfer((byte)value);
                break;
            case ModeAddress:
                WriteMode(value);
                break;
            case ControlAddress:
                WriteControl(value);
                break;
            case BaudAddress:
                _baud = value;
                break;
        }
    }

    public void Write32(uint address, uint value)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Escrita SIO0 de 32 bits desalinhada: 0x{address:X8}.");

        switch (address)
        {
            case DataAddress:
                StartTransfer((byte)value);
                break;
            case ModeAddress:
                WriteMode((ushort)value);
                WriteControl((ushort)(value >> 16));
                break;
        }
    }

    public string GetRegisterName(uint address)
    {
        return address switch
        {
            >= DataAddress and <= DataAddress + 3 => "SIO0_DATA",
            >= StatusAddress and <= StatusAddress + 3 => "SIO0_STAT",
            ModeAddress or ModeAddress + 1 => "SIO0_MODE",
            ControlAddress or ControlAddress + 1 => "SIO0_CTRL",
            BaudAddress or BaudAddress + 1 => "SIO0_BAUD",
            _ => "SIO0_UNKNOWN",
        };
    }

    private uint ReadStatus()
    {
        uint status = 0;
        if (!_transmitQueued)
            status |= 1u << 0;
        if (_receiveFifo.Count > 0)
            status |= 1u << 1;
        if (!_transferPending && !_transmitQueued)
            status |= 1u << 2;
        if (DsrAsserted)
            status |= 1u << 7;
        if (_interruptRequest)
            status |= 1u << 9;
        return status;
    }

    private void WriteMode(ushort value)
    {
        _mode = (ushort)(value & 0x013F);
    }

    private void WriteControl(ushort value)
    {
        bool dtrWasSet = (_control & Dtr) != 0;
        bool portTwoWasSelected = (_control & PortSelect) != 0;

        if ((value & ResetBit) != 0)
        {
            Reset();
            return;
        }

        if ((value & Acknowledge) != 0)
            _interruptRequest = false;

        _control = (ushort)(value & 0x3F0F);
        bool dtrIsSet = (_control & Dtr) != 0;
        bool portChanged =
            portTwoWasSelected != ((_control & PortSelect) != 0);
        if ((dtrWasSet && !dtrIsSet) || portChanged)
        {
            ResetPeripheralTransfers();
            _activePeripheral = null;
            _activePeripheralKind = Sio0PeripheralKind.Unknown;
            _activeTransaction = 0;
            _transactionByteIndex = 0;
            _transferPending = false;
            _transferCyclesRemaining = 0;
            _transmitQueued = false;
            _acknowledgeDelayRemaining = 0;
            _acknowledgeCyclesRemaining = 0;
        }
        else
            TryStartQueuedTransfer();
    }

    private void StartTransfer(byte value)
    {
        if (_transferPending ||
            _acknowledgeDelayRemaining > 0 ||
            _acknowledgeCyclesRemaining > 0 ||
            (_control & (TxEnable | Dtr)) != (TxEnable | Dtr))
        {
            _queuedTransmitByte = value;
            _transmitQueued = true;
            return;
        }

        BeginTransfer(value, queued: false);
    }

    private void BeginTransfer(byte value, bool queued)
    {
        if (_activePeripheral is null)
        {
            _activePeripheral = ResolvePeripheral(value);
            _activePeripheralKind = ResolvePeripheralKind(value);
            _activeTransaction = ++_transactionSequence;
            _transactionByteIndex = 0;
        }

        _pendingResult = _activePeripheral?.Transfer(value) ??
            new ControllerTransferResult(0xFF, false);
        _pendingTransmitByte = value;
        _pendingPort = (_control & PortSelect) == 0 ? 1 : 2;
        _pendingPeripheralKind = _activePeripheralKind;
        _pendingPeripheralConnected = _activePeripheral is not null;
        _pendingWasQueued = queued;
        _pendingTransferStartCycle = _clockCycles;
        _pendingTransaction = _activeTransaction;
        _pendingTransactionByteIndex = _transactionByteIndex++;
        _pendingControl = _control;
        _pendingMode = _mode;
        _pendingBaud = _baud;
        _transferPending = true;
        _transferCyclesRemaining = CalculateTransferCycles();
    }

    private bool TryStartQueuedTransfer()
    {
        if (!_transmitQueued ||
            _transferPending ||
            _acknowledgeDelayRemaining > 0 ||
            _acknowledgeCyclesRemaining > 0 ||
            (_control & (TxEnable | Dtr)) != (TxEnable | Dtr))
        {
            return false;
        }

        byte value = _queuedTransmitByte;
        _transmitQueued = false;
        BeginTransfer(value, queued: true);
        return true;
    }

    private uint CalculateTransferCycles()
    {
        uint reload = _baud == 0 ? 1u : _baud;
        uint factor = (_mode & 3) switch
        {
            2 => 16u,
            3 => 64u,
            _ => 1u,
        };
        return Math.Max(1u, reload * factor * 8);
    }

    private void CompleteTransfer()
    {
        RecordTransfer();

        if (_receiveFifo.Count < 8)
            _receiveFifo.Enqueue(_pendingResult.Data);

        if (_pendingResult.Acknowledge)
            _acknowledgeDelayRemaining = AcknowledgeDelayCycles;
        else
        {
            _activePeripheral = null;
            _activePeripheralKind = Sio0PeripheralKind.Unknown;
            _activeTransaction = 0;
            _transactionByteIndex = 0;
            TryStartQueuedTransfer();
        }

        bool rxInterrupt = (_control & RxInterruptEnable) != 0 &&
                           _receiveFifo.Count >= ReceiveInterruptThreshold();
        bool txInterrupt = (_control & TxInterruptEnable) != 0;
        if (rxInterrupt || txInterrupt)
        {
            _interruptRequest = true;
            _interruptController.Request(InterruptSource.Controller);
        }
    }

    private void BeginAcknowledgePulse()
    {
        _acknowledgeCyclesRemaining = AcknowledgePulseCycles;
        if ((_control & DsrInterruptEnable) == 0)
            return;

        _interruptRequest = true;
        _interruptController.Request(InterruptSource.Controller);
    }

    private int ReceiveInterruptThreshold() =>
        ((_control >> 8) & 3) switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            _ => 8,
        };

    private byte ReadReceiveByte()
    {
        return _receiveFifo.TryDequeue(out byte value) ? value : (byte)0xFF;
    }

    private uint ReadReceivePreview(int byteCount, int dequeueCount)
    {
        byte[] values = _receiveFifo.Take(byteCount).ToArray();
        uint result = 0;
        for (int index = 0; index < values.Length; index++)
            result |= (uint)values[index] << (index * 8);

        int bytesToDequeue = Math.Min(dequeueCount, _receiveFifo.Count);
        for (int index = 0; index < bytesToDequeue; index++)
            _receiveFifo.Dequeue();

        return result;
    }

    private ISioPeripheral? ResolvePeripheral(byte address)
    {
        bool portTwo = (_control & PortSelect) != 0;
        return address switch
        {
            0x01 => portTwo ? ControllerPort2 : ControllerPort1,
            0x81 => portTwo ? MemoryCardPort2 : MemoryCardPort1,
            _ => null,
        };
    }

    private static Sio0PeripheralKind ResolvePeripheralKind(byte address) =>
        address switch
        {
            0x01 => Sio0PeripheralKind.Controller,
            0x81 => Sio0PeripheralKind.MemoryCard,
            _ => Sio0PeripheralKind.Unknown,
        };

    private void RecordTransfer()
    {
        if (_transferHistory.Count == MaximumTransferHistory)
            _transferHistory.Dequeue();

        _transferHistory.Enqueue(new Sio0TransferTrace(
            ++_transferSequence,
            _pendingTransaction,
            _pendingTransactionByteIndex,
            _pendingTransferStartCycle,
            _clockCycles,
            _pendingResult.Acknowledge
                ? _clockCycles + AcknowledgeDelayCycles
                : null,
            _pendingPort,
            _pendingPeripheralKind,
            _pendingPeripheralConnected,
            _pendingWasQueued,
            _pendingTransmitByte,
            _pendingResult.Data,
            _pendingResult.Acknowledge,
            _pendingControl,
            _pendingMode,
            _pendingBaud));
    }

    private void ResetPeripheralTransfers()
    {
        ControllerPort1.ResetTransfer();
        ControllerPort2?.ResetTransfer();
        MemoryCardPort1?.ResetTransfer();
        MemoryCardPort2?.ResetTransfer();
    }

    private static void ValidatePort(int port)
    {
        if (port is not 1 and not 2)
            throw new ArgumentOutOfRangeException(nameof(port));
    }
}
