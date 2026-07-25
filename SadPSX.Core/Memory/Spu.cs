namespace SadPSX.Core.Memory;

public sealed class Spu : IClockedDevice, IMmioDevice
{
    public const uint BaseAddress = 0x1F80_1C00;
    public const uint EndAddress = 0x1F80_1DFF;
    public const uint TransferAddressRegister = 0x1F80_1DA6;
    public const uint TransferFifoRegister = 0x1F80_1DA8;
    public const uint ControlRegister = 0x1F80_1DAA;
    public const uint TransferControlRegister = 0x1F80_1DAC;
    public const uint StatusRegister = 0x1F80_1DAE;

    private const int RegisterCount = 0x100;
    private const int SoundRamSize = 512 * 1024;
    private const uint ControlApplyDelayCycles = 2;

    private readonly ushort[] _registers = new ushort[RegisterCount];
    private readonly byte[] _soundRam = new byte[SoundRamSize];
    private readonly Queue<ushort> _transferFifo = new(32);

    private uint _currentTransferAddress;
    private uint _controlApplyCycles;
    private ushort _pendingStatusMode;

    public ushort Control => ReadRawRegister(ControlRegister);

    public ushort Status => ReadRawRegister(StatusRegister);

    public byte[] SoundRam => _soundRam;

    public void Reset()
    {
        Array.Clear(_registers);
        Array.Clear(_soundRam);
        _transferFifo.Clear();
        _currentTransferAddress = 0;
        _controlApplyCycles = 0;
        _pendingStatusMode = 0;
    }

    public void Tick(uint cycles)
    {
        if (_controlApplyCycles == 0 || cycles == 0)
            return;

        if (cycles < _controlApplyCycles)
        {
            _controlApplyCycles -= cycles;
            return;
        }

        _controlApplyCycles = 0;
        ApplyControlToStatus();
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
                _pendingStatusMode = (ushort)(value & 0x003F);
                _controlApplyCycles = ControlApplyDelayCycles;
                break;

            default:
                WriteRawRegister(address, value);
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
            TransferAddressRegister => "SPU_TRANSFER_ADDR",
            TransferFifoRegister => "SPU_TRANSFER_FIFO",
            ControlRegister => "SPUCNT",
            TransferControlRegister => "SPU_TRANSFER_CTRL",
            StatusRegister => "SPUSTAT",
            _ when registerAddress < 0x1F80_1D80 => "SPU_VOICE",
            _ => "SPU_REG",
        };
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
        {
            int address = (int)_currentTransferAddress;
            _soundRam[address] = (byte)value;
            _soundRam[(address + 1) % SoundRamSize] = (byte)(value >> 8);
            _currentTransferAddress =
                (_currentTransferAddress + 2) % SoundRamSize;
        }
    }

    private ushort ReadRawRegister(uint address)
    {
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
}
