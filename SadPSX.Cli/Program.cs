using System.Globalization;
using SadPSX.Core;
using SadPSX.Core.Debugging;
using SadPSX.Core.Memory;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

string biosPath = args[0];
ulong stepCount = 100;
bool trace = false;
bool showMmioLog = false;
bool dumpRegisters = false;
int? loopThreshold = null;
var pcBreakpoints = new HashSet<uint>();
var memoryBreakpoints = new HashSet<uint>();
var checkpoints = new HashSet<uint>();

int argumentIndex = 1;
if (argumentIndex < args.Length &&
    !args[argumentIndex].StartsWith("--", StringComparison.Ordinal) &&
    ulong.TryParse(args[argumentIndex], out ulong parsedStepCount))
{
    stepCount = parsedStepCount;
    argumentIndex++;
}

try
{
    while (argumentIndex < args.Length)
    {
        string option = args[argumentIndex++];

        switch (option)
        {
            case "--trace":
                trace = true;
                break;

            case "--mmio-log":
                showMmioLog = true;
                break;

            case "--dump-registers":
                dumpRegisters = true;
                break;

            case "--break-pc":
                pcBreakpoints.Add(ParseAddress(ReadOptionValue(args, ref argumentIndex, option)));
                break;

            case "--break-memory":
                memoryBreakpoints.Add(ParseAddress(ReadOptionValue(args, ref argumentIndex, option)));
                break;

            case "--checkpoint":
                checkpoints.Add(ParseAddress(ReadOptionValue(args, ref argumentIndex, option)));
                break;

            case "--loop-threshold":
                string thresholdText = ReadOptionValue(args, ref argumentIndex, option);
                if (!int.TryParse(thresholdText, out int parsedThreshold) ||
                    parsedThreshold <= 0)
                {
                    throw new ArgumentException(
                        $"Valor inválido para {option}: {thresholdText}.");
                }

                loopThreshold = parsedThreshold;
                break;

            default:
                throw new ArgumentException($"Opção desconhecida: {option}.");
        }
    }
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"Erro: {exception.Message}");
    Console.Error.WriteLine();
    PrintUsage();
    return 1;
}

if (!File.Exists(biosPath))
{
    Console.Error.WriteLine($"Erro: arquivo de BIOS não encontrado: {biosPath}");
    return 1;
}

var machine = new PsxMachine();

try
{
    machine.LoadBios(biosPath);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Erro ao carregar a BIOS: {exception.Message}");
    return 1;
}

Console.WriteLine($"BIOS carregada: {biosPath}");
Console.WriteLine($"PC inicial: 0x{machine.Cpu.Pc:X8}");
Console.WriteLine($"Executando até {stepCount} instruções{(trace ? " (com trace)" : "")}...");
Console.WriteLine();

var tracer = new TraceLogger(machine)
{
    Output = trace ? Console.Out : null,
    MaxEntries = 1000,
};

using var debugger = new ExecutionDebugger(machine)
{
    LoopVisitThreshold = loopThreshold,
    Output = Console.Out,
};

debugger.PcBreakpoints.UnionWith(pcBreakpoints);
debugger.MemoryBreakpoints.UnionWith(memoryBreakpoints);
debugger.Checkpoints.UnionWith(checkpoints);

ulong executed = 0;
Exception? failure = null;

try
{
    executed = debugger.Run(stepCount, tracer.Step);
}
catch (Exception exception)
{
    failure = exception;
}

Console.WriteLine();
Console.WriteLine($"Instruções executadas: {executed}");
Console.WriteLine($"PC final: 0x{machine.Cpu.Pc:X8}");
Console.WriteLine($"Ciclos: {machine.Cpu.Cycles}");

if (debugger.StopReason is DebuggerStop stop)
{
    Console.WriteLine(
        $"Parada do debugger: {stop.Kind} no ciclo {stop.Cycle}: {stop.Message}");
}

if (dumpRegisters && debugger.StopReason is null)
{
    Console.WriteLine();
    Console.WriteLine("[registradores]");
    Console.WriteLine(debugger.FormatRegisters());
}

if (showMmioLog)
    PrintMmioLog(machine.Bus.Mmio);

if (failure is not null)
{
    Console.WriteLine();
    Console.Error.WriteLine(
        $"Execução interrompida por exceção: " +
        $"{failure.GetType().Name}: {failure.Message}");

    if (!trace && tracer.Entries.Count > 0)
        Console.Error.WriteLine($"Última instrução traceada: {tracer.Entries[^1]}");

    return 1;
}

if (!trace)
{
    Console.WriteLine();
    Console.WriteLine("Últimas instruções executadas:");
    int start = Math.Max(0, tracer.Entries.Count - 10);
    for (int index = start; index < tracer.Entries.Count; index++)
        Console.WriteLine(tracer.Entries[index]);
}

return 0;

static string ReadOptionValue(string[] arguments, ref int index, string option)
{
    if (index >= arguments.Length)
        throw new ArgumentException($"A opção {option} exige um valor.");

    return arguments[index++];
}

static uint ParseAddress(string value)
{
    NumberStyles style = NumberStyles.Integer;
    string digits = value;

    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        style = NumberStyles.AllowHexSpecifier;
        digits = value[2..];
    }

    if (!uint.TryParse(digits, style, CultureInfo.InvariantCulture, out uint address))
        throw new ArgumentException($"Endereço inválido: {value}.");

    return address;
}

static void PrintMmioLog(Mmio mmio)
{
    Console.WriteLine();
    Console.WriteLine($"Acessos MMIO: {mmio.TotalAccessCount}");
    Console.WriteLine("Primeiros acessos:");

    foreach (MmioAccess access in mmio.AccessLog)
        Console.WriteLine(access);

    Console.WriteLine();
    Console.WriteLine("Resumo por endereço/operação:");

    foreach (MmioAccessSummary summary in mmio.AccessSummaries
        .OrderBy(summary => summary.FirstSequence))
    {
        string operation = summary.Kind == MemoryAccessKind.Write ? "W" : "R";
        Console.WriteLine(
            $"{operation}{summary.Width * 8,-2} 0x{summary.Address:X8} " +
            $"{summary.RegisterName,-16} count={summary.Count,-8} " +
            $"last=0x{summary.LastValue:X8} " +
            $"{(summary.Handled ? "tratado" : "não tratado")}");
    }
}

static void PrintUsage()
{
    Console.WriteLine(
        "Uso: SadPSX.Cli <bios.bin> [instruções] [opções]");
    Console.WriteLine();
    Console.WriteLine("Opções:");
    Console.WriteLine("  --trace                 Imprime cada instrução executada");
    Console.WriteLine("  --mmio-log              Mostra primeiros acessos e resumo MMIO");
    Console.WriteLine("  --dump-registers        Mostra os registradores ao finalizar");
    Console.WriteLine("  --break-pc <endereço>   Para antes de executar o PC informado");
    Console.WriteLine("  --break-memory <addr>   Para após acessar o endereço informado");
    Console.WriteLine("  --checkpoint <endereço> Registra ciclo e MMIO ao alcançar o PC");
    Console.WriteLine("  --loop-threshold <n>    Para quando um PC for visitado n vezes");
    Console.WriteLine();
    Console.WriteLine("Endereços aceitam decimal ou hexadecimal com prefixo 0x.");
    Console.WriteLine();
    Console.WriteLine("Exemplo:");
    Console.WriteLine(
        "  SadPSX.Cli SCPH1001.BIN 1000000 --mmio-log " +
        "--checkpoint 0xBFC00000 --loop-threshold 100000");
}
