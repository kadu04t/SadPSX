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

    // Suporte a branch delay slot: quando um branch/jump é tomado, o desvio
    // não acontece imediatamente. A instrução seguinte (o "delay slot")
    // sempre executa primeiro, e só depois o PC pula para o alvo agendado.
    private bool _branchPending;
    private uint _branchTarget;

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

        _branchPending = false;
        _branchTarget = 0;
    }

    /// <summary>
    /// Busca, decodifica e executa uma instrução através do barramento.
    ///
    /// Ordem crítica: a instrução é executada com Pc/NextPc ainda nos
    /// valores ATUAIS (Pc = endereço da instrução sendo executada, NextPc =
    /// endereço do delay slot). Isso é o que permite aos cálculos de branch
    /// target, jump target e endereço de retorno (link register) usarem
    /// NextPc como referência correta. Só depois que Execute termina é que
    /// Pc/NextPc avançam para o próximo ciclo.
    ///
    /// Quando há um branch pendente (agendado no ciclo anterior — ou seja,
    /// este Step está executando o delay slot de um branch já tomado), o
    /// alvo do branch se torna o próximo Pc, não o próximo NextPc. NextPc
    /// é sempre recalculado a partir do novo Pc (+4), pronto para o ciclo
    /// seguinte.
    /// </summary>
    public void Step()
    {
        uint rawInstruction = _bus.Read32(Pc);

        bool applyPendingBranchAfterThisStep = _branchPending;
        uint pendingTarget = _branchTarget;
        _branchPending = false;

        Execute(new Instruction(rawInstruction));

        uint newPc = applyPendingBranchAfterThisStep ? pendingTarget : NextPc;
        Pc = newPc;
        NextPc = unchecked(newPc + 4);

        // Proteção adicional da propriedade arquitetural do registrador $zero.
        _registers[0] = 0;

        Cycles = unchecked(Cycles + 1);
    }

    // Usado pelas instruções de branch/jump para agendar um desvio que só
    // terá efeito depois que a instrução do delay slot for executada.
    private void ScheduleBranch(uint target)
    {
        _branchPending = true;
        _branchTarget = target;
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

            case 0x04: // BEQ rs, rt, offset
                if (GetRegister(instruction.Rs) == GetRegister(instruction.Rt))
                    ScheduleBranch(BranchTarget(instruction));
                break;

            case 0x05: // BNE rs, rt, offset
                if (GetRegister(instruction.Rs) != GetRegister(instruction.Rt))
                    ScheduleBranch(BranchTarget(instruction));
                break;

            case 0x06: // BLEZ rs, offset (rs <= 0, com sinal)
                if ((int)GetRegister(instruction.Rs) <= 0)
                    ScheduleBranch(BranchTarget(instruction));
                break;

            case 0x07: // BGTZ rs, offset (rs > 0, com sinal)
                if ((int)GetRegister(instruction.Rs) > 0)
                    ScheduleBranch(BranchTarget(instruction));
                break;

            case 0x01: // REGIMM: BLTZ / BGEZ / BLTZAL / BGEZAL (diferenciados por rt)
                ExecuteRegImm(instruction);
                break;

            case 0x02: // J target
                ScheduleBranch(JumpTarget(instruction));
                break;

            case 0x03: // JAL target
                SetRegister(31, unchecked(NextPc + 4)); // endereço de retorno ($ra)
                ScheduleBranch(JumpTarget(instruction));
                break;

            default:
                throw new NotImplementedException(
                    $"Opcode 0x{instruction.Opcode:X2} não implementado.");
        }

        // Mesmo em execução direta, $zero deve continuar protegido.
        _registers[0] = 0;
    }

    // Alvo de branches condicionais: relativo a NextPc (o endereço do delay
    // slot), não ao Pc da instrução de branch em si.
    private uint BranchTarget(Instruction instruction) =>
        unchecked(NextPc + ((uint)instruction.SignedImmediate << 2));

    // Alvo de J/JAL: mantém os 4 bits mais altos do PC atual (região), e
    // substitui os 28 bits baixos pelo campo de 26 bits do jump target
    // deslocado 2 bits à esquerda (instruções são alinhadas em 4 bytes).
    private uint JumpTarget(Instruction instruction) =>
        (NextPc & 0xF000_0000) | (instruction.JumpTarget << 2);

    private void ExecuteRegImm(Instruction instruction)
    {
        bool isLessThanZero = (int)GetRegister(instruction.Rs) < 0;
        bool isGreaterOrEqualZero = !isLessThanZero;

        // O bit mais baixo de rt distingue LTZ (0) de GEZ (1).
        // O bit mais alto de rt distingue "sem link" (0) de "com link" (1, *AL*).
        bool linkVariant = (instruction.Rt & 0x10) != 0;
        bool isGezVariant = (instruction.Rt & 0x01) != 0;

        bool takeBranch = isGezVariant ? isGreaterOrEqualZero : isLessThanZero;

        // BLTZAL/BGEZAL sempre gravam o endereço de retorno em $ra,
        // mesmo quando o branch não é tomado — assim como no hardware real.
        if (linkVariant)
            SetRegister(31, unchecked(NextPc + 4));

        if (takeBranch)
            ScheduleBranch(BranchTarget(instruction));
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

            case 0x08: // JR rs
                ScheduleBranch(GetRegister(instruction.Rs));
                break;

            case 0x09: // JALR rd, rs
            {
                uint target = GetRegister(instruction.Rs);
                uint returnAddress = unchecked(NextPc + 4);

                SetRegister(instruction.Rd, returnAddress);
                ScheduleBranch(target);
                break;
            }

            case 0x10: // MFHI rd
                SetRegister(instruction.Rd, Hi);
                break;

            case 0x11: // MTHI rs
                Hi = GetRegister(instruction.Rs);
                break;

            case 0x12: // MFLO rd
                SetRegister(instruction.Rd, Lo);
                break;

            case 0x13: // MTLO rs
                Lo = GetRegister(instruction.Rs);
                break;

            case 0x18: // MULT rs, rt (signed, 32x32 -> 64 bits em Hi:Lo)
            {
                long left = unchecked((int)GetRegister(instruction.Rs));
                long right = unchecked((int)GetRegister(instruction.Rt));
                long product = unchecked(left * right);

                Lo = unchecked((uint)product);
                Hi = unchecked((uint)(product >> 32));
                break;
            }

            case 0x19: // MULTU rs, rt (unsigned, 32x32 -> 64 bits em Hi:Lo)
            {
                ulong left = GetRegister(instruction.Rs);
                ulong right = GetRegister(instruction.Rt);
                ulong product = unchecked(left * right);

                Lo = unchecked((uint)product);
                Hi = unchecked((uint)(product >> 32));
                break;
            }

            case 0x1A: // DIV rs, rt (signed: Lo=quociente, Hi=resto)
            {
                int dividend = unchecked((int)GetRegister(instruction.Rs));
                int divisor = unchecked((int)GetRegister(instruction.Rt));

                if (divisor == 0)
                {
                    // Comportamento definido do hardware real para divisão
                    // por zero (não lança exceção, ao contrário do C#).
                    Lo = dividend < 0 ? 1u : 0xFFFF_FFFFu;
                    Hi = unchecked((uint)dividend);
                }
                else if (dividend == int.MinValue && divisor == -1)
                {
                    // Caso especial: -2147483648 / -1 estouraria o range de
                    // int. O hardware real produz este resultado específico
                    // em vez de lançar overflow.
                    Lo = unchecked((uint)int.MinValue);
                    Hi = 0;
                }
                else
                {
                    Lo = unchecked((uint)(dividend / divisor));
                    Hi = unchecked((uint)(dividend % divisor));
                }
                break;
            }

            case 0x1B: // DIVU rs, rt (unsigned: Lo=quociente, Hi=resto)
            {
                uint dividend = GetRegister(instruction.Rs);
                uint divisor = GetRegister(instruction.Rt);

                if (divisor == 0)
                {
                    // Comportamento definido do hardware real para divisão
                    // por zero (não lança exceção, ao contrário do C#).
                    Lo = 0xFFFF_FFFFu;
                    Hi = dividend;
                }
                else
                {
                    Lo = dividend / divisor;
                    Hi = dividend % divisor;
                }
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