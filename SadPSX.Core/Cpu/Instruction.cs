namespace SadPSX.Core.Cpu;

public readonly record struct Instruction(uint Value)
{
    public uint Opcode => Value >> 26;

    public int Rs => (int)((Value >> 21) & 0x1F);
    public int Rt => (int)((Value >> 16) & 0x1F);
    public int Rd => (int)((Value >> 11) & 0x1F);

    public int ShiftAmount => (int)((Value >> 6) & 0x1F);
    public uint Function => Value & 0x3F;

    public ushort Immediate => (ushort)Value;

    public int SignedImmediate => (short)Immediate;

    public uint JumpTarget => Value & 0x03FF_FFFF;
}
