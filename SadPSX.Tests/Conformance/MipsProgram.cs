using SadPSX.Core.Cpu;
using SadPSX.Core.Memory;

namespace SadPSX.Tests.Conformance;

internal sealed class MipsProgram
{
    public MipsProgram(uint startAddress = 0)
    {
        Bus = new Bus();
        Cpu = new R3000A(Bus);
        Cpu.Reset(startAddress);
    }

    public Bus Bus { get; }

    public R3000A Cpu { get; }

    public void Load(uint address, params uint[] instructions)
    {
        for (int index = 0; index < instructions.Length; index++)
            Bus.Write32(address + (uint)(index * 4), instructions[index]);
    }

    public void Run(ulong instructionCount)
    {
        for (ulong index = 0; index < instructionCount; index++)
            Cpu.Step();
    }

    public static uint IType(
        uint opcode,
        int source,
        int target,
        short immediate)
    {
        return (opcode << 26) |
               ((uint)source << 21) |
               ((uint)target << 16) |
               unchecked((ushort)immediate);
    }

    public static uint RType(
        int source,
        int target,
        int destination,
        uint function)
    {
        return ((uint)source << 21) |
               ((uint)target << 16) |
               ((uint)destination << 11) |
               function;
    }
}
