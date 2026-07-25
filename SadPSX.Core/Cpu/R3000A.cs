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

    // Verdadeiro quando a instrução atualmente em Execute() está sendo
    // executada como o delay slot de um branch/jump já tomado. Usado para
    // preencher corretamente o bit BD (branch delay) de COP0.CAUSE caso
    // essa instrução dispare uma exceção.
    private bool _executingInBranchDelaySlot;

    private bool _isStepping;
    private int _pendingLoadRegister = -1;
    private uint _pendingLoadValue;
    private int _writtenRegisterThisStep = -1;

    public Cop0 Cop0 { get; } = new();

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
        _executingInBranchDelaySlot = false;
        _exceptionRaised = false;
        _isStepping = false;
        _pendingLoadRegister = -1;
        _pendingLoadValue = 0;
        _writtenRegisterThisStep = -1;

        Cop0.Reset();
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
        bool applyPendingBranchAfterThisStep = _branchPending;
        uint pendingTarget = _branchTarget;
        _branchPending = false;

        // A instrução atual está em um delay slot se, e somente se, havia
        // um branch pendente agendado no ciclo anterior.
        _executingInBranchDelaySlot = applyPendingBranchAfterThisStep;
        _exceptionRaised = false;
        _writtenRegisterThisStep = -1;

        int loadRegisterToCommit = _pendingLoadRegister;
        uint loadValueToCommit = _pendingLoadValue;
        _pendingLoadRegister = -1;
        _pendingLoadValue = 0;

        _isStepping = true;
        try
        {
            if ((Pc & 0x03) != 0)
            {
                RaiseAddressException(ExceptionCode.AddressErrorLoad, Pc);
            }
            else
            {
                try
                {
                    uint rawInstruction = _bus.ReadInstruction32(Pc);
                    Execute(new Instruction(rawInstruction));
                }
                catch (InvalidOperationException)
                {
                    RaiseException(ExceptionCode.InstructionBusError);
                }
            }
        }
        finally
        {
            _isStepping = false;
        }

        if (loadRegisterToCommit > 0 &&
            _writtenRegisterThisStep != loadRegisterToCommit)
        {
            _registers[loadRegisterToCommit] = loadValueToCommit;
        }

        if (!_exceptionRaised)
        {
            // Fluxo normal: avança Pc/NextPc considerando um eventual
            // branch agendado por esta instrução.
            uint newPc = applyPendingBranchAfterThisStep ? pendingTarget : NextPc;
            Pc = newPc;
            NextPc = unchecked(newPc + 4);
        }
        // Se uma exceção ocorreu, RaiseException() já deixou Pc/NextPc
        // apontando para o vetor de exceção correto — nada a fazer aqui.

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

    // Vetores de exceção do R3000A. O vetor real depende do bit BEV (bit 22)
    // de SR: quando BEV=0 (padrão após o reset do PS1 ser normalmente
    // desligado pela BIOS), o handler vive na RAM; quando BEV=1 (estado
    // inicial logo após reset), o handler vive na própria BIOS.
    private const uint ExceptionVectorRam = 0x8000_0080;
    private const uint ExceptionVectorRom = 0xBFC0_0180;

    private const uint StatusBootExceptionVectorsBit = 1u << 22; // SR.BEV
    private const uint StatusIsolateCacheBit = 1u << 16; // SR.IsC

    // Sinaliza ao Step() que uma exceção ocorreu durante Execute() e que o
    // Pc/NextPc já foram definidos para o vetor de exceção — o Step() deve
    // então pular seu próprio cálculo normal de avanço de Pc/NextPc.
    private bool _exceptionRaised;

    /// <summary>
    /// Dispara uma exceção a partir da instrução atualmente em execução.
    /// Atualiza COP0 (EPC, CAUSE, pilha de modo em SR) e redireciona Pc para
    /// o vetor de exceção apropriado, cancelando qualquer branch delay slot
    /// que estivesse pendente (a exceção interrompe o fluxo imediatamente,
    /// o delay slot agendado não chega a ser executado).
    /// </summary>
    private void RaiseException(ExceptionCode code)
    {
        // Se a instrução que causou a exceção está em um delay slot, o
        // endereço de retorno correto é o do PRÓPRIO branch (Pc - 4), não o
        // do delay slot em si — é isso que permite ao handler, ao retornar,
        // reexecutar o branch e reconstituir corretamente o delay slot.
        uint epc = _executingInBranchDelaySlot ? unchecked(Pc - 4) : Pc;

        Cop0.RaiseException(code, epc, _executingInBranchDelaySlot);

        bool useRomVector = (Cop0.Sr & StatusBootExceptionVectorsBit) != 0;
        uint vector = useRomVector ? ExceptionVectorRom : ExceptionVectorRam;

        // A exceção interrompe o fluxo imediatamente: qualquer branch que
        // estivesse agendado para depois do delay slot é descartado.
        _branchPending = false;
        _pendingLoadRegister = -1;

        Pc = vector;
        NextPc = unchecked(vector + 4);

        _exceptionRaised = true;
    }

    private void RaiseAddressException(ExceptionCode code, uint badVirtualAddress)
    {
        Cop0.BadVaddr = badVirtualAddress;
        RaiseException(code);
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

        if (_isStepping)
            _writtenRegisterThisStep = index;

        _registers[index] = value;
    }

    private void LoadRegister(int index, uint value)
    {
        ValidateRegisterIndex(index);

        if (index == 0)
            return;

        if (!_isStepping)
        {
            SetRegister(index, value);
            return;
        }

        _pendingLoadRegister = index;
        _pendingLoadValue = value;
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

            case 0x08: // ADDI rt, rs, immediate (com trap de overflow)
                ExecuteAddi(instruction);
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

            case 0x10: // COP0: MFC0 / MTC0 / RFE
                ExecuteCop0(instruction);
                break;

            default:
                RaiseException(ExceptionCode.ReservedInstruction);
                break;
        }

        // Mesmo em execução direta, $zero deve continuar protegido.
        _registers[0] = 0;
    }

    private void ExecuteCop0(Instruction instruction)
    {
        // O campo "rs" (bits 25:21) diferencia as sub-operações do COP0.
        switch (instruction.Rs)
        {
            case 0x00: // MFC0 rt, rd  (rd aqui é o registrador COP0 de origem)
                LoadRegister(instruction.Rt, Cop0.GetRegister(instruction.Rd));
                break;

            case 0x04: // MTC0 rt, rd  (rd aqui é o registrador COP0 de destino)
                Cop0.SetRegister(instruction.Rd, GetRegister(instruction.Rt));
                break;

            case 0x10: // RFE (Return From Exception): identificado por
                       // rs=0b10000 com function=0b010000 no restante da
                       // instrução, mas na prática é a única operação COP0
                       // que usa esse valor de rs em código real, então
                       // tratamos diretamente aqui.
                if ((instruction.Value & 0x001F_FFFF) == 0x10)
                    Cop0.ReturnFromException();
                else
                    RaiseException(ExceptionCode.ReservedInstruction);
                break;

            default:
                RaiseException(ExceptionCode.ReservedInstruction);
                break;
        }
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
        if (instruction.Rt is not (0x00 or 0x01 or 0x10 or 0x11))
        {
            RaiseException(ExceptionCode.ReservedInstruction);
            return;
        }

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

    private void ExecuteAddi(Instruction instruction)
    {
        int source = unchecked((int)GetRegister(instruction.Rs));
        int immediate = instruction.SignedImmediate;

        try
        {
            // "checked" força o runtime a lançar OverflowException se a
            // soma estourar o range de int32 — exatamente a mesma
            // condição que o hardware real detecta para disparar a
            // exceção de overflow do ADDI (ao contrário do ADDIU, que
            // nunca dispara essa exceção e sempre faz wrap silencioso).
            int result = checked(source + immediate);
            SetRegister(instruction.Rt, unchecked((uint)result));
        }
        catch (OverflowException)
        {
            // O registrador de destino NÃO é modificado quando a exceção
            // é disparada — o hardware real intercepta antes da escrita.
            RaiseException(ExceptionCode.Overflow);
        }
    }

    private void ExecuteAdd(Instruction instruction)
    {
        int left = unchecked((int)GetRegister(instruction.Rs));
        int right = unchecked((int)GetRegister(instruction.Rt));

        try
        {
            int result = checked(left + right);
            SetRegister(instruction.Rd, unchecked((uint)result));
        }
        catch (OverflowException)
        {
            RaiseException(ExceptionCode.Overflow);
        }
    }

    private void ExecuteSub(Instruction instruction)
    {
        int left = unchecked((int)GetRegister(instruction.Rs));
        int right = unchecked((int)GetRegister(instruction.Rt));

        try
        {
            int result = checked(left - right);
            SetRegister(instruction.Rd, unchecked((uint)result));
        }
        catch (OverflowException)
        {
            RaiseException(ExceptionCode.Overflow);
        }
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

        try
        {
            sbyte value = unchecked((sbyte)_bus.Read8(address));
            LoadRegister(instruction.Rt, unchecked((uint)(int)value));
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private void ExecuteLbu(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);

        try
        {
            byte value = _bus.Read8(address);
            LoadRegister(instruction.Rt, value);
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private void ExecuteLh(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);

        if ((address & 0x01) != 0)
        {
            RaiseAddressException(ExceptionCode.AddressErrorLoad, address);
            return;
        }

        try
        {
            short value = unchecked((short)_bus.Read16(address));
            LoadRegister(instruction.Rt, unchecked((uint)(int)value));
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private void ExecuteLhu(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);

        if ((address & 0x01) != 0)
        {
            RaiseAddressException(ExceptionCode.AddressErrorLoad, address);
            return;
        }

        try
        {
            ushort value = _bus.Read16(address);
            LoadRegister(instruction.Rt, value);
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private void ExecuteLw(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);

        if ((address & 0x03) != 0)
        {
            RaiseAddressException(ExceptionCode.AddressErrorLoad, address);
            return;
        }

        try
        {
            uint value = _bus.Read32(address);
            LoadRegister(instruction.Rt, value);
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private void ExecuteSb(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        byte value = unchecked((byte)GetRegister(instruction.Rt));

        if (IsIsolatedCacheRamAccess(address))
            return;

        try
        {
            _bus.Write8(address, value);
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private void ExecuteSh(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        ushort value = unchecked((ushort)GetRegister(instruction.Rt));

        if ((address & 0x01) != 0)
        {
            RaiseAddressException(ExceptionCode.AddressErrorStore, address);
            return;
        }

        if (IsIsolatedCacheRamAccess(address))
            return;

        try
        {
            _bus.Write16(address, value);
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private void ExecuteSw(Instruction instruction)
    {
        uint address = GetEffectiveAddress(instruction);
        uint value = GetRegister(instruction.Rt);

        if ((address & 0x03) != 0)
        {
            RaiseAddressException(ExceptionCode.AddressErrorStore, address);
            return;
        }

        if (IsIsolatedCacheRamAccess(address))
            return;

        try
        {
            _bus.Write32(address, value);
        }
        catch (InvalidOperationException)
        {
            RaiseException(ExceptionCode.DataBusError);
        }
    }

    private uint GetEffectiveAddress(Instruction instruction)
    {
        return unchecked(
            GetRegister(instruction.Rs) +
            (uint)instruction.SignedImmediate);
    }

    private bool IsIsolatedCacheRamAccess(uint virtualAddress)
    {
        if ((Cop0.Sr & StatusIsolateCacheBit) == 0)
            return false;

        uint physicalAddress = Bus.TranslateToPhysical(
            virtualAddress,
            out MemorySegment segment);

        bool isCachedSegment =
            segment is MemorySegment.Kuseg or MemorySegment.Kseg0;

        return isCachedSegment && physicalAddress < 8 * 1024 * 1024;
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

            case 0x20: // ADD rd, rs, rt
                ExecuteAdd(instruction);
                break;

            case 0x23: // SUBU rd, rs, rt
                SetRegister(
                    instruction.Rd,
                    unchecked(
                        GetRegister(instruction.Rs) -
                        GetRegister(instruction.Rt)));
                break;

            case 0x22: // SUB rd, rs, rt
                ExecuteSub(instruction);
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

            case 0x0C: // SYSCALL
                RaiseException(ExceptionCode.Syscall);
                break;

            case 0x0D: // BREAK
                RaiseException(ExceptionCode.Breakpoint);
                break;

            default:
                RaiseException(ExceptionCode.ReservedInstruction);
                break;
        }
    }

    private int GetVariableShiftAmount(int registerIndex)
    {
        return (int)(GetRegister(registerIndex) & 0x1F);
    }
}
