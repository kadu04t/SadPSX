using System.Globalization;
using SadPSX.Core;
using SadPSX.Core.Bus;
using SadPSX.Core.Cpu;
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
bool validate = false;
bool stopOnUnexpected = false;
string? discPath = null;
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

            case "--validate":
                validate = true;
                break;

            case "--disc":
                discPath = Path.GetFullPath(
                    ReadOptionValue(args, ref argumentIndex, option));
                break;

            case "--stop-on-unexpected":
                stopOnUnexpected = true;
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

if (discPath is not null && !File.Exists(discPath))
{
    Console.Error.WriteLine(
        $"Erro: imagem de disco não encontrada: {discPath}");
    return 1;
}

var machine = new PsxMachine();
if (showMmioLog)
    machine.Bus.Mmio.TraceMode = MmioTraceMode.Full;

try
{
    machine.LoadBios(biosPath);
    if (discPath is not null)
        machine.LoadDisc(discPath);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Erro ao carregar a BIOS: {exception.Message}");
    return 1;
}

Console.WriteLine($"BIOS carregada: {biosPath}");
if (discPath is not null)
    Console.WriteLine($"Disco carregado: {discPath}");
Console.WriteLine($"PC inicial: 0x{machine.Cpu.Pc:X8}");
Console.WriteLine($"Executando até {stepCount} instruções{(trace ? " (com trace)" : "")}...");
Console.WriteLine();

if (stopOnUnexpected)
    return RunUntilUnexpectedException(machine, stepCount);

if (validate)
{
    BiosValidationResult result = BiosValidator.Run(machine, stepCount);
    PrintValidationResult(result);
    return result.Succeeded ? 0 : 1;
}

TraceLogger? tracer = trace
    ? new TraceLogger(machine)
    {
        Output = Console.Out,
        MaxEntries = 1000,
    }
    : null;

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
    executed = debugger.Run(
        stepCount,
        tracer is null ? machine.Step : tracer.Step);
}
catch (Exception exception)
{
    failure = exception;
}

Console.WriteLine();
Console.WriteLine($"Instruções executadas: {executed}");
Console.WriteLine($"PC final: 0x{machine.Cpu.Pc:X8}");
Console.WriteLine($"Instruções: {machine.Cpu.Cycles}");
Console.WriteLine($"Ciclos de clock: {machine.Cpu.ClockCycles}");

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

    if (tracer is not null && tracer.Entries.Count > 0)
        Console.Error.WriteLine($"Última instrução traceada: {tracer.Entries[^1]}");

    return 1;
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

static int RunUntilUnexpectedException(
    PsxMachine machine,
    ulong instructionLimit)
{
    CpuExceptionInfo? unexpectedException = null;

    void CaptureException(CpuExceptionInfo exception)
    {
        if (exception.Code is not ExceptionCode.Interrupt and
            not ExceptionCode.Syscall)
        {
            unexpectedException ??= exception;
        }
    }

    machine.Cpu.ExceptionOccurred += CaptureException;
    try
    {
        while (machine.Cpu.Cycles < instructionLimit &&
               unexpectedException is null)
        {
            machine.Step();
        }
    }
    finally
    {
        machine.Cpu.ExceptionOccurred -= CaptureException;
    }

    if (unexpectedException is not CpuExceptionInfo exception)
    {
        Console.WriteLine(
            $"Nenhuma exceção inesperada em {machine.Cpu.Cycles} instruções.");
        return 0;
    }

    uint rawInstruction = machine.Bus.Peek32(exception.FaultingPc);
    string disassembly = Disassembler.Disassemble(
        new Instruction(rawInstruction),
        exception.FaultingPc);
    Console.WriteLine(
        $"Exceção: {exception.Code} em 0x{exception.FaultingPc:X8} " +
        $"após {machine.Cpu.Cycles} instruções");
    Console.WriteLine(
        $"Opcode: 0x{rawInstruction:X8}  {disassembly}");
    Console.WriteLine(
        $"EPC=0x{exception.Epc:X8} " +
        $"delay-slot={exception.InBranchDelaySlot}");
    Console.WriteLine();
    Console.WriteLine("Memória próxima:");
    for (int offset = -16; offset <= 16; offset += 4)
    {
        uint address = unchecked(exception.FaultingPc + (uint)offset);
        Console.WriteLine(
            $"  0x{address:X8}: 0x{machine.Bus.Peek32(address):X8}");
    }

    using var debugger = new ExecutionDebugger(machine);
    Console.WriteLine();
    Console.WriteLine("[registradores]");
    Console.WriteLine(debugger.FormatRegisters());
    return 2;
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

static void PrintValidationResult(BiosValidationResult result)
{
    Console.WriteLine(
        $"Validação da BIOS: {(result.Succeeded ? "APROVADA" : "REPROVADA")}");
    Console.WriteLine(
        $"Instruções: {result.ExecutedInstructions}/{result.RequestedInstructions}");
    Console.WriteLine($"Ciclos de clock: {result.ElapsedClockCycles}");
    Console.WriteLine(
        $"PC: 0x{result.InitialPc:X8} -> 0x{result.FinalPc:X8}");
    Console.WriteLine($"PCs únicos: {result.UniqueProgramCounters}");
    Console.WriteLine(
        $"MMIO: {result.MmioAccesses} acessos, " +
        $"{result.UnhandledMmioAccesses} não tratados");

    Console.WriteLine("Exceções emuladas:");
    if (result.ExceptionCounts.Count == 0)
    {
        Console.WriteLine("  nenhuma");
    }
    else
    {
        foreach ((ExceptionCode code, ulong count) in result.ExceptionCounts
            .OrderBy(entry => entry.Key))
        {
            Console.WriteLine($"  {code}: {count}");
        }
    }

    Console.WriteLine(
        $"Exceções inesperadas: {result.UnexpectedExceptionCount}");

    if (result.FailureType is not null)
    {
        Console.WriteLine(
            $"Falha do host: {result.FailureType}: {result.FailureMessage}");
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
    Console.WriteLine("  --validate              Executa e resume uma validação da BIOS");
    Console.WriteLine("  --disc <imagem>         Conecta uma imagem BIN ou CUE");
    Console.WriteLine("  --stop-on-unexpected    Para na primeira exceção inesperada");
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
