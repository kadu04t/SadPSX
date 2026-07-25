using SadPSX.Core;
using SadPSX.Core.Debugging;

if (args.Length < 1)
{
    Console.WriteLine("Uso: SadPSX.Cli <caminho-da-bios.bin> [numero-de-instrucoes] [--trace]");
    Console.WriteLine();
    Console.WriteLine("Exemplos:");
    Console.WriteLine("  SadPSX.Cli SCPH1001.bin");
    Console.WriteLine("  SadPSX.Cli SCPH1001.bin 1000");
    Console.WriteLine("  SadPSX.Cli SCPH1001.bin 1000 --trace");
    return 1;
}

string biosPath = args[0];

if (!File.Exists(biosPath))
{
    Console.Error.WriteLine($"Erro: arquivo de BIOS não encontrado: {biosPath}");
    return 1;
}

ulong stepCount = 100; // padrão conservador; a BIOS real tem milhões de instruções antes de terminar o boot
if (args.Length >= 2 && ulong.TryParse(args[1], out ulong parsedCount))
    stepCount = parsedCount;

bool trace = args.Contains("--trace");

var machine = new PsxMachine();

try
{
    machine.LoadBios(biosPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Erro ao carregar a BIOS: {ex.Message}");
    return 1;
}

Console.WriteLine($"BIOS carregada: {biosPath}");
Console.WriteLine($"PC inicial: 0x{machine.Cpu.Pc:X8}");
Console.WriteLine($"Executando até {stepCount} instruções{(trace ? " (com trace)" : "")}...");
Console.WriteLine();

var tracer = new TraceLogger(machine) { Output = trace ? Console.Out : null, MaxEntries = 1000 };

ulong executed = 0;
Exception? failure = null;

try
{
    for (; executed < stepCount; executed++)
        tracer.Step();
}
catch (Exception ex)
{
    // Qualquer opcode não implementado ou situação inesperada interrompe a
    // execução aqui, mas não derruba o processo sem contexto: relatamos o
    // ponto exato (PC + instrução crua) para facilitar o diagnóstico.
    failure = ex;
}

Console.WriteLine();
Console.WriteLine($"Instruções executadas: {executed}");
Console.WriteLine($"PC final: 0x{machine.Cpu.Pc:X8}");
Console.WriteLine($"Ciclos: {machine.Cpu.Cycles}");

if (failure is not null)
{
    Console.WriteLine();
    Console.Error.WriteLine($"Execução interrompida por exceção: {failure.GetType().Name}: {failure.Message}");

    if (!trace && tracer.Entries.Count > 0)
    {
        var last = tracer.Entries[^1];
        Console.Error.WriteLine($"Última instrução traceada: {last}");
    }

    return 1;
}

if (!trace)
{
    Console.WriteLine();
    Console.WriteLine("Últimas instruções executadas:");
    int start = Math.Max(0, tracer.Entries.Count - 10);
    for (int i = start; i < tracer.Entries.Count; i++)
        Console.WriteLine(tracer.Entries[i]);
}

return 0;