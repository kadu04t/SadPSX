using System.Diagnostics;
using SadPSX.Core;
using SadPSX.Core.Controllers;
using SadPSX.Frontend.Audio;
using SadPSX.Frontend.Diagnostics;
using SadPSX.Frontend.Input;
using SadPSX.Frontend.Launcher;
using SadPSX.Frontend.Library;
using SadPSX.Frontend.UI.Hosting;
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
    private readonly SdlAudioOutput _audioOutput;
    private readonly MemoryCard _memoryCard;
    private readonly ControllerInputState _controllerInput;
    private readonly SdlGamepadInput _gamepadInput;
    private readonly int _instructionBatchSize;
    private readonly int? _frameLimit;
    private readonly bool _allowDashboardReturn;
    private readonly string? _discPath;
    private readonly string? _discSerial;
    private readonly GameActivityStore _activityStore = new();
    private readonly Stopwatch _activePlayTime = new();
    private readonly FrontendSettingsStore _settingsStore = new();
    private readonly Stopwatch _runtime = Stopwatch.StartNew();

    private TimeSpan _lastFrameTime;
    private TimeSpan _lastTitleTime;
    private ulong _lastTitleInstructionCount;
    private int _presentedFrames;
    private bool _running = true;
    private bool _paused;
    private bool _analogController;
    private FrontendSessionResult _sessionResult = FrontendSessionResult.Quit;

    private FrontendApplication(
        FrontendOptions options,
        SdlFrontendHost host,
        bool allowDashboardReturn)
    {
        _machine = new PsxMachine();
        _memoryCard = MemoryCard.LoadOrCreate(
            options.MemoryCardPath ?? GetDefaultMemoryCardPath());
        _machine.Bus.Sio0.AttachMemoryCard(1, _memoryCard);
        _machine.LoadBios(options.BiosPath);
        if (options.DiscPath is not null)
            _machine.LoadDisc(options.DiscPath);
        _discSerial = _machine.Bus.CdRom.BootInfo is { } bootInfo
            ? GameIdentityService.ParseSerial(bootInfo.ExecutablePath)
            : null;
        _diagnosticConsole = new DiagnosticConsole(_machine);
        _instructionBatchSize = options.InstructionBatchSize;
        _frameLimit = options.FrameLimit;
        _allowDashboardReturn = allowDashboardReturn;
        _discPath = options.DiscPath;
        _paused = options.StartPaused;
        FrontendSettings settings = _settingsStore.Load();
        _videoOutput = new SdlVideoOutput(
            host,
            settings.VideoScaling,
            settings.SmoothVideo);
        _audioOutput = new SdlAudioOutput();
        IController initialController = settings.DefaultAnalogController
            ? new AnalogController()
            : new DigitalController();
        _machine.Bus.Sio0.AttachController(1, initialController);
        _analogController = settings.DefaultAnalogController;
        _controllerInput = new ControllerInputState(
            initialController);
        _gamepadInput = new SdlGamepadInput(
            _controllerInput,
            settings.EffectiveControllerMapping);
        _machine.Bus.VideoTiming.VBlankStarted += OnVBlankStarted;
        _audioOutput.SetPaused(_paused);
        if (_discPath is not null)
        {
            TryBeginActivitySession();
            if (!_paused)
                _activePlayTime.Start();
        }
    }

    public static int Run(string[] arguments)
    {
        FrontendOptions? options = null;
        if (arguments.Length > 0)
        {
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
        }

        if (options is not null && !File.Exists(options.BiosPath))
        {
            Console.Error.WriteLine(
                $"Erro: arquivo de BIOS não encontrado: {options.BiosPath}");
            return 1;
        }

        if (options?.DiscPath is not null &&
            !File.Exists(options.DiscPath))
        {
            Console.Error.WriteLine(
                $"Erro: imagem de disco não encontrada: {options.DiscPath}");
            return 1;
        }

        if (!SDL.Init(
                SDL.InitFlags.Video |
                SDL.InitFlags.Audio |
                SDL.InitFlags.Gamepad))
        {
            Console.Error.WriteLine(
                $"Erro ao inicializar SDL3: {SDL.GetError()}");
            return 1;
        }

        try
        {
            FrontendSettingsStore settingsStore = new();
            FrontendSettings settings = settingsStore.Load();
            if (options is not null)
            {
                using var directHost = new SdlFrontendHost(settings.Fullscreen);
                using var directApplication = new FrontendApplication(
                    options,
                    directHost,
                    allowDashboardReturn: false);
                directApplication.PrintStartup(options);
                directApplication.MainLoop();
                return 0;
            }

            if (settings.ShowBootAnimation)
            {
                using var splash = new SdlSplashScreen();
                splash.Run();
            }

            using var host = new SdlFrontendHost(settings.Fullscreen);
            bool showBootAnimation = true;
            while (true)
            {
                FrontendOptions? selectedOptions;
                using (var launcher = new SdlLauncher(
                           host,
                           showBootAnimation))
                {
                    selectedOptions = launcher.Run();
                }

                if (selectedOptions is null)
                    return 0;

                FrontendSessionResult result;
                using (var application = new FrontendApplication(
                           selectedOptions,
                           host,
                           allowDashboardReturn: true))
                {
                    application.PrintStartup(selectedOptions);
                    result = application.MainLoop();
                }

                if (result == FrontendSessionResult.Quit)
                    return 0;

                showBootAnimation = false;
            }
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
        _machine.Bus.VideoTiming.VBlankStarted -= OnVBlankStarted;
        _activePlayTime.Stop();
        TryCompleteActivitySession();
        if (_memoryCard.IsDirty)
            _memoryCard.Save();
        _gamepadInput.Dispose();
        _videoOutput.Dispose();
        _audioOutput.Dispose();
        _diagnosticConsole.Dispose();
    }

    private FrontendSessionResult MainLoop()
    {
        try
        {
            _videoOutput.Capture(_machine.Bus.Gpu);
            PresentFrame();

            while (_running)
            {
                ProcessEvents();

                if (!_paused)
                {
                    _machine.Run((ulong)_instructionBatchSize);
                    _diagnosticConsole.ObserveExecution(_machine.Cpu.Pc);
                    _audioOutput.Pump(_machine.Bus.Spu);
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

            return _sessionResult;
        }
        catch (Exception exception)
        {
            _diagnosticConsole.ReportFatal(exception);
            throw;
        }
    }

    private void PresentFrame()
    {
        if (!_videoOutput.PresentPending())
            return;

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
                    _sessionResult = FrontendSessionResult.Quit;
                    _running = false;
                    break;

                case SDL.EventType.KeyDown:
                    HandleControllerKey(
                        currentEvent.Key.Scancode,
                        pressed: true);
                    if (!currentEvent.Key.Repeat)
                        HandleKey(currentEvent.Key.Scancode);
                    break;

                case SDL.EventType.KeyUp:
                    HandleControllerKey(
                        currentEvent.Key.Scancode,
                        pressed: false);
                    break;

                case SDL.EventType.GamepadAdded:
                    _gamepadInput.HandleDeviceAdded(
                        currentEvent.GDevice.Which);
                    break;

                case SDL.EventType.GamepadRemoved:
                    _gamepadInput.HandleDeviceRemoved(
                        currentEvent.GDevice.Which);
                    break;

                case SDL.EventType.GamepadButtonDown:
                case SDL.EventType.GamepadButtonUp:
                    if (_allowDashboardReturn &&
                        currentEvent.GButton.Down &&
                        (SDL.GamepadButton)currentEvent.GButton.Button ==
                        SDL.GamepadButton.Guide)
                    {
                        ReturnToDashboard();
                        break;
                    }

                    _gamepadInput.HandleButton(
                        currentEvent.GButton.Which,
                        (SDL.GamepadButton)currentEvent.GButton.Button,
                        currentEvent.GButton.Down);
                    break;

                case SDL.EventType.GamepadAxisMotion:
                    _gamepadInput.HandleAxis(
                        currentEvent.GAxis.Which,
                        (SDL.GamepadAxis)currentEvent.GAxis.Axis,
                        currentEvent.GAxis.Value);
                    break;
            }
        }

        _gamepadInput.Poll();
    }

    private void HandleControllerKey(SDL.Scancode scancode, bool pressed)
    {
        ControllerButton? button = scancode switch
        {
            SDL.Scancode.Up => ControllerButton.Up,
            SDL.Scancode.Right => ControllerButton.Right,
            SDL.Scancode.Down => ControllerButton.Down,
            SDL.Scancode.Left => ControllerButton.Left,
            SDL.Scancode.Return => ControllerButton.Start,
            SDL.Scancode.Backspace => ControllerButton.Select,
            SDL.Scancode.Z => ControllerButton.Cross,
            SDL.Scancode.X => ControllerButton.Circle,
            SDL.Scancode.A => ControllerButton.Square,
            SDL.Scancode.S => ControllerButton.Triangle,
            SDL.Scancode.Q => ControllerButton.L1,
            SDL.Scancode.W => ControllerButton.R1,
            SDL.Scancode.E => ControllerButton.L2,
            SDL.Scancode.D => ControllerButton.R2,
            _ => null,
        };

        if (button is ControllerButton mappedButton)
            _controllerInput.SetKeyboardButton(mappedButton, pressed);
    }

    private void HandleKey(SDL.Scancode scancode)
    {
        switch (scancode)
        {
            case SDL.Scancode.Escape:
                if (_allowDashboardReturn)
                    ReturnToDashboard();
                else
                {
                    _sessionResult = FrontendSessionResult.Quit;
                    _running = false;
                }
                break;

            case SDL.Scancode.Space:
                _paused = !_paused;
                _audioOutput.SetPaused(_paused);
                if (_discPath is not null)
                {
                    if (_paused)
                        _activePlayTime.Stop();
                    else
                        _activePlayTime.Start();
                }
                Console.WriteLine(
                    _paused
                        ? "Emulação pausada."
                        : "Emulação retomada.");
                break;

            case SDL.Scancode.R:
                _machine.Reset();
                _audioOutput.Clear();
                _diagnosticConsole.Reset();
                _lastTitleInstructionCount = 0;
                Console.WriteLine("Console reiniciado.");
                break;

            case SDL.Scancode.F1:
                DiagnosticTerminal.EnsureVisible();
                _diagnosticConsole.PrintStatus();
                break;

            case SDL.Scancode.F2:
                DiagnosticTerminal.EnsureVisible();
                _diagnosticConsole.PrintCpu();
                break;

            case SDL.Scancode.F3:
                DiagnosticTerminal.EnsureVisible();
                _diagnosticConsole.PrintMmio();
                break;

            case SDL.Scancode.F4:
                DiagnosticTerminal.EnsureVisible();
                _diagnosticConsole.PrintExceptions();
                break;

            case SDL.Scancode.F5:
                DiagnosticTerminal.EnsureVisible();
                _diagnosticConsole.PrintSio0();
                break;

            case SDL.Scancode.F6:
                DiagnosticTerminal.EnsureVisible();
                _diagnosticConsole.PrintMemoryCards();
                break;

            case SDL.Scancode.F7:
                DiagnosticTerminal.EnsureVisible();
                _diagnosticConsole.ToggleFullMmioTrace();
                break;

            case SDL.Scancode.F8:
                DiagnosticTerminal.EnsureVisible();
                ToggleControllerType();
                break;

            case SDL.Scancode.F11:
                bool fullscreen = _videoOutput.ToggleFullscreen();
                FrontendSettingsStore settingsStore = new();
                FrontendSettings settings = settingsStore.Load();
                settingsStore.Save(settings with { Fullscreen = fullscreen });
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
        _diagnosticConsole.Poll(
            _paused,
            instructionsPerSecond,
            _videoOutput.Metrics);

        string state = _paused ? "Pausado" : "Executando";
        _videoOutput.SetTitle(
            $"SadPSX | {state} | PC 0x{_machine.Cpu.Pc:X8} | " +
            $"{instructionsPerSecond / 1_000_000:0.00} MIPS");
    }

    private void OnVBlankStarted(uint scanline)
    {
        _videoOutput.Capture(_machine.Bus.Gpu);
    }

    private void ToggleControllerType()
    {
        IController controller = _analogController
            ? new DigitalController()
            : new AnalogController();
        _analogController = !_analogController;
        _machine.Bus.Sio0.AttachController(1, controller);
        _controllerInput.SetController(controller);
        try
        {
            FrontendSettings settings = _settingsStore.Load();
            _settingsStore.Save(settings with
            {
                DefaultAnalogController = _analogController,
            });
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        Console.WriteLine(
            _analogController
                ? "Controle da porta 1: DualShock."
                : "Controle da porta 1: digital.");
    }

    private void ReturnToDashboard()
    {
        _sessionResult = FrontendSessionResult.ReturnToDashboard;
        _running = false;
    }

    private void TryBeginActivitySession()
    {
        try
        {
            _activityStore.BeginSession(
                _discPath!,
                _discSerial,
                DateTimeOffset.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void TryCompleteActivitySession()
    {
        if (_discPath is null)
            return;

        try
        {
            _activityStore.CompleteSession(
                _discPath,
                _discSerial,
                _activePlayTime.Elapsed);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void PrintStartup(FrontendOptions options)
    {
        Console.WriteLine("SadPSX Frontend");
        Console.WriteLine($"BIOS: {options.BiosPath}");
        if (options.DiscPath is not null)
        {
            Console.WriteLine($"Disco: {options.DiscPath}");
            if (_machine.Bus.CdRom.BootInfo is { } bootInfo)
            {
                Console.WriteLine(
                    $"Boot: {bootInfo.ExecutablePath} " +
                    $"(LBA {bootInfo.LogicalBlockAddress}, " +
                    $"{bootInfo.FileSize} bytes)");
            }
            else
            {
                Console.WriteLine(
                    "Boot: SYSTEM.CNF ou executável não encontrado.");
            }
        }
        Console.WriteLine($"Memory card: {_memoryCard.BackingPath}");
        Console.WriteLine($"Lote de instruções: {_instructionBatchSize}");
        Console.WriteLine();
        Console.WriteLine("Controles:");
        Console.WriteLine("  Gamepad Xbox/PlayStation/genérico via SDL3");
        Console.WriteLine("  Setas   Direcional");
        Console.WriteLine("  Z/X     Cruz/Círculo");
        Console.WriteLine("  A/S     Quadrado/Triângulo");
        Console.WriteLine("  Q/W     L1/R1");
        Console.WriteLine("  E/D     L2/R2");
        Console.WriteLine("  Enter   Start");
        Console.WriteLine("  Backsp. Select");
        Console.WriteLine("  Espaço  Pausar/continuar");
        Console.WriteLine("  R       Reiniciar console");
        Console.WriteLine("  F1      Estado geral");
        Console.WriteLine("  F2      CPU e registradores");
        Console.WriteLine("  F3      MMIO não tratado");
        Console.WriteLine("  F4      Exceções recentes");
        Console.WriteLine("  F5      Transações SIO0 recentes");
        Console.WriteLine("  F6      Comandos de memory card recentes");
        Console.WriteLine("  F7      Alternar trace MMIO completo");
        Console.WriteLine("  F8      Alternar controle Digital/DualShock");
        Console.WriteLine("  F11     Alternar tela cheia");
        Console.WriteLine(
            _allowDashboardReturn
                ? "  Esc     Voltar ao dashboard"
                : "  Esc     Sair");
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "Uso: SadPSX " +
            "<BIOS.BIN> [--disc jogo.cue|jogo.bin] " +
            "[--memory-card card1.mcr] " +
            "[--batch N] [--paused] [--frames N]");
    }

    private static string GetDefaultMemoryCardPath()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "SadPSX", "MemoryCards", "card1.mcr");
    }
}
