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

    private readonly InterruptController _interruptController;
    private readonly Queue<byte> _receiveFifo = new(8);

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

    public Sio0(InterruptController interruptController)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        ControllerPort1 = new DigitalController();
        Reset();
    }

    public DigitalController ControllerPort1 { get; }
    public ushort Mode => _mode;
    public ushort Control => _control;
    public ushort Baud => _baud;
    public int ReceiveCount => _receiveFifo.Count;
    public bool TransferPending => _transferPending;
    public bool InterruptRequest => _interruptRequest;
    public bool DsrAsserted => _acknowledgeCyclesRemaining > 0;

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
        ControllerPort1.ResetTransfer();
        ControllerPort1.ReleaseAll();
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
                if (_acknowledgeCyclesRemaining == 0)
                    TryStartQueuedTransfer();
                continue;
            }

            if (!TryStartQueuedTransfer())
                break;
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
            DataAddress => (ushort)ReadReceivePreview(2, dequeue: true),
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
            DataAddress => ReadReceivePreview(4, dequeue: true),
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
            DataAddress => ReadReceivePreview(4, dequeue: false),
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

        if ((value & ResetBit) != 0)
        {
            Reset();
            return;
        }

        if ((value & Acknowledge) != 0)
            _interruptRequest = false;

        _control = (ushort)(value & 0x3F0F);
        bool dtrIsSet = (_control & Dtr) != 0;
        if (dtrWasSet && !dtrIsSet)
        {
            ControllerPort1.ResetTransfer();
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

        BeginTransfer(value);
    }

    private void BeginTransfer(byte value)
    {
        _pendingResult = (_control & PortSelect) == 0
            ? ControllerPort1.Transfer(value)
            : new ControllerTransferResult(0xFF, false);
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
        BeginTransfer(value);
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
        if (_receiveFifo.Count < 8)
            _receiveFifo.Enqueue(_pendingResult.Data);

        if (_pendingResult.Acknowledge)
            _acknowledgeDelayRemaining = AcknowledgeDelayCycles;
        else
            TryStartQueuedTransfer();

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

    private uint ReadReceivePreview(int byteCount, bool dequeue)
    {
        byte[] values = _receiveFifo.Take(byteCount).ToArray();
        uint result = 0;
        for (int index = 0; index < values.Length; index++)
            result |= (uint)values[index] << (index * 8);

        if (dequeue && _receiveFifo.Count > 0)
            _receiveFifo.Dequeue();

        return result;
    }
}
