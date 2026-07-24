using SadPSX.Core.Memory;

namespace SadPSX.Core.Cpu;

public sealed class R3000A
{
    private const int RegisterCount = 32;

    private readonly uint[] _registers = new uint[RegisterCount];
    private readonly Bus _bus;

    public uint Pc { get; private set; } = 0xBFC0_0000;
    public uint NextPc { get; private set; } = 0xBFC0_0004;

    public uint Hi { get; private set; }
    public uint Lo { get; private set; }

    public R3000A(Bus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    // Construtor de conveniência: cria um Bus + Ram próprios.
    // Útil para testes isolados da CPU que não precisam compartilhar
    // memória com o resto da máquina.
    public R3000A() : this(new Bus())
    {
    }

    public uint GetRegister(int index)
    {
        ValidateRegisterIndex(index);
        return _registers[index];
    }

    private void SetRegister(int index, uint value)
    {
        ValidateRegisterIndex(index);

        // O registrador $zero é permanentemente zero.
        if (index == 0)
            return;

        _registers[index] = value;
    }

    private static void ValidateRegisterIndex(int index)
    {
        if ((uint)index >= RegisterCount)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void Execute(Instruction instruction)
    {
        switch (instruction.Opcode)
        {
            case 0x00:
                ExecuteSpecial(instruction);
                break;

            case 0x09: // ADDIU rt, rs, imm
            {
                uint result = unchecked(
                    GetRegister(instruction.Rs) +
                    (uint)instruction.SignedImmediate);

                SetRegister(instruction.Rt, result);
                break;
            }

            case 0x0D: // ORI rt, rs, imm
                SetRegister(
                    instruction.Rt,
                    GetRegister(instruction.Rs) | instruction.Immediate);
                break;

            case 0x0C: // ANDI rt, rs, imm
                SetRegister(
                    instruction.Rt,
                    GetRegister(instruction.Rs) & instruction.Immediate);
                break;

            case 0x0E: // XORI rt, rs, imm
                SetRegister(
                    instruction.Rt,
                    GetRegister(instruction.Rs) ^ instruction.Immediate);
                break;

            case 0x0A: // SLTI rt, rs, imm
            {
                bool lessThan = (int)GetRegister(instruction.Rs) < instruction.SignedImmediate;
                SetRegister(instruction.Rt, lessThan ? 1u : 0u);
                break;
            }

            case 0x0B: // SLTIU rt, rs, imm
            {
                // O imediato é sign-extended primeiro e só depois comparado
                // como unsigned — assim como no hardware real.
                uint immediateAsUnsigned = unchecked((uint)instruction.SignedImmediate);
                bool lessThan = GetRegister(instruction.Rs) < immediateAsUnsigned;
                SetRegister(instruction.Rt, lessThan ? 1u : 0u);
                break;
            }

            case 0x0F: // LUI rt, imm
                SetRegister(
                    instruction.Rt,
                    (uint)instruction.Immediate << 16);
                break;

            case 0x20: // LB rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                sbyte value = (sbyte)_bus.Read8(address);
                SetRegister(instruction.Rt, unchecked((uint)(int)value));
                break;
            }

            case 0x24: // LBU rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                byte value = _bus.Read8(address);
                SetRegister(instruction.Rt, value);
                break;
            }

            case 0x21: // LH rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                short value = (short)_bus.Read16(address);
                SetRegister(instruction.Rt, unchecked((uint)(int)value));
                break;
            }

            case 0x25: // LHU rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                ushort value = _bus.Read16(address);
                SetRegister(instruction.Rt, value);
                break;
            }

            case 0x23: // LW rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                uint value = _bus.Read32(address);
                SetRegister(instruction.Rt, value);
                break;
            }

            case 0x28: // SB rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                _bus.Write8(address, (byte)GetRegister(instruction.Rt));
                break;
            }

            case 0x29: // SH rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                _bus.Write16(address, (ushort)GetRegister(instruction.Rt));
                break;
            }

            case 0x2B: // SW rt, imm(rs)
            {
                uint address = EffectiveAddress(instruction);
                _bus.Write32(address, GetRegister(instruction.Rt));
                break;
            }

            default:
                throw new NotImplementedException(
                    $"Opcode 0x{instruction.Opcode:X2} não implementado.");
        }
    }

    // Endereço efetivo de load/store: base (rs) + deslocamento com sinal.
    private uint EffectiveAddress(Instruction instruction) =>
        unchecked(GetRegister(instruction.Rs) + (uint)instruction.SignedImmediate);

    private void ExecuteSpecial(Instruction instruction)
    {
        switch (instruction.Function)
        {
            case 0x00: // SLL rd, rt, sa
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) << instruction.ShiftAmount);
                break;

            case 0x02: // SRL rd, rt, sa (shift lógico: preenche com zero)
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) >> instruction.ShiftAmount);
                break;

            case 0x03: // SRA rd, rt, sa (shift aritmético: preserva o sinal)
                SetRegister(
                    instruction.Rd,
                    unchecked((uint)((int)GetRegister(instruction.Rt) >> instruction.ShiftAmount)));
                break;

            case 0x04: // SLLV rd, rt, rs (quantidade de shift vem dos 5 bits baixos de rs)
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) << (int)(GetRegister(instruction.Rs) & 0x1F));
                break;

            case 0x06: // SRLV rd, rt, rs
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) >> (int)(GetRegister(instruction.Rs) & 0x1F));
                break;

            case 0x07: // SRAV rd, rt, rs
                SetRegister(
                    instruction.Rd,
                    unchecked((uint)((int)GetRegister(instruction.Rt) >> (int)(GetRegister(instruction.Rs) & 0x1F))));
                break;

            case 0x21: // ADDU rd, rs, rt
                SetRegister(
                    instruction.Rd,
                    unchecked(
                        GetRegister(instruction.Rs) +
                        GetRegister(instruction.Rt)));
                break;

            case 0x23: // SUBU rd, rs, rt
                SetRegister(
                    instruction.Rd,
                    unchecked(
                        GetRegister(instruction.Rs) -
                        GetRegister(instruction.Rt)));
                break;

            case 0x24: // AND rd, rs, rt
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rs) &
                    GetRegister(instruction.Rt));
                break;

            case 0x25: // OR rd, rs, rt
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rs) |
                    GetRegister(instruction.Rt));
                break;

            case 0x26: // XOR rd, rs, rt
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rs) ^
                    GetRegister(instruction.Rt));
                break;

            case 0x27: // NOR rd, rs, rt
                SetRegister(
                    instruction.Rd,
                    ~(GetRegister(instruction.Rs) | GetRegister(instruction.Rt)));
                break;

            case 0x2A: // SLT rd, rs, rt (comparação COM sinal)
            {
                bool lessThan = (int)GetRegister(instruction.Rs) < (int)GetRegister(instruction.Rt);
                SetRegister(instruction.Rd, lessThan ? 1u : 0u);
                break;
            }

            case 0x2B: // SLTU rd, rs, rt (comparação SEM sinal)
            {
                bool lessThan = GetRegister(instruction.Rs) < GetRegister(instruction.Rt);
                SetRegister(instruction.Rd, lessThan ? 1u : 0u);
                break;
            }

            default:
                throw new NotImplementedException(
                    $"Função SPECIAL 0x{instruction.Function:X2} não implementada.");
        }
    }
}