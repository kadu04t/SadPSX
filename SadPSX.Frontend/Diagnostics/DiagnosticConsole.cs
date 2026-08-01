using SadPSX.Core;
using SadPSX.Core.Bios;
using SadPSX.Core.Bus;
using SadPSX.Core.Controllers;
using SadPSX.Core.Cpu;
using SadPSX.Core.Debugging;
using SadPSX.Core.Dma;
using SadPSX.Frontend.Video;

namespace SadPSX.Frontend.Diagnostics;

internal sealed class DiagnosticConsole : IDisposable
{
    private const ulong DmaStallThresholdCycles = 3_386_880;

    private static readonly string[] RegisterNames =
    [
        "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
        "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
        "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
        "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra",
    ];

    private readonly RuntimeDiagnostics _diagnostics;
    private readonly Mmio _mmio;
    private readonly PostStatusRegister _postStatus;
    private readonly StreamWriter? _logWriter;
    private readonly HashSet<uint> _observedProgramCounters = [];

    private ulong _lastInstructions;
    private ulong _lastVideoFrames;
    private ulong _lastCapturedFrames;
    private ulong _lastPresentedFrames;
    private ulong _lastUnhandledMmio;
    private double _lastInstructionsPerSecond;
    private VideoPresentationMetrics _lastVideoMetrics;
    private bool _cpuStallReported;
    private bool _videoStallReported;
    private bool _presentationStallReported;
    private bool _dmaStallReported;
    private bool _loopReported;
    private bool _disposed;

    public DiagnosticConsole(PsxMachine machine)
    {
        _diagnostics = new RuntimeDiagnostics(machine);
        _mmio = machine.Bus.Mmio;
        _postStatus = machine.Bus.PostStatus;
        _diagnostics.UnexpectedExceptionOccurred += OnUnexpectedException;
        _postStatus.ValueChanged += OnPostStatusChanged;
        _logWriter = CreateLogWriter(out string? logPath);

        WriteHeader("Console de diagnóstico iniciado");
        if (logPath is not null)
            Write(DiagnosticLevel.Info, $"Log: {logPath}");
    }

    public void Poll(
        bool paused,
        double instructionsPerSecond,
        VideoPresentationMetrics videoMetrics)
    {
        RuntimeDiagnosticSnapshot snapshot = _diagnostics.Capture();
        _lastInstructionsPerSecond = instructionsPerSecond;
        _lastVideoMetrics = videoMetrics;
        ulong instructionDelta = snapshot.Instructions - _lastInstructions;
        ulong frameDelta = snapshot.VideoFrames - _lastVideoFrames;
        ulong captureDelta =
            videoMetrics.CapturedFrames - _lastCapturedFrames;
        ulong presentationDelta =
            videoMetrics.PresentedFrames - _lastPresentedFrames;

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

        bool presentationStalled =
            !paused &&
            frameDelta > 0 &&
            (captureDelta == 0 || presentationDelta == 0);
        if (presentationStalled)
        {
            if (!_presentationStallReported)
            {
                Write(
                    DiagnosticLevel.Warning,
                    "O timing de vídeo avança, mas o frontend deixou de capturar ou apresentar frames.");
                PrintStatus(snapshot, instructionsPerSecond);
                _presentationStallReported = true;
            }
        }
        else
        {
            _presentationStallReported = false;
        }

        bool dmaStalled =
            snapshot.GpuDma.ActiveCycles >= DmaStallThresholdCycles ||
            snapshot.CdRomDma.ActiveCycles >= DmaStallThresholdCycles;
        if (!paused && instructionDelta > 0 && dmaStalled)
        {
            if (!_dmaStallReported)
            {
                Write(
                    DiagnosticLevel.Warning,
                    "DMA ocupado por tempo anormal enquanto a CPU continua executando.");
                PrintStatus(snapshot, instructionsPerSecond);
                _dmaStallReported = true;
            }
        }
        else
        {
            _dmaStallReported = false;
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
        _lastCapturedFrames = videoMetrics.CapturedFrames;
        _lastPresentedFrames = videoMetrics.PresentedFrames;
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
                $"EPC=0x{exception.Epc:X8} BD={exception.InBranchDelaySlot}" +
                FormatExceptionInstruction(exception));
        }
    }

    public void PrintSio0(int maximumEntries = 64)
    {
        IReadOnlyList<Sio0TransferTrace> transfers =
            _diagnostics.CaptureSio0Transfers();
        WriteHeader("Transações SIO0 recentes");
        if (transfers.Count == 0)
        {
            WriteRaw("Nenhuma transferência registrada.");
            return;
        }

        foreach (Sio0TransferTrace transfer in
                 transfers.TakeLast(maximumEntries))
        {
            string peripheral = transfer.Peripheral switch
            {
                Sio0PeripheralKind.Controller => "PAD ",
                Sio0PeripheralKind.MemoryCard => "CARD",
                _ => "UNKN",
            };
            string acknowledge = transfer.Acknowledge
                ? $"sim@{transfer.AcknowledgeCycle}"
                : "não";
            WriteRaw(
                $"#{transfer.Sequence:D6} " +
                $"T{transfer.Transaction:D5}[{transfer.ByteIndex:D3}] " +
                $"ciclos={transfer.StartCycle}-{transfer.EndCycle} " +
                $"P{transfer.Port} {peripheral} " +
                $"conectado={(transfer.Connected ? "sim" : "não")} " +
                $"fila={(transfer.Queued ? "sim" : "não")} " +
                $"TX=0x{transfer.Transmit:X2} RX=0x{transfer.Receive:X2} " +
                $"ACK={acknowledge} CTRL=0x{transfer.Control:X4} " +
                $"MODE=0x{transfer.Mode:X4} BAUD=0x{transfer.Baud:X4}");
        }
    }

    public void PrintMemoryCards(int maximumEntries = 32)
    {
        IReadOnlyList<MemoryCardDiagnosticEntry> commands =
            _diagnostics.CaptureMemoryCardCommands();
        WriteHeader("Comandos de memory card recentes");
        if (commands.Count == 0)
        {
            WriteRaw("Nenhum comando concluído.");
            return;
        }

        foreach (MemoryCardDiagnosticEntry entry in
                 commands.TakeLast(maximumEntries))
        {
            MemoryCardCommandTrace trace = entry.Trace;
            string command = trace.Command switch
            {
                0x52 => "READ ",
                0x53 => "GETID",
                0x57 => "WRITE",
                _ => $"0x{trace.Command:X2}",
            };
            string sector = trace.Sector is ushort sectorValue
                ? $"{sectorValue:D4}"
                : "----";
            string expectedChecksum = trace.ExpectedChecksum is byte expected
                ? $"0x{expected:X2}"
                : "----";
            string receivedChecksum = trace.ReceivedChecksum is byte received
                ? $"0x{received:X2}"
                : "----";
            WriteRaw(
                $"P{entry.Port} #{trace.Sequence:D5} {command} " +
                $"setor={sector} checksum={receivedChecksum}/{expectedChecksum} " +
                $"final=0x{trace.Result:X2} " +
                $"resultado={(trace.Success ? "ok" : "erro")}");
        }
    }

    public void ToggleFullMmioTrace()
    {
        bool enable = _mmio.TraceMode != MmioTraceMode.Full;
        _mmio.ClearAccessLog();
        _mmio.TraceMode = enable
            ? MmioTraceMode.Full
            : MmioTraceMode.UnhandledOnly;
        _lastUnhandledMmio = 0;
        Write(
            DiagnosticLevel.Info,
            enable
                ? "Trace MMIO completo ativado; o desempenho pode diminuir."
                : "Trace MMIO completo desativado; apenas acessos não tratados serão registrados.");
    }

    public void Reset()
    {
        _diagnostics.Clear();
        _lastInstructions = 0;
        _lastVideoFrames = 0;
        _lastCapturedFrames = 0;
        _lastPresentedFrames = 0;
        _lastUnhandledMmio = 0;
        _lastInstructionsPerSecond = 0;
        _lastVideoMetrics = default;
        _observedProgramCounters.Clear();
        _cpuStallReported = false;
        _videoStallReported = false;
        _presentationStallReported = false;
        _dmaStallReported = false;
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
            $"FIFO={snapshot.GpuDmaFifoWords} " +
            $"sync={snapshot.CpuGp0FifoSynchronizations}/" +
            $"{snapshot.CpuGp0FifoWordsDrained} " +
            $"GPUSTAT=0x{snapshot.GpuStatus:X8}");
        if (snapshot.PendingGp0Command is uint pendingGp0Command)
        {
            string expectedWords = snapshot.ExpectedGp0Words < 0
                ? "variável"
                : snapshot.ExpectedGp0Words.ToString();
            WriteRaw(
                $"GP0 pendente=0x{pendingGp0Command:X8} " +
                $"palavras={snapshot.PendingGp0Words}/{expectedWords}");
        }
        if (snapshot.RejectedPrimitives > 0)
        {
            WriteRaw(
                $"GPU descartou={snapshot.RejectedPrimitives} " +
                $"primeira={snapshot.FirstRejectedPrimitive}");
        }
        WriteRaw(
            $"Display VRAM=0x{snapshot.DisplayVramStart:X5} " +
            $"H=0x{snapshot.HorizontalDisplayRange:X6} " +
            $"V=0x{snapshot.VerticalDisplayRange:X5} " +
            $"capturados={_lastVideoMetrics.CapturedFrames} " +
            $"apresentados={_lastVideoMetrics.PresentedFrames} " +
            $"descartados={_lastVideoMetrics.DroppedFrames} " +
            $"repetidos={_lastVideoMetrics.ConsecutiveDuplicateFrames}");
        WriteRaw(
            $"IRQ    I_STAT=0x{snapshot.InterruptStatus:X4} " +
            $"I_MASK=0x{snapshot.InterruptMask:X4}");
        WriteRaw(
            $"CD-ROM disco={(snapshot.HasDisc ? "sim" : "não")} " +
            $"IRQ={snapshot.CdRomInterruptFlags} " +
            $"IE=0x{snapshot.CdRomInterruptEnable:X2} " +
            $"modo=0x{snapshot.CdRomMode:X2} " +
            $"cmd=0x{snapshot.CdRomLastCommand:X2} " +
            $"total={snapshot.CdRomCommands} " +
            $"busy={(snapshot.CdRomCommandBusy ? "sim" : "não")} " +
            $"LBA={snapshot.CdRomLogicalBlockAddress} " +
            $"lendo={(snapshot.CdRomReading ? "sim" : "não")} " +
            $"tocando={(snapshot.CdRomPlaying ? "sim" : "não")} " +
            $"seek={(snapshot.CdRomSeeking ? "sim" : "não")} " +
            $"resultados={snapshot.CdRomResultBytes} " +
            $"setores={snapshot.CdRomBufferedSectors} " +
            $"dados={snapshot.CdRomDataBytes} bytes");
        WriteRaw(
            $"DMA    transferências={snapshot.DmaTransfers}  " +
            $"MMIO não tratado={snapshot.UnhandledMmioAccesses}");
        WriteRaw(FormatDmaChannel("DMA2 GPU", snapshot.GpuDma));
        if (snapshot.GpuDmaTransfer.Active)
        {
            DmaGpuTransferSnapshot transfer = snapshot.GpuDmaTransfer;
            WriteRaw(
                $"DMA2 ativo lista={transfer.LinkedList} " +
                $"cabeçalho={transfer.HeaderPending} " +
                $"início=0x{transfer.StartAddress:X6} " +
                $"nó=0x{transfer.HeaderAddress:X6} " +
                $"qtd={transfer.CommandsInNode} " +
                $"atual=0x{transfer.CurrentAddress:X6} " +
                $"comando=0x{transfer.CommandAddress:X6} " +
                $"restantes={transfer.CommandsRemaining} " +
                $"próximo=0x{transfer.NextAddress:X6} " +
                $"palavras={transfer.TransferredWords}");
        }
        WriteRaw(
            $"{FormatDmaChannel("DMA3 CD ", snapshot.CdRomDma)} " +
            $"GPU-espera={snapshot.GpuDmaWaitReason} " +
            $"há={snapshot.GpuDmaWaitCycles} ciclos");
        WriteRaw(
            $"Boot   POST=0x{snapshot.PostStatus:X2} " +
            $"(escritas={snapshot.PostWriteCount})  " +
            $"SIO0_CTRL=0x{snapshot.Sio0Control:X4} " +
            $"RX={snapshot.Sio0ReceiveBytes}");
    }

    private static string FormatDmaChannel(
        string name,
        DmaChannelRuntimeSnapshot channel) =>
        $"{name} MADR=0x{channel.BaseAddress:X6} " +
        $"BCR=0x{channel.BlockControl:X8} " +
        $"CHCR=0x{channel.ChannelControl:X8} " +
        $"busy={(channel.Busy ? "sim" : "não")} " +
        $"há={channel.ActiveCycles} " +
        $"último={channel.LastTransferCycles} " +
        $"máximo={channel.LongestTransferCycles} ciclos " +
        $"concluídas={channel.CompletedTransfers}";

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
            $"EPC=0x{exception.Epc:X8}, delay-slot={exception.InBranchDelaySlot}" +
            FormatExceptionInstruction(exception) + ".");

        if (exception.Code == ExceptionCode.Breakpoint)
        {
            PrintSio0(maximumEntries: 160);
            PrintMemoryCards();
        }
    }

    private static string FormatExceptionInstruction(
        CpuExceptionInfo exception)
    {
        if (exception.RawInstruction is not uint rawInstruction)
            return " Instr=<indisponível>";

        string disassembly = Disassembler.Disassemble(
            new Instruction(rawInstruction),
            exception.FaultingPc);
        string breakCode = exception.BreakCode is uint code
            ? $" BreakCode=0x{code:X5}"
            : string.Empty;
        return $" Instr=0x{rawInstruction:X8} ({disassembly}){breakCode}";
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
