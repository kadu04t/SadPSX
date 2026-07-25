using SadPSX.Core.Cpu;

namespace SadPSX.Core.Debugging;

/// <summary>
/// Produz uma linha de trace legível para cada instrução executada por um
/// <see cref="PsxMachine"/>: endereço, texto desmontado, e (opcionalmente)
/// o número do ciclo.
///
/// O <see cref="TraceLogger"/> não altera a CPU nem o barramento — ele lê
/// a instrução crua diretamente do <see cref="Memory.Bus"/> no PC atual
/// (antes de o Step() acontecer), desmonta com o <see cref="Disassembler"/>,
/// e só então delega a execução para <see cref="PsxMachine.Step"/>.
///
/// Uso típico:
/// <code>
/// var machine = new PsxMachine();
/// machine.LoadBios(biosBytes);
/// var tracer = new TraceLogger(machine);
/// tracer.Step();  // loga a instrução E a executa
/// </code>
/// </summary>
public sealed class TraceLogger
{
    private readonly PsxMachine _machine;

    /// <summary>
    /// Lista de linhas de trace acumuladas desde a criação (ou desde o
    /// último <see cref="Clear"/>). Cada Step() adiciona uma entrada.
    /// </summary>
    public List<TraceEntry> Entries { get; } = new();

    /// <summary>
    /// Se definido, cada linha de trace também é escrita aqui em tempo
    /// real (por exemplo, Console.Out ou um StreamWriter para arquivo).
    /// Deixe null para apenas acumular em <see cref="Entries"/>.
    /// </summary>
    public TextWriter? Output { get; set; }

    /// <summary>
    /// Limite de entradas mantidas em <see cref="Entries"/>. Quando
    /// atingido, entradas mais antigas são descartadas (buffer circular
    /// simples), para evitar consumo de memória ilimitado em traces longos.
    /// Null desativa o limite.
    /// </summary>
    public int? MaxEntries { get; set; }

    public TraceLogger(PsxMachine machine)
    {
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
    }

    /// <summary>
    /// Lê e desmonta a instrução no Pc atual, registra uma entrada de
    /// trace, e então executa um ciclo de CPU (equivalente a
    /// <see cref="PsxMachine.Step"/>).
    /// </summary>
    public void Step()
    {
        uint pc = _machine.Cpu.Pc;
        uint rawInstruction = 0;
        string disassembly;

        if ((pc & 0x03) != 0)
        {
            disassembly = "<erro de alinhamento no fetch>";
        }
        else
        {
            try
            {
                rawInstruction = _machine.Bus.Read32(pc);
                var instruction = new Instruction(rawInstruction);
                disassembly = Disassembler.Disassemble(instruction, pc);
            }
            catch (InvalidOperationException)
            {
                disassembly = "<erro de barramento no fetch>";
            }
        }

        var entry = new TraceEntry(_machine.Cpu.Cycles, pc, rawInstruction, disassembly);
        RecordEntry(entry);

        _machine.Step();
    }

    /// <summary>
    /// Executa e traceia <paramref name="stepCount"/> ciclos em sequência.
    /// </summary>
    public void Run(ulong stepCount)
    {
        for (ulong i = 0; i < stepCount; i++)
            Step();
    }

    /// <summary>
    /// Executa e traceia ciclos até que <paramref name="stopCondition"/>
    /// retorne verdadeiro, ou até <paramref name="maxSteps"/> ser atingido.
    /// Retorna verdadeiro se a condição foi satisfeita.
    /// </summary>
    public bool RunUntil(Func<PsxMachine, bool> stopCondition, ulong maxSteps)
    {
        ArgumentNullException.ThrowIfNull(stopCondition);

        for (ulong i = 0; i < maxSteps; i++)
        {
            Step();

            if (stopCondition(_machine))
                return true;
        }

        return false;
    }

    public void Clear() => Entries.Clear();

    private void RecordEntry(TraceEntry entry)
    {
        Entries.Add(entry);

        if (MaxEntries is int max && Entries.Count > max)
            Entries.RemoveAt(0);

        Output?.WriteLine(entry.ToString());
    }
}

/// <summary>
/// Uma única linha de trace: em qual ciclo, em qual endereço, qual
/// instrução crua, e seu texto desmontado.
/// </summary>
public readonly record struct TraceEntry(ulong Cycle, uint Pc, uint RawInstruction, string Disassembly)
{
    public override string ToString() =>
        $"{Cycle,10} | 0x{Pc:X8} | {RawInstruction:X8} | {Disassembly}";
}
