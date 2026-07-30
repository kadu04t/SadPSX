using System.Diagnostics;
using SadPSX.Core;
using SadPSX.Core.Bus;

const ulong DefaultInstructionCount = 5_000_000;
const ulong WarmupInstructionCount = 100_000;

ulong instructionCount = args.Length == 0
    ? DefaultInstructionCount
    : ulong.Parse(args[0]);

Console.WriteLine($"SadPSX performance benchmark ({instructionCount:N0} iterations)");
Console.WriteLine($"Runtime: {Environment.Version}");
Console.WriteLine();

RunCpuBenchmark(instructionCount, executeFromRam: false);
RunCpuBenchmark(instructionCount, executeFromRam: true);
RunMachineBenchmark(instructionCount, executeFromRam: false);
RunMachineBenchmark(instructionCount, executeFromRam: true);
RunBusBenchmark(instructionCount);

static void RunCpuBenchmark(
    ulong instructionCount,
    bool executeFromRam)
{
    var machine = CreateLoopMachine(executeFromRam);
    for (ulong index = 0; index < WarmupInstructionCount; index++)
        machine.Cpu.Step();
    ResetLoopMachine(machine, executeFromRam);

    Stopwatch stopwatch = Stopwatch.StartNew();
    for (ulong index = 0; index < instructionCount; index++)
        machine.Cpu.Step();
    stopwatch.Stop();

    PrintResult(
        executeFromRam ? "CPU RAM" : "CPU BIOS",
        instructionCount,
        stopwatch.Elapsed);
}

static void RunMachineBenchmark(
    ulong instructionCount,
    bool executeFromRam)
{
    var machine = CreateLoopMachine(executeFromRam);
    machine.Run(WarmupInstructionCount);
    ResetLoopMachine(machine, executeFromRam);

    Stopwatch stopwatch = Stopwatch.StartNew();
    machine.Run(instructionCount);
    stopwatch.Stop();

    PrintResult(
        executeFromRam ? "Machine RAM" : "Machine BIOS",
        instructionCount,
        stopwatch.Elapsed);
}

static void RunBusBenchmark(ulong iterationCount)
{
    var bus = new Bus();
    uint value = 0;

    for (ulong index = 0; index < WarmupInstructionCount; index++)
    {
        bus.Write32((uint)(index & 0x001F_FFFC), (uint)index);
        value ^= bus.Read32((uint)(index & 0x001F_FFFC));
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    for (ulong index = 0; index < iterationCount; index++)
    {
        uint address = (uint)(index & 0x001F_FFFC);
        bus.Write32(address, (uint)index);
        value ^= bus.Read32(address);
    }
    stopwatch.Stop();

    GC.KeepAlive(value);
    PrintResult("RAM pairs", iterationCount, stopwatch.Elapsed);
}

static PsxMachine CreateLoopMachine(bool executeFromRam)
{
    var machine = new PsxMachine();
    byte[] image = new byte[512 * 1024];
    BitConverter.GetBytes(0x1000_FFFFu).CopyTo(image, 0);
    machine.LoadBios(image);
    if (executeFromRam)
    {
        machine.Bus.Write32(0, 0x1000_FFFFu);
        machine.Cpu.Reset(0);
    }
    return machine;
}

static void ResetLoopMachine(
    PsxMachine machine,
    bool executeFromRam)
{
    machine.Reset();
    if (executeFromRam)
        machine.Cpu.Reset(0);
}

static void PrintResult(
    string name,
    ulong iterationCount,
    TimeSpan elapsed)
{
    double operationsPerSecond =
        iterationCount / Math.Max(elapsed.TotalSeconds, double.Epsilon);
    Console.WriteLine(
        $"{name,-12} {elapsed.TotalSeconds,8:0.000}s  " +
        $"{operationsPerSecond / 1_000_000,8:0.00} Mops/s");
}
