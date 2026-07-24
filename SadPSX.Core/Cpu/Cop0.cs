namespace SadPSX.Core.Cpu;

/// <summary>
/// Coprocessador 0 (System Control) do R3000A: mantém o estado de exceções,
/// modo de operação (kernel/usuário) e máscara de interrupções.
///
/// Por enquanto só os registradores relevantes para SYSCALL/BREAK e o
/// mecanismo genérico de exceção são modelados (SR, CAUSE, EPC). Os demais
/// (BadVaddr, registradores de breakpoint de hardware, PRId, etc.) serão
/// adicionados conforme forem necessários.
/// </summary>
public sealed class Cop0
{
    private const int RegisterCount = 32;

    // Índices dos registradores COP0 relevantes dentro do banco de 32.
    private const int StatusIndex = 12;
    private const int CauseIndex = 13;
    private const int EpcIndex = 14;

    private readonly uint[] _registers = new uint[RegisterCount];

    /// <summary>
    /// Status Register (SR, registrador 12). Controla o modo de operação
    /// (kernel/usuário), a máscara de interrupções, e mantém uma pilha de
    /// 3 níveis desses dois bits que é deslocada a cada exceção/retorno.
    /// </summary>
    public uint Sr
    {
        get => _registers[StatusIndex];
        set => _registers[StatusIndex] = value;
    }

    /// <summary>
    /// Cause Register (CAUSE, registrador 13). Indica a causa da exceção
    /// mais recente através do campo ExcCode (bits 6:2), e se a instrução
    /// que causou a exceção estava em um branch delay slot (bit 31, BD).
    /// </summary>
    public uint Cause
    {
        get => _registers[CauseIndex];
        set => _registers[CauseIndex] = value;
    }

    /// <summary>
    /// Exception Program Counter (EPC, registrador 14). Endereço de retorno
    /// da exceção: normalmente o PC da instrução que causou a exceção, ou
    /// PC-4 se a exceção ocorreu dentro de um branch delay slot (nesse
    /// caso o bit BD de CAUSE também é setado, e o handler deve reexecutar
    /// o branch para reconstituir o delay slot corretamente).
    /// </summary>
    public uint Epc
    {
        get => _registers[EpcIndex];
        set => _registers[EpcIndex] = value;
    }

    public uint GetRegister(int index)
    {
        ValidateRegisterIndex(index);
        return _registers[index];
    }

    public void SetRegister(int index, uint value)
    {
        ValidateRegisterIndex(index);
        _registers[index] = value;
    }

    private static void ValidateRegisterIndex(int index)
    {
        if ((uint)index >= RegisterCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"O índice do registrador COP0 deve estar entre 0 e {RegisterCount - 1}.");
        }
    }

    /// <summary>
    /// Registra os efeitos de uma exceção sobre CAUSE, EPC e SR, seguindo o
    /// comportamento real do R3000A. Não decide o vetor de destino (isso é
    /// responsabilidade de quem chama, com base no bit BEV de SR) — apenas
    /// atualiza o estado do coprocessador.
    /// </summary>
    /// <param name="excCode">Código da exceção (campo ExcCode de CAUSE).</param>
    /// <param name="epc">
    /// Endereço da instrução que causou a exceção (ou do branch, se a
    /// exceção ocorreu dentro de um delay slot).
    /// </param>
    /// <param name="inBranchDelaySlot">
    /// Verdadeiro se a instrução que causou a exceção estava em um branch
    /// delay slot.
    /// </param>
    public void RaiseException(ExceptionCode excCode, uint epc, bool inBranchDelaySlot)
    {
        Epc = epc;

        uint cause = Cause;

        // Limpa o campo ExcCode (bits 6:2) e o bit BD (31), depois grava
        // os novos valores.
        cause &= ~(0b1_1111u << 2);
        cause |= ((uint)excCode & 0x1F) << 2;

        if (inBranchDelaySlot)
            cause |= 1u << 31;
        else
            cause &= ~(1u << 31);

        Cause = cause;

        // "Empilha" o modo kernel/usuário e a máscara de interrupção: os
        // bits [5:0] de SR (KUc/IEc, KUp/IEp, KUo/IEo — os 3 níveis mais
        // recentes) são deslocados 2 bits à esquerda, e os 2 bits mais
        // baixos (o novo nível "atual") são zerados — o que força o modo
        // kernel com interrupções desabilitadas durante o handler.
        uint sr = Sr;
        uint stack = sr & 0x3F;
        stack = (stack << 2) & 0x3F;
        Sr = (sr & ~0x3Fu) | stack;
    }

    /// <summary>
    /// Reverte o efeito de RaiseException sobre a pilha de modo de SR
    /// (equivalente à instrução RFE: Return From Exception). Não altera
    /// PC — isso é responsabilidade de quem chama.
    /// </summary>
    public void ReturnFromException()
    {
        uint sr = Sr;
        uint stack = sr & 0x3F;
        stack >>= 2;
        Sr = (sr & ~0x3Fu) | stack;
    }
}

/// <summary>
/// Códigos de exceção (campo ExcCode do registrador CAUSE) do R3000A.
/// Apenas os relevantes para a implementação atual estão listados; os
/// demais serão adicionados conforme forem necessários.
/// </summary>
public enum ExceptionCode : uint
{
    Interrupt = 0x00,
    AddressErrorLoad = 0x04,
    AddressErrorStore = 0x05,
    Syscall = 0x08,
    Breakpoint = 0x09,
    ReservedInstruction = 0x0A,
    CoprocessorUnusable = 0x0B,
    Overflow = 0x0C,
}