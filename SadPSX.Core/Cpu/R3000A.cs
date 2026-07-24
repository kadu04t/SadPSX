using SadPSX.Core.Memory;

namespace SadPSX.Core.Cpu;

/// <summary>
/// Implementação interpretada da CPU MIPS R3000A utilizada pelo PlayStation 1.
/// </summary>
public sealed class R3000A
{
    public const uint ResetVector = 0xBFC0_0000;

    private const int RegisterCount = 32;

    private readonly uint[] _registers = new uint[RegisterCount];
    private readonly Bus _bus;

    /// <summary>
    /// Endereço da próxima instrução que será executada.
    /// </summary>
    public uint Pc { get; private set; }

    /// <summary>
    /// Endereço sequencial seguinte.
    ///
    /// Manter PC e NextPC separados permitirá implementar corretamente
    /// os branch delay slots do R3000A.
    /// </summary>
    public uint NextPc { get; private set; }

    public uint Hi { get; private set; }

    public uint Lo { get; private set; }

    /// <summary>
    /// Quantidade de instruções processadas através de Step().
    ///
    /// Por enquanto cada Step representa um ciclo lógico.
    /// A temporização real será refinada futuramente.
    /// </summary>
    public ulong Cycles { get; private set; }

    public R3000A(Bus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));

        Reset();
    }

    /// <summary>
    /// Construtor de conveniência para testes isolados.
    /// Cria um Bus com sua própria RAM.
    /// </summary>
    public R3000A()
        : this(new Bus())
    {
    }

    /// <summary>
    /// Restaura o estado interno da CPU.
    ///
    /// Por padrão, o processador começa no vetor de reset real do PS1:
    /// 0xBFC00000.
    /// </summary>
    public void Reset(uint pc = ResetVector)
    {
        Array.Clear(_registers, 0, _registers.Length);

        Pc = pc;
        NextPc = unchecked(pc + 4);

        Hi = 0;
        Lo = 0;
        Cycles = 0;
    }

    /// <summary>
    /// Busca, decodifica e executa uma instrução através do barramento.
    /// </summary>
    public void Step()
    {
        uint rawInstruction = _bus.Read32(Pc);

        /*
         * A ordem é importante:
         *
         * 1. A instrução é buscada no PC atual.
         * 2. PC passa a apontar para a instrução do delay slot.
         * 3. NextPC avança normalmente.
         * 4. A instrução é executada e poderá modificar NextPC no futuro.
         */
        Pc = NextPc;
        NextPc = unchecked(NextPc + 4);

        Execute(new Instruction(rawInstruction));

        // Proteção adicional da propriedade arquitetural do registrador $zero.
        _registers[0] = 0;

        Cycles = unchecked(Cycles + 1);
    }

    public uint GetRegister(int index)
    {
        ValidateRegisterIndex(index);

        return _registers[index];
    }

    private void SetRegister(int index, uint value)
    {
        ValidateRegisterIndex(index);

        // O registrador $zero é permanentemente igual a zero.
        if (index == 0)
            return;

        _registers[index] = value;
    }

    private static void ValidateRegisterIndex(int index)
    {
        if ((uint)index >= RegisterCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"O índice do registrador deve estar entre 0 e {RegisterCount - 1}.");
        }
    }

    /// <summary>
    /// Executa diretamente uma instrução já decodificada.
    ///
    /// Este método permanece público por enquanto para permitir testes
    /// unitários específicos de cada instrução.
    /// </summary>
    public void Execute(Instruction instruction)
    {
        switch (instruction.Opcode)
        {
            case 0x00: // SPECIAL
                ExecuteSpecial(instruction);
                break;

            case 0x09: // ADDIU rt, rs, immediate
                ExecuteAddiu(instruction);
                break;

            case 0x0A: // SLTI rt, rs, immediate
                ExecuteSlti(instruction);
                break;

            case 0x0B: // SLTIU rt, rs, immediate
                ExecuteSltiu(instruction);
                break;

            case 0x0C: // ANDI rt, rs, immediate
                SetRegister(
                    instruction.Rt,
                    GetRegister(instruction.Rs) & instruction.Immediate);
                break;

            case 0x0D: // ORI rt, rs, immediate
                SetRegister(
                    instruction.Rt,
                    GetRegister(instruction.Rs) | instruction.Immediate);
                break;

            case 0x0E: // XORI rt, rs, immediate
                SetRegister(
                    instruction.Rt,
                    GetRegister(instruction.Rs) ^ instruction.Immediate);
                break;

            case 0x0F: // LUI rt, immediate
                SetRegister(
                    instruction.Rt,
                    (uint)instruction.Immediate << 16);
                break;

            case 0x20: // LB rt, offset(rs)
                ExecuteLb(instruction);
                break;

            case 0x21: // LH rt, offset(rs)
                ExecuteLh(instruction);
                break;

            case 0x23: // LW rt, offset(rs)
                ExecuteLw(instruction);
                break;

            case 0x24: // LBU rt, offset(rs)
                ExecuteLbu(instruction);
                break;

            case 0x25: // LHU rt, offset(rs)
                ExecuteLhu(instruction);
                break;

            case 0x28: // SB rt, offset(rs)
                ExecuteSb(instruction);
                break;

            case 0x29: // SH rt, offset(rs)
                ExecuteSh(instruction);
                break;

            case 0x2B: // SW rt, offset(rs)
                ExecuteSw(instruction);
                break;

            default:
                throw new NotImplementedException(
                    $"Opcode 0x{instruction.Opcode:X2} não implementado.");
        }

        // Mesmo em execução direta, $zero deve continuar protegido.
        _registers[0] = 0;
    }

    private void ExecuteAddiu(Instruction instruction)
    {
        uint result = unchecked(
            GetRegister(instruction.Rs) +
            (uint)instruction.SignedImmediate);

        SetRegister(instruction.Rt, result);
    }

    private void ExecuteSlti(Instruction instruction)
    {
        int source = unchecked((int)GetRegister(instruction.Rs));
        int immediate = instruction.SignedImmediate;

        SetRegister(
            instruction.Rt,
            source < immediate ? 1u : 0u);
    }

    private void ExecuteSltiu(Instruction instruction)
    {
        /*
         * Apesar de ser uma comparação unsigned, o imediato é primeiro
         * estendido com sinal e depois reinterpretado como uint.
         */
        uint source = GetRegister(instruction.Rs);
        uint immediate = unchecked((uint)instruction.SignedImmediate);

        SetRegister(
            instruction.Rt,
            source < immediate ? 1u : 0u);
    }

    private void ExecuteLb(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        sbyte value = unchecked((sbyte)_bus.Read8(address));

        SetRegister(
            instruction.Rt,
            unchecked((uint)(int)value));
    }

    private void ExecuteLbu(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        byte value = _bus.Read8(address);

        SetRegister(instruction.Rt, value);
    }

    private void ExecuteLh(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        short value = unchecked((short)_bus.Read16(address));

        SetRegister(
            instruction.Rt,
            unchecked((uint)(int)value));
    }

    private void ExecuteLhu(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        ushort value = _bus.Read16(address);

        SetRegister(instruction.Rt, value);
    }

    private void ExecuteLw(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        uint value = _bus.Read32(address);

        SetRegister(instruction.Rt, value);
    }

    private void ExecuteSb(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        byte value = unchecked((byte)GetRegister(instruction.Rt));

        _bus.Write8(address, value);
    }

    private void ExecuteSh(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        ushort value = unchecked((ushort)GetRegister(instruction.Rt));

        _bus.Write16(address, value);
    }

    private void ExecuteSw(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        uint value = GetRegister(instruction.Rt);

        _bus.Write32(address, value);
    }

    private uint GetEffectiveAddress(Instruction instruction)
    {
        return unchecked(
            GetRegister(instruction.Rs) +
            (uint)instruction.SignedImmediate);
    }

    private void ExecuteSpecial(Instruction instruction)
    {
        switch (instruction.Function)
        {
            case 0x00: // SLL rd, rt, shiftAmount
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) << instruction.ShiftAmount);
                break;

            case 0x02: // SRL rd, rt, shiftAmount
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) >> instruction.ShiftAmount);
                break;

            case 0x03: // SRA rd, rt, shiftAmount
                SetRegister(
                    instruction.Rd,
                    unchecked(
                        (uint)(
                            (int)GetRegister(instruction.Rt) >>
                            instruction.ShiftAmount)));
                break;

            case 0x04: // SLLV rd, rt, rs
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) <<
                    GetVariableShiftAmount(instruction.Rs));
                break;

            case 0x06: // SRLV rd, rt, rs
                SetRegister(
                    instruction.Rd,
                    GetRegister(instruction.Rt) >>
                    GetVariableShiftAmount(instruction.Rs));
                break;

            case 0x07: // SRAV rd, rt, rs
                SetRegister(
                    instruction.Rd,
                    unchecked(
                        (uint)(
                            (int)GetRegister(instruction.Rt) >>
                            GetVariableShiftAmount(instruction.Rs))));
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
                    ~(
                        GetRegister(instruction.Rs) |
                        GetRegister(instruction.Rt)));
                break;

            case 0x2A: // SLT rd, rs, rt
            {
                int left = unchecked((int)GetRegister(instruction.Rs));
                int right = unchecked((int)GetRegister(instruction.Rt));

                SetRegister(
                    instruction.Rd,
                    left < right ? 1u : 0u);

                break;
            }

            case 0x2B: // SLTU rd, rs, rt
            {
                uint left = GetRegister(instruction.Rs);
                uint right = GetRegister(instruction.Rt);

                SetRegister(
                    instruction.Rd,
                    left < right ? 1u : 0u);

                break;
            }

            default:
                throw new NotImplementedException(
                    $"Função SPECIAL 0x{instruction.Function:X2} não implementada.");
        }
    }

    private int GetVariableShiftAmount(int registerIndex)
    {
        return (int)(GetRegister(registerIndex) & 0x1F);
    }
}