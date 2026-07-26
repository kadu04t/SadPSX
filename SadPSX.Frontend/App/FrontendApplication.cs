using System.Diagnostics;
using SadPSX.Core;
using SadPSX.Frontend.Diagnostics;
using SadPSX.Frontend.Video;
using SDL3;

namespace SadPSX.Frontend.App;

internal sealed class FrontendApplication : IDisposable
{
    private static readonly TimeSpan FrameInterval =
        TimeSpan.FromSeconds(1.0 / 60.0);
    private static readonly TimeSpan TitleInterval =
        TimeSpan.FromSeconds(1);

    private readonly PsxMachine _machine;
    private readonly DiagnosticConsole _diagnosticConsole;
    private readonly SdlVideoOutput _videoOutput;
    private readonly int _instructionBatchSize;
    private readonly int? _frameLimit;
    private readonly Stopwatch _runtime = Stopwatch.StartNew();

    private TimeSpan _lastFrameTime;
    private TimeSpan _lastTitleTime;
    private ulong _lastTitleInstructionCount;
    private int _presentedFrames;
    private bool _running = true;
    private bool _paused;

    private FrontendApplication(FrontendOptions options)
    {
        _machine = new PsxMachine();
        _machine.LoadBios(options.BiosPath);
        if (options.DiscPath is not null)
            _machine.LoadDisc(options.DiscPath);
        _diagnosticConsole = new DiagnosticConsole(_machine);
        _instructionBatchSize = options.InstructionBatchSize;
        _frameLimit = options.FrameLimit;
        _paused = options.StartPaused;
        _videoOutput = new SdlVideoOutput("SadPSX", 960, 720);
    }

    public static int Run(string[] arguments)
    {
        FrontendOptions options;
        try
        {
            options = FrontendOptions.Parse(arguments);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Erro: {exception.Message}");
            PrintUsage();
            return 1;
        }

        if (!File.Exists(options.BiosPath))
        {
            Console.Error.WriteLine(
                $"Erro: arquivo de BIOS não encontrado: {options.BiosPath}");
            return 1;
        }

        if (options.DiscPath is not null && !File.Exists(options.DiscPath))
        {
            Console.Error.WriteLine(
                $"Erro: imagem de disco não encontrada: {options.DiscPath}");
            return 1;
        }

        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Gamepad))
        {
            Console.Error.WriteLine(
                $"Erro ao inicializar SDL3: {SDL.GetError()}");
            return 1;
        }

        try
        {
            using var application = new FrontendApplication(options);
            application.PrintStartup(options);
            application.MainLoop();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Frontend interrompido: {exception.Message}");
            return 1;
        }
        finally
        {
            SDL.Quit();
        }
    }

    public void Dispose()
    {
        _videoOutput.Dispose();
        _diagnosticConsole.Dispose();
    }

    private void MainLoop()
    {
        try
        {
            PresentFrame();

            while (_running)
            {
                ProcessEvents();

                if (!_paused)
                {
                    _machine.Run((ulong)_instructionBatchSize);
                    _diagnosticConsole.ObserveExecution(_machine.Cpu.Pc);
                }
                else
                    SDL.Delay(1);

                TimeSpan now = _runtime.Elapsed;
                if (now - _lastFrameTime >= FrameInterval)
                {
                    PresentFrame();
                    _lastFrameTime = now;
                }

                if (now - _lastTitleTime >= TitleInterval)
                {
                    UpdateWindowTitle(now);
                    _lastTitleTime = now;
                }
            }
        }
        catch (Exception exception)
        {
            _diagnosticConsole.ReportFatal(exception);
            throw;
        }
    }

    private void PresentFrame()
    {
        _videoOutput.Present(_machine.Bus.Gpu);
        _presentedFrames++;

        if (_frameLimit is int frameLimit &&
            _presentedFrames >= frameLimit)
        {
            _running = false;
        }
    }

    private void ProcessEvents()
    {
        while (SDL.PollEvent(out SDL.Event currentEvent))
        {
            switch ((SDL.EventType)currentEvent.Type)
            {
                case SDL.EventType.Quit:
                case SDL.EventType.WindowCloseRequested:
                    _running = false;
                    break;

                case SDL.EventType.KeyDown
                    when !currentEvent.Key.Repeat:
                    HandleKey(currentEvent.Key.Scancode);
                    break;
            }
        }
    }

    private void HandleKey(SDL.Scancode scancode)
    {
        switch (scancode)
        {
            case SDL.Scancode.Escape:
                _running = false;
                break;

            case SDL.Scancode.Space:
                _paused = !_paused;
                Console.WriteLine(
                    _paused
                        ? "Emulação pausada."
                        : "Emulação retomada.");
                break;

            case SDL.Scancode.R:
                _machine.Reset();
                _diagnosticConsole.Reset();
                _lastTitleInstructionCount = 0;
                Console.WriteLine("Console reiniciado.");
                break;

            case SDL.Scancode.F1:
                _diagnosticConsole.PrintStatus();
                break;

            case SDL.Scancode.F2:
                _diagnosticConsole.PrintCpu();
                break;

            case SDL.Scancode.F3:
                _diagnosticConsole.PrintMmio();
                break;

            case SDL.Scancode.F4:
                _diagnosticConsole.PrintExceptions();
                break;

            case SDL.Scancode.F11:
                _videoOutput.ToggleFullscreen();
                break;
        }
    }

    private void UpdateWindowTitle(TimeSpan now)
    {
        ulong instructionCount = _machine.Cpu.Cycles;
        ulong elapsedInstructions =
            instructionCount - _lastTitleInstructionCount;
        double elapsedSeconds = Math.Max(
            (now - _lastTitleTime).TotalSeconds,
            double.Epsilon);
        double instructionsPerSecond =
            elapsedInstructions / elapsedSeconds;
        _lastTitleInstructionCount = instructionCount;
        _diagnosticConsole.Poll(_paused, instructionsPerSecond);

        string state = _paused ? "Pausado" : "Executando";
        _videoOutput.SetTitle(
            $"SadPSX | {state} | PC 0x{_machine.Cpu.Pc:X8} | " +
            $"{instructionsPerSecond / 1_000_000:0.00} MIPS");
    }

    private void PrintStartup(FrontendOptions options)
    {
        Console.WriteLine("SadPSX Frontend");
        Console.WriteLine($"BIOS: {options.BiosPath}");
        if (options.DiscPath is not null)
            Console.WriteLine($"Disco: {options.DiscPath}");
        Console.WriteLine($"Lote de instruções: {_instructionBatchSize}");
        Console.WriteLine();
        Console.WriteLine("Controles:");
        Console.WriteLine("  Espaço  Pausar/continuar");
        Console.WriteLine("  R       Reiniciar console");
        Console.WriteLine("  F1      Estado geral");
        Console.WriteLine("  F2      CPU e registradores");
        Console.WriteLine("  F3      MMIO não tratado");
        Console.WriteLine("  F4      Exceções recentes");
        Console.WriteLine("  F11     Alternar tela cheia");
        Console.WriteLine("  Esc     Sair");
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "Uso: dotnet run --project SadPSX.Frontend -- " +
            "<BIOS.BIN> [--disc jogo.cue|jogo.bin] " +
            "[--batch N] [--paused] [--frames N]");
    }
}
