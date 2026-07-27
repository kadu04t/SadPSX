using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SadPSX.Frontend.App;
using SDL3;

namespace SadPSX.Frontend.Launcher;

internal sealed class SdlLauncher : IDisposable
{
    private const int WindowWidth = 720;
    private const int WindowHeight = 460;
    private const int DefaultInstructionBatchSize = 10_000;

    private static readonly SDL.FRect BiosButton =
        new() { X = 160, Y = 130, W = 400, H = 56 };
    private static readonly SDL.FRect DiscButton =
        new() { X = 160, Y = 215, W = 400, H = 56 };
    private static readonly SDL.FRect StartButton =
        new() { X = 240, Y = 350, W = 240, H = 56 };

    private readonly nint _window;
    private readonly nint _renderer;
    private readonly SDL.DialogFileCallback _dialogCallback;
    private readonly ConcurrentQueue<DialogResult> _dialogResults = new();

    private SDL.DialogFileFilter[]? _dialogFilters;
    private DialogTarget _pendingDialogTarget;
    private string? _biosPath;
    private string? _discPath;
    private string _status = "Select a BIOS to continue. Disc image is optional.";
    private bool _dialogOpen;
    private bool _running = true;
    private bool _launchRequested;
    private bool _disposed;

    public SdlLauncher()
    {
        if (!SDL.CreateWindowAndRenderer(
                "SadPSX Launcher",
                WindowWidth,
                WindowHeight,
                0,
                out _window,
                out _renderer))
        {
            throw new InvalidOperationException(
                $"Could not create SDL3 launcher: {SDL.GetError()}");
        }

        _dialogCallback = HandleDialogResult;
        SDL.SetRenderVSync(_renderer, 1);
    }

    public FrontendOptions? Run()
    {
        while (_running)
        {
            ProcessEvents();
            ProcessDialogResults();
            Render();
            SDL.Delay(1);
        }

        if (!_launchRequested || _biosPath is null)
            return null;

        return new FrontendOptions(
            _biosPath,
            _discPath,
            DefaultInstructionBatchSize,
            StartPaused: false,
            FrameLimit: null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeDialogFilters();
        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        _disposed = true;
    }

    private void ProcessEvents()
    {
        while (SDL.PollEvent(out SDL.Event currentEvent))
        {
            switch ((SDL.EventType)currentEvent.Type)
            {
                case SDL.EventType.Quit:
                case SDL.EventType.WindowCloseRequested:
                    if (!_dialogOpen)
                        _running = false;
                    break;

                case SDL.EventType.KeyDown:
                    if (currentEvent.Key.Repeat)
                        break;

                    if (currentEvent.Key.Scancode == SDL.Scancode.Escape &&
                        !_dialogOpen)
                    {
                        _running = false;
                    }
                    else if (currentEvent.Key.Scancode ==
                             SDL.Scancode.Return)
                    {
                        RequestLaunch();
                    }
                    break;

                case SDL.EventType.MouseButtonDown:
                    if (currentEvent.Button.Button != 1 || _dialogOpen)
                        break;

                    HandleClick(
                        currentEvent.Button.X,
                        currentEvent.Button.Y);
                    break;
            }
        }
    }

    private void HandleClick(float x, float y)
    {
        if (Contains(BiosButton, x, y))
        {
            ShowFileDialog(
                DialogTarget.Bios,
                [
                    new SDL.DialogFileFilter(
                        "PlayStation BIOS",
                        "bin;rom"),
                    new SDL.DialogFileFilter("All files", "*"),
                ]);
        }
        else if (Contains(DiscButton, x, y))
        {
            ShowFileDialog(
                DialogTarget.Disc,
                [
                    new SDL.DialogFileFilter(
                        "PlayStation disc image",
                        "cue;bin"),
                    new SDL.DialogFileFilter("All files", "*"),
                ]);
        }
        else if (Contains(StartButton, x, y))
        {
            RequestLaunch();
        }
    }

    private void ShowFileDialog(
        DialogTarget target,
        SDL.DialogFileFilter[] filters)
    {
        _pendingDialogTarget = target;
        _dialogFilters = filters;
        _dialogOpen = true;
        _status = target == DialogTarget.Bios
            ? "Waiting for BIOS selection..."
            : "Waiting for disc selection...";

        SDL.ShowOpenFileDialog(
            _dialogCallback,
            nint.Zero,
            _window,
            filters,
            filters.Length,
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            allowMany: false);
    }

    private void HandleDialogResult(
        nint userdata,
        nint fileList,
        int selectedFilter)
    {
        if (fileList == nint.Zero)
        {
            _dialogResults.Enqueue(new DialogResult(
                _pendingDialogTarget,
                null,
                SDL.GetError()));
            return;
        }

        nint firstPathPointer = Marshal.ReadIntPtr(fileList);
        string? selectedPath = firstPathPointer == nint.Zero
            ? null
            : Marshal.PtrToStringUTF8(firstPathPointer);
        _dialogResults.Enqueue(new DialogResult(
            _pendingDialogTarget,
            selectedPath,
            null));
    }

    private void ProcessDialogResults()
    {
        while (_dialogResults.TryDequeue(out DialogResult result))
        {
            _dialogOpen = false;
            DisposeDialogFilters();

            if (result.Error is not null)
            {
                _status = $"File dialog failed: {result.Error}";
                continue;
            }

            if (result.Path is null)
            {
                _status = "Selection canceled.";
                continue;
            }

            string path = Path.GetFullPath(result.Path);
            if (!File.Exists(path))
            {
                _status = "The selected file does not exist.";
                continue;
            }

            if (result.Target == DialogTarget.Bios)
                SelectBios(path);
            else
                SelectDisc(path);
        }
    }

    private void SelectBios(string path)
    {
        const long expectedBiosSize = 512 * 1024;
        if (new FileInfo(path).Length != expectedBiosSize)
        {
            _status = "Invalid BIOS: expected a 512 KiB file.";
            return;
        }

        _biosPath = path;
        _status = $"BIOS selected: {Path.GetFileName(path)}";
    }

    private void SelectDisc(string path)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            _status = "Invalid disc image: select a .cue or .bin file.";
            return;
        }

        _discPath = path;
        _status = $"Disc selected: {Path.GetFileName(path)}";
    }

    private void RequestLaunch()
    {
        if (_dialogOpen)
            return;

        if (_biosPath is null)
        {
            _status = "Select a valid 512 KiB BIOS first.";
            return;
        }

        _launchRequested = true;
        _running = false;
    }

    private void Render()
    {
        SetColor(15, 15, 18);
        EnsureSuccess(SDL.RenderClear(_renderer), "clear launcher");

        DrawCenteredText("SadPSX", 52, 2);
        DrawCenteredText("PlayStation Emulator - Beta 0.0.1", 88);

        DrawButton(
            BiosButton,
            "SELECT BIOS",
            enabled: !_dialogOpen,
            selected: _biosPath is not null);
        DrawButton(
            DiscButton,
            "SELECT .CUE / .BIN",
            enabled: !_dialogOpen,
            selected: _discPath is not null);
        DrawButton(
            StartButton,
            "START",
            enabled: !_dialogOpen && _biosPath is not null,
            selected: false);

        DrawCenteredText(
            _biosPath is null
                ? "BIOS: not selected"
                : $"BIOS: {Path.GetFileName(_biosPath)}",
            195);
        DrawCenteredText(
            _discPath is null
                ? "DISC: optional"
                : $"DISC: {Path.GetFileName(_discPath)}",
            280);
        DrawCenteredText(TrimToWidth(_status, 82), 425);

        EnsureSuccess(SDL.RenderPresent(_renderer), "present launcher");
    }

    private void DrawButton(
        SDL.FRect rectangle,
        string label,
        bool enabled,
        bool selected)
    {
        if (!enabled)
            SetColor(55, 55, 60);
        else if (selected)
            SetColor(190, 58, 30);
        else
            SetColor(235, 92, 28);

        SDL.FRect fill = rectangle;
        EnsureSuccess(
            SDL.RenderFillRect(_renderer, in fill),
            "draw launcher button");

        SetColor(255, 255, 255);
        SDL.FRect border = rectangle;
        EnsureSuccess(
            SDL.RenderRect(_renderer, in border),
            "draw launcher button border");
        DrawCenteredText(
            label,
            rectangle.Y + ((rectangle.H - 8) / 2));
    }

    private void DrawCenteredText(string text, float y, int scale = 1)
    {
        float textWidth = text.Length * 8 * scale;
        float x = (WindowWidth - textWidth) / (2 * scale);
        float scaledY = y / scale;

        EnsureSuccess(
            SDL.SetRenderScale(_renderer, scale, scale),
            "set launcher text scale");
        SetColor(245, 245, 245);
        EnsureSuccess(
            SDL.RenderDebugText(_renderer, x, scaledY, text),
            "draw launcher text");
        EnsureSuccess(
            SDL.SetRenderScale(_renderer, 1, 1),
            "reset launcher text scale");
    }

    private void SetColor(byte red, byte green, byte blue)
    {
        EnsureSuccess(
            SDL.SetRenderDrawColor(
                _renderer,
                red,
                green,
                blue,
                255),
            "set launcher color");
    }

    private void DisposeDialogFilters()
    {
        if (_dialogFilters is null)
            return;

        foreach (SDL.DialogFileFilter filter in _dialogFilters)
            filter.Dispose();

        _dialogFilters = null;
    }

    private static bool Contains(SDL.FRect rectangle, float x, float y)
    {
        return x >= rectangle.X &&
               x < rectangle.X + rectangle.W &&
               y >= rectangle.Y &&
               y < rectangle.Y + rectangle.H;
    }

    private static string TrimToWidth(string text, int maximumCharacters)
    {
        if (text.Length <= maximumCharacters)
            return text;

        return $"{text[..(maximumCharacters - 3)]}...";
    }

    private static void EnsureSuccess(bool succeeded, string operation)
    {
        if (!succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {operation} with SDL3: {SDL.GetError()}");
        }
    }

    private enum DialogTarget
    {
        Bios,
        Disc,
    }

    private readonly record struct DialogResult(
        DialogTarget Target,
        string? Path,
        string? Error);
}
