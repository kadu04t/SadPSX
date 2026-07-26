using SadPSX.Core.Bus;

namespace SadPSX.Core.Bios;

public sealed class PostStatusRegister : IMmioDevice
{
    public const uint Address = 0x1F80_2041;

    public byte Value { get; private set; }
    public ulong WriteCount { get; private set; }

    public event Action<byte>? ValueChanged;

    public void Reset()
    {
        Value = 0;
        WriteCount = 0;
    }

    public bool Handles(uint address) => address == Address;

    public byte Read8(uint address) => Value;

    public ushort Read16(uint address) => Value;

    public uint Read32(uint address) => Value;

    public void Write8(uint address, byte value)
    {
        Value = value;
        WriteCount++;
        ValueChanged?.Invoke(value);
    }

    public void Write16(uint address, ushort value) => Write8(address, (byte)value);

    public void Write32(uint address, uint value) => Write8(address, (byte)value);

    public string GetRegisterName(uint address) => "BIOS_POST";
}
