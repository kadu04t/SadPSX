using SadPSX.Core;
using SadPSX.Core.Bios;
using SadPSX.Core.Bus;
using SadPSX.Core.Cpu;
using SadPSX.Core.Debugging;

namespace SadPSX.Frontend.Diagnostics;

internal sealed class DiagnosticConsole : IDisposable
{
    private static readonly string[] RegisterNames =
    [
        "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
        "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
        "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
        "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra",
    ];

    private readonly RuntimeDiagnostics _diagnostics;
    private readonly PostStatusRegister _postStatus;
    private readonly StreamWriter? _logWriter;
    private readonly HashSet<uint> _observedProgramCounters = [];

    private ulong _lastInstructions;
    private ulong _lastVideoFrames;
    private ulong _lastUnhandledMmio;
    private double _lastInstructionsPerSecond;
    private bool _cpuStallReported;
    private bool _videoStallReported;
    private bool _loopReported;
    private bool _disposed;

    public DiagnosticConsole(PsxMachine machine)
    {
        _diagnostics = new RuntimeDiagnostics(machine);
        _postStatus = machine.Bus.PostStatus;
        _diagnostics.UnexpectedExceptionOccurred += OnUnexpectedException;
        _postStatus.ValueChanged += OnPostStatusChanged;
        _logWriter = CreateLogWriter(out string? logPath);

        WriteHeader("Console de diagnóstico iniciado");
        if (logPath is not null)
            Write(DiagnosticLevel.Info, $"Log: {logPath}");
    }

    public void Poll(bool paused, double instructionsPerSecond)
    {
        RuntimeDiagnosticSnapshot snapshot = _diagnostics.Capture();
        _lastInstructionsPerSecond = instructionsPerSecond;
        ulong instructionDelta = snapshot.Instructions - _lastInstructions;
        ulong frameDelta = snapshot.VideoFrames - _lastVideoFrames;

        if (!paused && instructionDelta == 0)
        {
            if (!_cpuStallReported)
            {
                Write(
                    DiagnosticLevel.Error,
                    "A CPU não executou instruções no último intervalo.");
                PrintStatus(snapshot, instructionsPerSecond);
                _cpuStallReported = true;
            }
        }
        else
        {
            _cpuStallReported = false;
        }

        if (!paused && instructionDelta > 0 && frameDelta == 0)
        {
            if (!_videoStallReported)
            {
                Write(
                    DiagnosticLevel.Warning,
                    "A CPU avança, mas o timing de vídeo não completou um frame.");
                _videoStallReported = true;
            }
        }
        else
        {
            _videoStallReported = false;
        }

        if (!paused &&
            instructionDelta >= 100_000 &&
            _observedProgramCounters.Count is > 0 and <= 4)
        {
            if (!_loopReported)
            {
                Write(
                    DiagnosticLevel.Warning,
                    $"Possível loop curto ({_observedProgramCounters.Count} PCs) " +
                    $"próximo de 0x{snapshot.Pc:X8}: " +
                    snapshot.Disassembly);
                Write(
                    DiagnosticLevel.Info,
                    "A CPU continua ativa; o código pode estar aguardando IRQ ou dispositivo.");
                _loopReported = true;
            }
        }
        else
        {
            _loopReported = false;
        }

        if (snapshot.UnhandledMmioAccesses > _lastUnhandledMmio)
        {
            ulong newAccesses =
                snapshot.UnhandledMmioAccesses - _lastUnhandledMmio;
            Write(
                DiagnosticLevel.Warning,
                $"{newAccesses} novo(s) acesso(s) MMIO não tratado(s). " +
                "Pressione F3 para detalhes.");
        }

        _lastInstructions = snapshot.Instructions;
        _lastVideoFrames = snapshot.VideoFrames;
        _lastUnhandledMmio = snapshot.UnhandledMmioAccesses;
        _observedProgramCounters.Clear();
    }

    public void ObserveExecution(uint pc)
    {
        if (_observedProgramCounters.Count < 32)
            _observedProgramCounters.Add(pc);
    }

    public void PrintStatus()
    {
        PrintStatus(_diagnostics.Capture(), _lastInstructionsPerSecond);
    }

    public void PrintCpu()
    {
        RuntimeDiagnosticSnapshot snapshot = _diagnostics.Capture();
        uint[] registers = _diagnostics.CaptureRegisters();
        WriteHeader("CPU");
        WriteRaw(
            $"PC=0x{snapshot.Pc:X8}  NextPC=0x{snapshot.NextPc:X8}  " +
            $"Instr=0x{snapshot.RawInstruction:X8}");
        WriteRaw(snapshot.Disassembly);

        for (int index = 0; index < registers.Length; index += 4)
        {
            WriteRaw(string.Join(
                "  ",
                Enumerable.Range(index, 4).Select(register =>
                    $"${RegisterNames[register],-4}=0x{registers[register]:X8}")));
        }
    }

    public void PrintMmio()
    {
        IReadOnlyList<MmioAccessSummary> summaries =
            _diagnostics.CaptureUnhandledMmio();
        WriteHeader("MMIO não tratado");
        if (summaries.Count == 0)
        {
            WriteRaw("Nenhum acesso não tratado.");
            return;
        }

        foreach (MmioAccessSummary summary in summaries.Take(20))
        {
            string operation =
                summary.Kind == MemoryAccessKind.Write ? "W" : "R";
            WriteRaw(
                $"{operation}{summary.Width * 8,-2} 0x{summary.Address:X8}  " +
                $"vezes={summary.Count} último=0x{summary.LastValue:X8}");
        }
    }

    public void PrintExceptions()
    {
        WriteHeader("Exceções recentes");
        if (_diagnostics.RecentExceptions.Count == 0)
        {
            WriteRaw("Nenhuma exceção registrada.");
            return;
        }

        foreach (CpuExceptionInfo exception in _diagnostics.RecentExceptions)
        {
            WriteRaw(
                $"{exception.Code,-22} PC=0x{exception.FaultingPc:X8} " +
                $"EPC=0x{exception.Epc:X8} BD={exception.InBranchDelaySlot}");
        }
    }

    public void Reset()
    {
        _diagnostics.Clear();
        _lastInstructions = 0;
        _lastVideoFrames = 0;
        _lastUnhandledMmio = 0;
        _lastInstructionsPerSecond = 0;
        _observedProgramCounters.Clear();
        _cpuStallReported = false;
        _videoStallReported = false;
        _loopReported = false;
        Write(DiagnosticLevel.Info, "Diagnósticos reiniciados.");
    }

    public void ReportFatal(Exception exception)
    {
        Write(
            DiagnosticLevel.Fatal,
            $"{exception.GetType().Name}: {exception.Message}");
        WriteRaw(exception.StackTrace ?? "<sem stack trace>");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _diagnostics.UnexpectedExceptionOccurred -= OnUnexpectedException;
        _postStatus.ValueChanged -= OnPostStatusChanged;
        _diagnostics.Dispose();
        _logWriter?.Dispose();
        _disposed = true;
    }

    private void PrintStatus(
        RuntimeDiagnosticSnapshot snapshot,
        double instructionsPerSecond)
    {
        WriteHeader("Estado atual");
        WriteRaw(
            $"CPU    PC=0x{snapshot.Pc:X8}  instruções={snapshot.Instructions:N0}  " +
            $"clock={snapshot.ClockCycles:N0}  {instructionsPerSecond / 1_000_000:0.00} MIPS");
        WriteRaw(
            $"Código 0x{snapshot.RawInstruction:X8}  {snapshot.Disassembly}");
        WriteRaw(
            $"Vídeo  frame={snapshot.VideoFrames} linha={snapshot.Scanline} " +
            $"GP0={snapshot.Gp0Commands} GP1={snapshot.Gp1Commands} " +
            $"GPUSTAT=0x{snapshot.GpuStatus:X8}");
        WriteRaw(
            $"IRQ    I_STAT=0x{snapshot.InterruptStatus:X4} " +
            $"I_MASK=0x{snapshot.InterruptMask:X4}");
        WriteRaw(
            $"CD-ROM disco={(snapshot.HasDisc ? "sim" : "não")} " +
            $"IRQ={snapshot.CdRomInterruptFlags} dados={snapshot.CdRomDataBytes} bytes");
        WriteRaw(
            $"DMA    transferências={snapshot.DmaTransfers}  " +
            $"MMIO não tratado={snapshot.UnhandledMmioAccesses}");
        WriteRaw(
            $"Boot   POST=0x{snapshot.PostStatus:X2} " +
            $"(escritas={snapshot.PostWriteCount})  " +
            $"SIO0_CTRL=0x{snapshot.Sio0Control:X4} " +
            $"RX={snapshot.Sio0ReceiveBytes}");
    }

    private void OnPostStatusChanged(byte value)
    {
        Write(
            DiagnosticLevel.Info,
            $"BIOS POST avançou para 0x{value:X2}.");
    }

    private void OnUnexpectedException(CpuExceptionInfo exception)
    {
        Write(
            DiagnosticLevel.Error,
            $"{exception.Code} em PC=0x{exception.FaultingPc:X8}, " +
            $"EPC=0x{exception.Epc:X8}, delay-slot={exception.InBranchDelaySlot}.");
    }

    private void WriteHeader(string title) =>
        WriteRaw($"{Environment.NewLine}=== {title} ===");

    private void Write(DiagnosticLevel level, string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [{level.ToString().ToUpperInvariant()}] {message}";
        WriteRaw(line);
    }

    private void WriteRaw(string message)
    {
        global::System.Console.WriteLine(message);
        _logWriter?.WriteLine(message);
    }

    private static StreamWriter? CreateLogWriter(out string? logPath)
    {
        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            logPath = Path.Combine(directory, "SadPSX.log");
            return new StreamWriter(logPath, append: false)
            {
                AutoFlush = true,
            };
        }
        catch (IOException)
        {
            logPath = null;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            logPath = null;
            return null;
        }
    }

    private enum DiagnosticLevel
    {
        Info,
        Warning,
        Error,
        Fatal,
    }
}
