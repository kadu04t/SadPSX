using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SadPSX.Core.Controllers;
using SadPSX.Frontend.App;
using SadPSX.Frontend.Input;
using SadPSX.Frontend.Library;
using SadPSX.Frontend.UI.Audio;
using SadPSX.Frontend.UI.Hosting;
using SadPSX.Frontend.UI.Animation;
using SadPSX.Frontend.UI.Navigation;
using SadPSX.Frontend.UI.Rendering;
using SadPSX.Frontend.UI.Screens;
using SadPSX.Frontend.UI.Theming;
using SDL3;

namespace SadPSX.Frontend.Launcher;

internal sealed class SdlLauncher : IDisposable
{
    private const int CanvasWidth = 1920;
    private const int CanvasHeight = 1080;
    private const int DefaultInstructionBatchSize = 10_000;
    private const int BiosItem = 0;
    private const int ContinueItem = 1;
    private const int SettingsFullscreenItem = 0;
    private const int SettingsScalingItem = 1;
    private const int SettingsFilterItem = 2;
    private const int SettingsAudioItem = 3;
    private const int SettingsVolumeItem = 4;
    private const int SettingsControllerItem = 5;
    private const int SettingsMappingItem = 6;
    private const int SettingsBootItem = 7;
    private const int SettingsCoversItem = 8;
    private const int SettingsSoundsItem = 9;
    private const int SettingsThemeItem = 10;
    private const int SettingsWallpaperItem = 11;
    private const int SettingsCustomWallpaperItem = 12;
    private const int SettingsParallaxItem = 13;
    private const int SettingsUpdatesItem = 14;
    private const int SettingsLibraryItem = 15;
    private const int SettingsRescanItem = 16;
    private const int SettingsBackItem = 17;
    private const int SettingsItemCount = 18;
    private const int DetailsPlayItem = 0;
    private const int DetailsSettingsItem = 1;
    private const int DetailsBackItem = 2;
    private const double BootDurationSeconds = 3.45;

    private static readonly SDL.FRect BiosButton =
        new() { X = 610, Y = 520, W = 700, H = 112 };
    private static readonly SDL.FRect ContinueButton =
        new() { X = 770, Y = 680, W = 380, H = 80 };
    private static readonly SDL.FRect SettingsButton =
        new() { X = 1690, Y = 42, W = 170, H = 56 };
    private static readonly SDL.FRect DetailsPlayButton =
        new() { X = 610, Y = 720, W = 330, H = 82 };
    private static readonly SDL.FRect DetailsSettingsButton =
        new() { X = 962, Y = 720, W = 330, H = 82 };
    private static readonly SDL.FRect DetailsBackButton =
        new() { X = 1314, Y = 720, W = 220, H = 82 };
    private static readonly ControllerButton[] MappingTargets =
    [
        ControllerButton.Cross,
        ControllerButton.Circle,
        ControllerButton.Square,
        ControllerButton.Triangle,
        ControllerButton.L1,
        ControllerButton.R1,
        ControllerButton.L2,
        ControllerButton.R2,
        ControllerButton.Select,
        ControllerButton.Start,
        ControllerButton.L3,
        ControllerButton.R3,
        ControllerButton.Up,
        ControllerButton.Right,
        ControllerButton.Down,
        ControllerButton.Left,
    ];

    private readonly nint _window;
    private readonly nint _renderer;
    private readonly SdlFrontendHost _host;
    private readonly SDL.DialogFileCallback _dialogCallback;
    private readonly ConcurrentQueue<DialogResult> _dialogResults = new();
    private readonly ConcurrentQueue<MetadataResult> _metadataResults = new();
    private readonly ConcurrentQueue<UpdateCheckResult> _updateResults = new();
    private readonly SdlUiInput _uiInput = new();
    private readonly UiScreenNavigator _screens;
    private readonly UiFocusNavigator _setupFocus;
    private readonly UiFocusNavigator _settingsFocus = new(SettingsItemCount);
    private readonly UiFocusNavigator _detailsFocus = new(3);
    private readonly UiFocusNavigator _mappingFocus =
        new(MappingTargets.Length);
    private readonly AnimatedValue _screenFade = new(0);
    private readonly AnimatedValue _carouselPosition = new(0);
    private readonly AnimatedValue _launchFade = new(0);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly FrontendSettingsStore _settingsStore = new();
    private readonly GameLibraryScanner _libraryScanner = new();
    private readonly GameLibraryCatalogStore _catalogStore = new();
    private readonly GameActivityStore _activityStore = new();
    private readonly GameIdentityService _identity = new();
    private readonly CoverArtService _coverArt = new();
    private readonly UpdateService _updateService = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly SdlUiAudio _audio = new();
    private readonly SdlTextureCache _textures;
    private readonly SdlTextRenderer _text;
    private readonly SdlTexture _sadcatOpen;
    private readonly SdlTexture _sadcatClosed;
    private readonly SdlTexture _background;
    private readonly SdlTexture _coverPlaceholder;
    private readonly List<GameLibraryEntry> _games = [];
    private readonly HashSet<string> _metadataRequests =
        new(StringComparer.OrdinalIgnoreCase);

    private FrontendSettings _settings;
    private SDL.DialogFileFilter[]? _dialogFilters;
    private DialogTarget _pendingDialogTarget;
    private string? _biosPath;
    private string? _discPath;
    private string _status;
    private bool _dialogOpen;
    private bool _running = true;
    private bool _launchRequested;
    private bool _launchAfterDiscSelection;
    private bool _disposed;
    private TimeSpan _lastAnimationTime;
    private double _bootElapsedSeconds;
    private int _dashboardSelection;
    private GameActivityEntry? _selectedActivity;
    private UiScreenId _settingsReturnScreen = UiScreenId.Dashboard;
    private ControllerButton? _mappingCaptureTarget;
    private FrontendUpdateInfo? _updateInfo;
    private bool _checkingUpdates;

    private UiTheme Theme => UiTheme.Get(_settings.Theme);

    private UiColor White => Theme.TextPrimary;

    private UiColor Muted => Theme.TextSecondary;

    private UiColor Dim => Theme.Disabled;

    private UiColor Accent => Theme.Accent;

    public SdlLauncher(SdlFrontendHost host, bool showBootAnimation)
    {
        _host = host;
        _settings = _settingsStore.Load();
        _window = host.Window;
        _renderer = host.Renderer;
        host.SetTitle("SadPSX");
        host.ConfigurePresentation(
            CanvasWidth,
            CanvasHeight,
            SDL.RendererLogicalPresentation.Letterbox);
        SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);
        SDL.SetRenderVSync(_renderer, 1);

        _textures = new SdlTextureCache(_renderer);
        _text = new SdlTextRenderer(_renderer);
        _sadcatOpen = _textures.Get(FrontendAssets.SadcatOpen);
        _sadcatClosed = _textures.Get(FrontendAssets.SadcatClosed);
        _background = _textures.Get(FrontendAssets.DefaultBackground);
        _coverPlaceholder = _textures.Get(FrontendAssets.CoverPlaceholder);
        _dialogCallback = HandleDialogResult;
        _biosPath = FindValidBios(_settings.BiosPath) ?? FindLocalBios();
        _setupFocus = new UiFocusNavigator(2, IsSetupItemEnabled);
        _status = _biosPath is null
            ? "A PlayStation BIOS is required once."
            : $"BIOS ready: {Path.GetFileName(_biosPath)}";
        RescanLibrary();
        if (_settings.CheckForUpdates)
            RequestUpdateCheck(silent: true);

        if (_biosPath is not null && _biosPath != _settings.BiosPath)
            UpdateSettings(_settings with { BiosPath = _biosPath });

        UiScreenId firstScreen = showBootAnimation && _settings.ShowBootAnimation
            ? UiScreenId.Boot
            : _biosPath is null
                ? UiScreenId.FirstRunSetup
                : UiScreenId.Dashboard;
        _screens = new UiScreenNavigator(firstScreen);
        _screenFade.SetTarget(1, TimeSpan.FromMilliseconds(420));
    }

    public FrontendOptions? Run()
    {
        while (_running)
        {
            ProcessEvents();
            ProcessDialogResults();
            ProcessMetadataResults();
            ProcessUpdateResults();
            UpdateAnimations();
            Render();
            SDL.Delay(1);
        }

        if (!_launchRequested || _biosPath is null)
            return null;

        return new FrontendOptions(
            _biosPath,
            _discPath,
            MemoryCardPath: null,
            DefaultInstructionBatchSize,
            StartPaused: false,
            FrameLimit: null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeDialogFilters();
        _updateCancellation.Cancel();
        _updateCancellation.Dispose();
        _updateService.Dispose();
        _audio.Dispose();
        _coverArt.Dispose();
        _uiInput.Dispose();
        _text.Dispose();
        _textures.Dispose();
        _disposed = true;
    }

    private void ProcessEvents()
    {
        while (SDL.PollEvent(out SDL.Event currentEvent))
        {
            SDL.ConvertEventToRenderCoordinates(_renderer, ref currentEvent);
            if (TryHandleMappingCapture(currentEvent))
                continue;
            if (_uiInput.TryMapEvent(currentEvent, out UiAction action))
            {
                HandleUiAction(action);
                continue;
            }

            switch ((SDL.EventType)currentEvent.Type)
            {
                case SDL.EventType.Quit:
                case SDL.EventType.WindowCloseRequested:
                    if (!_dialogOpen)
                        _running = false;
                    break;

                case SDL.EventType.KeyDown
                    when !currentEvent.Key.Repeat &&
                         currentEvent.Key.Scancode == SDL.Scancode.F11:
                    SetFullscreen(!_settings.Fullscreen);
                    break;

                case SDL.EventType.MouseMotion when !_dialogOpen:
                    HandleMouseMotion(currentEvent.Motion.X, currentEvent.Motion.Y);
                    break;

                case SDL.EventType.MouseButtonDown
                    when currentEvent.Button.Button == 1 && !_dialogOpen:
                    HandleMouseClick(currentEvent.Button.X, currentEvent.Button.Y);
                    break;
            }
        }
    }

    private void HandleUiAction(UiAction action)
    {
        if (_dialogOpen)
            return;

        if (_settings.UiSounds)
        {
            _audio.Play(action switch
            {
                UiAction.Confirm or UiAction.Menu => UiSound.Confirm,
                UiAction.Back => UiSound.Back,
                _ => UiSound.Navigate,
            });
        }

        switch (_screens.Current)
        {
            case UiScreenId.Boot:
                if (action is UiAction.Confirm or UiAction.Back or UiAction.Menu)
                    CompleteBoot();
                break;
            case UiScreenId.FirstRunSetup:
                HandleSetupAction(action);
                break;
            case UiScreenId.Dashboard:
                HandleDashboardAction(action);
                break;
            case UiScreenId.GameDetails:
                HandleGameDetailsAction(action);
                break;
            case UiScreenId.Settings:
                HandleSettingsAction(action);
                break;
            case UiScreenId.ControllerMapping:
                HandleControllerMappingAction(action);
                break;
        }
    }

    private void HandleSetupAction(UiAction action)
    {
        if (_setupFocus.Move(action))
            return;

        if (action == UiAction.Confirm)
        {
            if (_setupFocus.SelectedIndex == BiosItem)
                ShowBiosDialog();
            else
                OpenDashboard();
        }
        else if (action == UiAction.Back)
        {
            _running = false;
        }
    }

    private void HandleDashboardAction(UiAction action)
    {
        switch (action)
        {
            case UiAction.Left:
                MoveDashboardSelection(-1);
                break;
            case UiAction.Right:
                MoveDashboardSelection(1);
                break;
            case UiAction.Up:
            case UiAction.Menu:
                OpenSettings();
                break;
            case UiAction.Confirm:
                ActivateDashboardSelection();
                break;
            case UiAction.Back:
                _running = false;
                break;
        }
    }

    private void HandleSettingsAction(UiAction action)
    {
        if (action is UiAction.Up or UiAction.Down)
        {
            _settingsFocus.Move(action);
            return;
        }

        if (action == UiAction.Back)
        {
            CloseSettings();
            return;
        }

        if (action == UiAction.Left)
        {
            ChangeSetting(-1);
            return;
        }
        if (action is not (UiAction.Right or UiAction.Confirm))
            return;

        ChangeSetting(1);
    }

    private void ChangeSetting(int direction)
    {
        switch (_settingsFocus.SelectedIndex)
        {
            case SettingsFullscreenItem:
                SetFullscreen(!_settings.Fullscreen);
                break;
            case SettingsScalingItem:
                UpdateSettings(_settings with
                {
                    VideoScaling = CycleScaling(
                        _settings.VideoScaling,
                        direction),
                });
                _status = "Video scaling applies when the game starts.";
                break;
            case SettingsFilterItem:
                UpdateSettings(_settings with
                {
                    SmoothVideo = !_settings.SmoothVideo,
                });
                _status = "Video filtering applies when the game starts.";
                break;
            case SettingsAudioItem:
                UpdateSettings(_settings with
                {
                    AudioEnabled = !_settings.AudioEnabled,
                });
                _status = "Audio output applies when the game starts.";
                break;
            case SettingsVolumeItem:
                UpdateSettings(_settings with
                {
                    AudioVolume = Math.Clamp(
                        _settings.AudioVolume + (direction * 10),
                        0,
                        100),
                });
                _status = "Audio volume applies when the game starts.";
                break;
            case SettingsControllerItem:
                UpdateSettings(_settings with
                {
                    DefaultAnalogController =
                        !_settings.DefaultAnalogController,
                });
                _status = "Controller profile applies when the game starts.";
                break;
            case SettingsMappingItem:
                OpenControllerMapping();
                break;
            case SettingsBootItem:
                UpdateSettings(_settings with
                {
                    ShowBootAnimation = !_settings.ShowBootAnimation,
                });
                break;
            case SettingsCoversItem:
                UpdateSettings(_settings with
                {
                    DownloadCovers = !_settings.DownloadCovers,
                });
                if (_settings.DownloadCovers)
                    RequestMissingCovers();
                break;
            case SettingsSoundsItem:
                UpdateSettings(_settings with
                {
                    UiSounds = !_settings.UiSounds,
                });
                break;
            case SettingsThemeItem:
                UpdateSettings(_settings with
                {
                    Theme = CycleTheme(_settings.Theme, direction),
                });
                _status = $"Theme changed to {FormatTheme(_settings.Theme)}.";
                break;
            case SettingsWallpaperItem:
                UpdateSettings(_settings with
                {
                    Wallpaper = CycleWallpaper(
                        _settings.Wallpaper,
                        direction),
                });
                _status = $"Wallpaper changed to {FormatWallpaper(_settings.Wallpaper)}.";
                break;
            case SettingsCustomWallpaperItem:
                ShowWallpaperDialog();
                break;
            case SettingsParallaxItem:
                UpdateSettings(_settings with
                {
                    WallpaperParallax = !_settings.WallpaperParallax,
                });
                break;
            case SettingsUpdatesItem:
                if (direction < 0)
                {
                    UpdateSettings(_settings with
                    {
                        CheckForUpdates = !_settings.CheckForUpdates,
                    });
                    _status = _settings.CheckForUpdates
                        ? "Automatic update checks enabled."
                        : "Automatic update checks disabled.";
                }
                else if (_updateInfo is { IsUpdateAvailable: true } update)
                {
                    OpenReleasePage(update.ReleaseUrl);
                }
                else
                {
                    RequestUpdateCheck(silent: false);
                }
                break;
            case SettingsLibraryItem:
                ShowLibraryDialog();
                break;
            case SettingsRescanItem:
                RescanLibrary();
                _status = $"Library refreshed: {_games.Count} game(s).";
                break;
            case SettingsBackItem:
                CloseSettings();
                break;
        }
    }

    private void HandleControllerMappingAction(UiAction action)
    {
        if (action == UiAction.Back)
        {
            ReturnToSettings();
            return;
        }

        if (action == UiAction.Menu)
        {
            UpdateSettings(_settings with
            {
                ControllerMapping = GamepadMapping.Default,
            });
            _status = "Controller mapping restored to defaults.";
            return;
        }

        if (action is UiAction.Up or UiAction.Down)
        {
            _mappingFocus.Move(action);
            return;
        }

        if (action is UiAction.Left or UiAction.Right)
        {
            int offset = action == UiAction.Right ? 8 : -8;
            int target = _mappingFocus.SelectedIndex + offset;
            if (target is >= 0 and < 16)
                _mappingFocus.Select(target);
            return;
        }

        if (action == UiAction.Confirm)
        {
            _mappingCaptureTarget =
                MappingTargets[_mappingFocus.SelectedIndex];
            _status =
                $"Press a gamepad button for {_mappingCaptureTarget}.";
        }
    }

    private bool TryHandleMappingCapture(in SDL.Event currentEvent)
    {
        if (_screens.Current != UiScreenId.ControllerMapping ||
            _mappingCaptureTarget is not ControllerButton target)
        {
            return false;
        }

        switch ((SDL.EventType)currentEvent.Type)
        {
            case SDL.EventType.KeyDown
                when currentEvent.Key.Scancode == SDL.Scancode.Escape:
                _mappingCaptureTarget = null;
                _status = "Button capture canceled.";
                return true;

            case SDL.EventType.GamepadButtonDown:
                if (GamepadMapping.TryFromButton(
                        (SDL.GamepadButton)currentEvent.GButton.Button,
                        out GamepadBinding buttonBinding))
                {
                    ApplyMapping(target, buttonBinding);
                }
                return true;

            case SDL.EventType.GamepadAxisMotion
                when currentEvent.GAxis.Value >= 16_000:
                GamepadBinding? triggerBinding =
                    (SDL.GamepadAxis)currentEvent.GAxis.Axis switch
                    {
                        SDL.GamepadAxis.LeftTrigger =>
                            GamepadBinding.LeftTrigger,
                        SDL.GamepadAxis.RightTrigger =>
                            GamepadBinding.RightTrigger,
                        _ => null,
                    };
                if (triggerBinding is GamepadBinding binding)
                    ApplyMapping(target, binding);
                return true;

            default:
                return false;
        }
    }

    private void ApplyMapping(
        ControllerButton target,
        GamepadBinding binding)
    {
        GamepadMapping mapping = _settings.EffectiveControllerMapping.Rebind(
            target,
            binding);
        UpdateSettings(_settings with { ControllerMapping = mapping });
        _mappingCaptureTarget = null;
        _status =
            $"{target} mapped to {GamepadMapping.GetDisplayName(binding)}.";
    }

    private void HandleGameDetailsAction(UiAction action)
    {
        if (_detailsFocus.Move(action))
            return;

        if (action == UiAction.Back)
        {
            OpenDashboard();
            return;
        }

        if (action != UiAction.Confirm ||
            _dashboardSelection >= _games.Count)
        {
            return;
        }

        switch (_detailsFocus.SelectedIndex)
        {
            case DetailsPlayItem:
                RequestLaunch(_games[_dashboardSelection].DiscPath);
                break;
            case DetailsSettingsItem:
                OpenSettings();
                break;
            case DetailsBackItem:
                OpenDashboard();
                break;
        }
    }

    private void HandleMouseMotion(float x, float y)
    {
        if (_screens.Current == UiScreenId.FirstRunSetup)
        {
            if (Contains(BiosButton, x, y))
                _setupFocus.Select(BiosItem);
            else if (Contains(ContinueButton, x, y))
                _setupFocus.Select(ContinueItem);
            return;
        }

        if (_screens.Current == UiScreenId.Settings)
        {
            int setting = FindSettingsItem(x, y);
            if (setting >= 0)
                _settingsFocus.Select(setting);
            return;
        }

        if (_screens.Current == UiScreenId.ControllerMapping)
        {
            int mappingItem = FindMappingItem(x, y);
            if (mappingItem >= 0)
                _mappingFocus.Select(mappingItem);
            return;
        }

        if (_screens.Current == UiScreenId.GameDetails)
        {
            int detailsItem = FindDetailsItem(x, y);
            if (detailsItem >= 0)
                _detailsFocus.Select(detailsItem);
            return;
        }

        if (_screens.Current != UiScreenId.Dashboard)
            return;

        if (Contains(SettingsButton, x, y))
            return;
        int item = FindDashboardItem(x, y);
        if (item >= 0 && item != _dashboardSelection)
            SelectDashboardItem(item);
    }

    private void HandleMouseClick(float x, float y)
    {
        if (_screens.Current == UiScreenId.Boot)
        {
            CompleteBoot();
            return;
        }

        if (_screens.Current == UiScreenId.FirstRunSetup)
        {
            if (Contains(BiosButton, x, y))
                ShowBiosDialog();
            else if (Contains(ContinueButton, x, y) && _biosPath is not null)
                OpenDashboard();
            return;
        }

        if (_screens.Current == UiScreenId.Settings)
        {
            int setting = FindSettingsItem(x, y);
            if (setting >= 0)
            {
                _settingsFocus.Select(setting);
                HandleSettingsAction(UiAction.Confirm);
            }
            return;
        }

        if (_screens.Current == UiScreenId.ControllerMapping)
        {
            int mappingItem = FindMappingItem(x, y);
            if (mappingItem >= 0)
            {
                _mappingFocus.Select(mappingItem);
                HandleControllerMappingAction(UiAction.Confirm);
            }
            return;
        }

        if (_screens.Current == UiScreenId.GameDetails)
        {
            int detailsItem = FindDetailsItem(x, y);
            if (detailsItem >= 0)
            {
                _detailsFocus.Select(detailsItem);
                HandleGameDetailsAction(UiAction.Confirm);
            }
            return;
        }

        if (_screens.Current != UiScreenId.Dashboard)
            return;
        if (Contains(SettingsButton, x, y))
        {
            OpenSettings();
            return;
        }

        int item = FindDashboardItem(x, y);
        if (item < 0)
            return;
        if (item == _dashboardSelection)
            ActivateDashboardSelection();
        else
            SelectDashboardItem(item);
    }

    private void CompleteBoot()
    {
        if (_screens.Current != UiScreenId.Boot)
            return;
        if (_biosPath is null)
            OpenSetup();
        else
            OpenDashboard();
    }

    private void OpenSetup()
    {
        _screens.Reset(UiScreenId.FirstRunSetup);
        _setupFocus.Refresh();
        BeginScreenTransition();
    }

    private void OpenDashboard()
    {
        if (_biosPath is null)
        {
            _status = "Select a valid BIOS before continuing.";
            OpenSetup();
            return;
        }

        _screens.Reset(UiScreenId.Dashboard);
        _dashboardSelection = Math.Clamp(
            _dashboardSelection,
            0,
            DashboardItemCount - 1);
        _carouselPosition.SnapTo(_dashboardSelection);
        RefreshSelectedActivity();
        BeginScreenTransition();
    }

    private void OpenSettings()
    {
        _settingsReturnScreen = _screens.Current;
        _screens.Reset(UiScreenId.Settings);
        BeginScreenTransition();
    }

    private void OpenControllerMapping()
    {
        _mappingCaptureTarget = null;
        _screens.Reset(UiScreenId.ControllerMapping);
        _status = "Choose a PlayStation button to remap.";
        BeginScreenTransition();
    }

    private void ReturnToSettings()
    {
        _mappingCaptureTarget = null;
        _screens.Reset(UiScreenId.Settings);
        BeginScreenTransition();
    }

    private void CloseSettings()
    {
        if (_settingsReturnScreen == UiScreenId.GameDetails)
            OpenGameDetails();
        else
            OpenDashboard();
    }

    private void OpenGameDetails()
    {
        if (_dashboardSelection >= _games.Count)
            return;
        _selectedActivity = _activityStore.Get(
            _games[_dashboardSelection].DiscPath,
            _games[_dashboardSelection].Serial);
        _detailsFocus.Select(DetailsPlayItem);
        _screens.Reset(UiScreenId.GameDetails);
        BeginScreenTransition();
    }

    private void MoveDashboardSelection(int direction)
    {
        int selection = (_dashboardSelection + direction) % DashboardItemCount;
        if (selection < 0)
            selection += DashboardItemCount;
        SelectDashboardItem(selection);
    }

    private void SelectDashboardItem(int item)
    {
        _dashboardSelection = item;
        RefreshSelectedActivity();
        _carouselPosition.SetTarget(item, TimeSpan.FromMilliseconds(290));
    }

    private void ActivateDashboardSelection()
    {
        if (_dashboardSelection == _games.Count)
            ShowDiscDialog(launchAfterSelection: true);
        else
            OpenGameDetails();
    }

    private int FindDashboardItem(float x, float y)
    {
        for (int index = 0; index < DashboardItemCount; index++)
        {
            if (Contains(GetDashboardCard(index), x, y))
                return index;
        }
        return -1;
    }

    private static int FindSettingsItem(float x, float y)
    {
        if (x < 300 || x > 1620)
            return -1;
        int item = (int)((y - 232) / 39);
        return item is >= 0 and < SettingsItemCount ? item : -1;
    }

    private static int FindDetailsItem(float x, float y)
    {
        if (Contains(DetailsPlayButton, x, y))
            return DetailsPlayItem;
        if (Contains(DetailsSettingsButton, x, y))
            return DetailsSettingsItem;
        if (Contains(DetailsBackButton, x, y))
            return DetailsBackItem;
        return -1;
    }

    private static int FindMappingItem(float x, float y)
    {
        for (int index = 0; index < MappingTargets.Length; index++)
        {
            if (Contains(GetMappingRectangle(index), x, y))
                return index;
        }

        return -1;
    }

    private void ShowBiosDialog()
    {
        ShowFileDialog(
            DialogTarget.Bios,
            [
                new SDL.DialogFileFilter("PlayStation BIOS", "bin;rom"),
                new SDL.DialogFileFilter("All files", "*"),
            ]);
    }

    private void ShowDiscDialog(bool launchAfterSelection)
    {
        _launchAfterDiscSelection = launchAfterSelection;
        ShowFileDialog(
            DialogTarget.Disc,
            [
                new SDL.DialogFileFilter("PlayStation disc image", "cue;bin"),
                new SDL.DialogFileFilter("All files", "*"),
            ]);
    }

    private void ShowLibraryDialog()
    {
        _pendingDialogTarget = DialogTarget.Library;
        _dialogOpen = true;
        _status = "Waiting for library folder selection...";
        SDL.ShowOpenFolderDialog(
            _dialogCallback,
            nint.Zero,
            _window,
            GetLibraryPath(),
            allowMany: false);
    }

    private void ShowWallpaperDialog()
    {
        ShowFileDialog(
            DialogTarget.Wallpaper,
            [
                new SDL.DialogFileFilter(
                    "Wallpaper image",
                    "png;jpg;jpeg;webp"),
                new SDL.DialogFileFilter("All files", "*"),
            ]);
    }

    private void ShowFileDialog(
        DialogTarget target,
        SDL.DialogFileFilter[] filters)
    {
        _pendingDialogTarget = target;
        _dialogFilters = filters;
        _dialogOpen = true;
        _status = target switch
        {
            DialogTarget.Bios => "Waiting for BIOS selection...",
            DialogTarget.Wallpaper => "Waiting for wallpaper selection...",
            _ => "Waiting for game selection...",
        };
        SDL.ShowOpenFileDialog(
            _dialogCallback,
            nint.Zero,
            _window,
            filters,
            filters.Length,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            allowMany: false);
    }

    private void HandleDialogResult(nint userdata, nint fileList, int filter)
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
                _launchAfterDiscSelection = false;
                continue;
            }

            string path = Path.GetFullPath(result.Path);
            if (result.Target == DialogTarget.Library)
            {
                SelectLibrary(path);
                continue;
            }
            if (!File.Exists(path))
            {
                _status = "The selected file does not exist.";
                _launchAfterDiscSelection = false;
                continue;
            }
            switch (result.Target)
            {
                case DialogTarget.Bios:
                    SelectBios(path);
                    break;
                case DialogTarget.Wallpaper:
                    SelectWallpaper(path);
                    break;
                default:
                    SelectDisc(path);
                    break;
            }
        }
    }

    private void SelectBios(string path)
    {
        if (new FileInfo(path).Length != 512 * 1024)
        {
            _status = "Invalid BIOS: expected a 512 KiB file.";
            return;
        }

        _biosPath = path;
        _status = $"BIOS ready: {Path.GetFileName(path)}";
        _setupFocus.Refresh();
        _setupFocus.Select(ContinueItem);
        UpdateSettings(_settings with { BiosPath = path });
    }

    private void SelectDisc(string path)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            _status = "Invalid game image: select a .cue or .bin file.";
            _launchAfterDiscSelection = false;
            return;
        }

        _discPath = path;
        UpdateSettings(_settings with { LastDiscPath = path });
        if (!_games.Any(game => string.Equals(
                game.DiscPath,
                path,
                StringComparison.OrdinalIgnoreCase)))
        {
            GameLibraryEntry game = _identity.Identify(
                FormatGameName(path),
                path);
            _games.Add(game);
            if (_settings.DownloadCovers)
            {
                _coverArt.Request(game);
                RequestMetadata(game);
            }
        }

        if (_launchAfterDiscSelection)
            RequestLaunch(path);
        _launchAfterDiscSelection = false;
    }

    private void SelectLibrary(string path)
    {
        if (!Directory.Exists(path))
        {
            _status = "The selected library folder does not exist.";
            return;
        }

        UpdateSettings(_settings with { LibraryPath = path });
        RescanLibrary();
        _status = $"Library changed: {_games.Count} game(s) found.";
    }

    private void SelectWallpaper(string path)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            _status = "Unsupported wallpaper format.";
            return;
        }

        try
        {
            _ = _textures.Get(path);
        }
        catch (InvalidOperationException)
        {
            _status = "Could not load the selected wallpaper.";
            return;
        }

        UpdateSettings(_settings with
        {
            CustomWallpaperPath = path,
            Wallpaper = FrontendWallpaperMode.Custom,
        });
        _status = $"Wallpaper ready: {Path.GetFileName(path)}";
    }

    private void RequestLaunch(string discPath)
    {
        if (_biosPath is null)
        {
            OpenSetup();
            return;
        }

        _discPath = discPath;
        UpdateSettings(_settings with { LastDiscPath = discPath });
        _launchRequested = true;
        _launchFade.SetTarget(1, TimeSpan.FromMilliseconds(360));
    }

    private void RescanLibrary()
    {
        _games.Clear();
        IReadOnlyList<GameLibraryEntry> scanned =
            _libraryScanner.Scan(GetLibraryPath());
        _games.AddRange(GameLibraryCatalogStore.Merge(
            scanned,
            _catalogStore.Load()));
        IncludeRememberedDisc();
        int rememberedIndex = _games.FindIndex(game => string.Equals(
            game.DiscPath,
            _settings.LastDiscPath,
            StringComparison.OrdinalIgnoreCase));
        if (rememberedIndex >= 0)
            _dashboardSelection = rememberedIndex;
        _dashboardSelection = Math.Clamp(
            _dashboardSelection,
            0,
            DashboardItemCount - 1);
        _carouselPosition.SnapTo(_dashboardSelection);
        RefreshSelectedActivity();
        if (_settings.DownloadCovers)
            RequestMissingCovers();
        SaveCatalog();
    }

    private void RequestMissingCovers()
    {
        foreach (GameLibraryEntry game in _games)
        {
            _coverArt.Request(game);
            RequestMetadata(game);
        }
    }

    private void RequestMetadata(GameLibraryEntry game)
    {
        if (game.Serial is null ||
            game.CatalogName is not null ||
            !_metadataRequests.Add(game.DiscPath))
        {
            return;
        }

        _ = ResolveMetadataAsync(game.DiscPath, game.Serial);
    }

    private async Task ResolveMetadataAsync(string discPath, string serial)
    {
        try
        {
            LibretroGameMetadata? metadata =
                await _coverArt.ResolveMetadataAsync(serial)
                    .ConfigureAwait(false);
            if (metadata is not null)
                _metadataResults.Enqueue(new MetadataResult(discPath, metadata));
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ProcessMetadataResults()
    {
        bool changed = false;
        while (_metadataResults.TryDequeue(out MetadataResult result))
        {
            int index = _games.FindIndex(game => string.Equals(
                game.DiscPath,
                result.DiscPath,
                StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                continue;

            GameLibraryEntry game = _games[index];
            _games[index] = game with
            {
                CatalogName = result.Metadata.Name,
                Region = result.Metadata.Region,
                DiscNumber = result.Metadata.DiscNumber ?? game.DiscNumber,
                Revision = result.Metadata.Revision,
            };
            _coverArt.Request(_games[index]);
            changed = true;
        }

        if (changed)
            SaveCatalog();
    }

    private void RequestUpdateCheck(bool silent)
    {
        if (_checkingUpdates)
            return;

        _checkingUpdates = true;
        if (!silent)
            _status = "Checking for SadPSX updates...";
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            FrontendUpdateInfo? update = await _updateService.CheckAsync(
                _updateCancellation.Token).ConfigureAwait(false);
            _updateResults.Enqueue(new UpdateCheckResult(update, null));
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException exception)
        {
            _updateResults.Enqueue(new UpdateCheckResult(null, exception.Message));
        }
        catch (JsonException exception)
        {
            _updateResults.Enqueue(new UpdateCheckResult(null, exception.Message));
        }
    }

    private void ProcessUpdateResults()
    {
        while (_updateResults.TryDequeue(out UpdateCheckResult result))
        {
            _checkingUpdates = false;
            if (result.Error is not null)
            {
                _status = "Update check unavailable. You can retry later.";
                continue;
            }

            _updateInfo = result.Update;
            _status = result.Update switch
            {
                { IsUpdateAvailable: true } update =>
                    $"SadPSX {update.Tag} is available.",
                not null => "SadPSX is up to date.",
                _ => "No published SadPSX release was found.",
            };
        }
    }

    private void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
            _status = "Release page opened in your browser.";
        }
        catch (InvalidOperationException)
        {
            _status = "Could not open the release page.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _status = "Could not open the release page.";
        }
    }

    private void SaveCatalog()
    {
        try
        {
            _catalogStore.Save(_games);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RefreshSelectedActivity()
    {
        _selectedActivity = _dashboardSelection < _games.Count
            ? _activityStore.Get(
                _games[_dashboardSelection].DiscPath,
                _games[_dashboardSelection].Serial)
            : null;
    }

    private string GetDashboardActivityLabel()
    {
        if (_selectedActivity is not { Sessions: > 0 } activity)
            return "Start a new PlayStation session";
        return $"Continue  •  {FormatPlayTime(activity.TotalPlayed)} played";
    }

    private void UpdateAnimations()
    {
        TimeSpan now = _clock.Elapsed;
        TimeSpan elapsed = now - _lastAnimationTime;
        _lastAnimationTime = now;
        _screenFade.Advance(elapsed);
        _carouselPosition.Advance(elapsed);
        _launchFade.Advance(elapsed);
        if (_launchRequested && _launchFade.Value >= 1)
        {
            _running = false;
            return;
        }

        if (_screens.Current != UiScreenId.Boot)
            return;
        _bootElapsedSeconds += elapsed.TotalSeconds;
        if (_bootElapsedSeconds >= BootDurationSeconds)
            CompleteBoot();
    }

    private void Render()
    {
        SetColor(new UiColor(0, 0, 0));
        EnsureSuccess(SDL.RenderClear(_renderer), "clear SadPSX shell");
        switch (_screens.Current)
        {
            case UiScreenId.Boot:
                RenderBoot();
                break;
            case UiScreenId.FirstRunSetup:
                RenderSetup();
                break;
            case UiScreenId.Dashboard:
                RenderDashboard();
                break;
            case UiScreenId.GameDetails:
                RenderGameDetails();
                break;
            case UiScreenId.Settings:
                RenderSettings();
                break;
            case UiScreenId.ControllerMapping:
                RenderControllerMapping();
                break;
        }
        if (_launchFade.Value > 0)
        {
            DrawFilledRect(
                new SDL.FRect
                {
                    X = 0,
                    Y = 0,
                    W = CanvasWidth,
                    H = CanvasHeight,
                },
                new UiColor(0, 0, 0, (byte)(255 * _launchFade.Value)));
        }
        EnsureSuccess(SDL.RenderPresent(_renderer), "present SadPSX shell");
    }

    private void RenderBoot()
    {
        if (_bootElapsedSeconds < 0.42)
            return;

        float logoAlpha = (float)Math.Clamp(
            (_bootElapsedSeconds - 0.42) / 0.38,
            0,
            1);
        bool eyesClosed = _bootElapsedSeconds is >= 1.08 and <= 1.24;
        SdlTexture cat = eyesClosed ? _sadcatClosed : _sadcatOpen;
        var catRect = new SDL.FRect { X = 740, Y = 106, W = 440, H = 440 };
        cat.Render(_renderer, in catRect, (byte)(255 * logoAlpha));
        DrawCenteredText(
            "SadPSX",
            572,
            58,
            White,
            FontWeight.SemiBold,
            logoAlpha);
        DrawCenteredText(
            "INITIALIZING",
            650,
            17,
            Muted,
            FontWeight.Medium,
            logoAlpha);

        string[] stages =
        [
            "CPU ........................ OK",
            "GPU ........................ OK",
            "SPU ........................ OK",
            "CD-ROM ..................... OK",
            "MEMORY CARDS ............... OK",
            "SETTINGS ................... LOADED",
            "WELCOME.",
        ];
        int count = Math.Clamp(
            (int)((_bootElapsedSeconds - 1.30) / 0.25) + 1,
            0,
            stages.Length);
        for (int index = 0; index < count; index++)
        {
            DrawCenteredText(
                stages[index],
                716 + (index * 34),
                17,
                index == count - 1 ? White : Muted,
                FontWeight.Medium);
        }
        DrawCenteredText(
            "PRESS CROSS OR ENTER TO SKIP",
            1030,
            14,
            Dim,
            FontWeight.Medium);
    }

    private void RenderSetup()
    {
        DrawBackground();
        DrawOverlay(new UiColor(4, 5, 9, 208));
        DrawBrandHeader(showSettings: false);
        DrawCenteredText(
            "WELCOME TO SADPSX",
            324,
            44,
            White,
            FontWeight.SemiBold);
        DrawCenteredText(
            "Choose your own PlayStation BIOS to start your library.",
            390,
            22,
            Muted);
        DrawSetupButton(
            BiosButton,
            "SELECT PLAYSTATION BIOS",
            _biosPath is null
                ? "Required - 512 KiB"
                : Path.GetFileName(_biosPath),
            BiosItem,
            enabled: true);
        DrawSetupButton(
            ContinueButton,
            "CONTINUE",
            string.Empty,
            ContinueItem,
            enabled: _biosPath is not null);
        DrawCenteredText(_status, 850, 18, Muted);
        DrawCenteredText(
            "D-PAD / LEFT STICK   NAVIGATE     CROSS / ENTER   CONFIRM",
            1018,
            15,
            Dim,
            FontWeight.Medium);
    }

    private void RenderDashboard()
    {
        DrawDashboardBackground();
        DrawBrandHeader(showSettings: true);

        string eyebrow = _dashboardSelection < _games.Count
            ? "PLAYSTATION  •  READY TO PLAY"
            : "YOUR LIBRARY";
        string title = _dashboardSelection < _games.Count
            ? _games[_dashboardSelection].Title
            : "Add a game";
        DrawText(eyebrow, 96, 190, 16, Muted, FontWeight.Medium);
        DrawText(title, 92, 226, 56, White, FontWeight.SemiBold);
        DrawText(
            _dashboardSelection < _games.Count
                ? GetDashboardActivityLabel()
                : "Choose a CUE or BIN image from your collection",
            96,
            302,
            22,
            Muted);

        for (int index = 0; index < DashboardItemCount; index++)
        {
            SDL.FRect card = GetDashboardCard(index);
            if (card.X + card.W < 0 || card.X > CanvasWidth)
                continue;
            DrawGameCard(index, card);
        }

        DrawText(
            _dashboardSelection < _games.Count ? "OPEN" : "BROWSE FILES",
            96,
            970,
            19,
            White,
            FontWeight.SemiBold);
        DrawText("BACK", 266, 970, 19, Muted, FontWeight.Medium);
        DrawRightAlignedText(
            $"{_games.Count} GAME{(_games.Count == 1 ? string.Empty : "S")}",
            1824,
            974,
            15,
            Dim,
            FontWeight.Medium);
    }

    private void RenderGameDetails()
    {
        if (_dashboardSelection >= _games.Count)
        {
            RenderDashboard();
            return;
        }

        GameLibraryEntry game = _games[_dashboardSelection];
        GameActivityEntry activity = _selectedActivity ??
            new GameActivityEntry(game.DiscPath);
        DrawDashboardBackground();
        DrawOverlay(new UiColor(2, 3, 8, 88));
        DrawBrandHeader(showSettings: false);

        var coverRectangle = new SDL.FRect
        {
            X = 108,
            Y = 196,
            W = 390,
            H = 552,
        };
        GetCoverTexture(game).RenderCover(
            _renderer,
            in coverRectangle,
            FadeAlpha(255));
        DrawRect(coverRectangle, new UiColor(255, 255, 255, 110));

        DrawText("PLAYSTATION LIBRARY", 610, 188, 16, Muted, FontWeight.Medium);
        DrawText(
            TrimToWidth(game.Title, 42),
            604,
            224,
            54,
            White,
            FontWeight.SemiBold);
        DrawText(GetGameSubtitle(game), 610, 302, 17, Muted, FontWeight.Medium);

        DrawActivityPanel(
            "LAST PLAYED",
            FormatLastPlayed(activity.LastPlayedUtc),
            610);
        DrawActivityPanel(
            "TOTAL TIME",
            FormatPlayTime(activity.TotalPlayed),
            920);
        DrawActivityPanel(
            "SESSIONS",
            activity.Sessions.ToString(),
            1230);

        DrawText("DISC IMAGE", 610, 544, 14, Dim, FontWeight.Medium);
        DrawText(
            TrimToWidth(Path.GetFileName(game.DiscPath), 72),
            610,
            572,
            18,
            Muted);
        DrawText("MEMORY CARD", 610, 626, 14, Dim, FontWeight.Medium);
        DrawText(
            "Memory Card 1  •  Ready",
            610,
            654,
            18,
            Muted);

        DrawDetailsButton(
            DetailsPlayButton,
            "PLAY",
            DetailsPlayItem,
            primary: true);
        DrawDetailsButton(
            DetailsSettingsButton,
            "SETTINGS",
            DetailsSettingsItem,
            primary: false);
        DrawDetailsButton(
            DetailsBackButton,
            "BACK",
            DetailsBackItem,
            primary: false);

        DrawText(
            activity.Sessions > 0
                ? "Continue from your last session"
                : "Begin a new PlayStation session",
            610,
            842,
            21,
            White,
            FontWeight.Medium);
        DrawText(
            "Your progress remains on Memory Card 1.",
            610,
            880,
            18,
            Muted);
        DrawCenteredText(
            "D-PAD / LEFT STICK   NAVIGATE     CROSS / ENTER   CONFIRM     CIRCLE / ESC   BACK",
            1018,
            15,
            Dim,
            FontWeight.Medium);
    }

    private void DrawActivityPanel(string label, string value, float x)
    {
        var panel = new SDL.FRect { X = x, Y = 374, W = 278, H = 112 };
        DrawFilledRect(panel, new UiColor(255, 255, 255, 17));
        DrawText(label, x + 24, 396, 13, Dim, FontWeight.Medium);
        DrawText(value, x + 24, 430, 24, White, FontWeight.SemiBold);
    }

    private void DrawDetailsButton(
        SDL.FRect rectangle,
        string label,
        int item,
        bool primary)
    {
        bool selected = _detailsFocus.SelectedIndex == item;
        DrawFilledRect(
            rectangle,
            primary
                ? Accent.WithAlpha(selected ? (byte)255 : (byte)210)
                : new UiColor(255, 255, 255, selected ? (byte)42 : (byte)18));
        DrawRect(
            rectangle,
            selected
                ? new UiColor(255, 255, 255, 230)
                : new UiColor(255, 255, 255, 62));
        DrawCenteredTextAt(
            label,
            rectangle.X + (rectangle.W / 2),
            rectangle.Y + 29,
            17,
            White,
            FontWeight.SemiBold);
    }

    private void RenderSettings()
    {
        DrawBackground();
        DrawOverlay(new UiColor(3, 4, 8, 220));
        DrawBrandHeader(showSettings: false);
        DrawText("Settings", 220, 126, 48, White, FontWeight.SemiBold);
        DrawText(
            "Video, audio and controls remain clear and predictable.",
            224,
            184,
            19,
            Muted);

        string[] labels =
        [
            "Fullscreen",
            "Display scaling",
            "Texture filtering",
            "Audio output",
            "Output volume",
            "Controller profile",
            "Button mapping",
            "Boot animation",
            "Automatic cover downloads",
            "Interface sounds",
            "Interface theme",
            "Dashboard wallpaper",
            "Choose custom wallpaper",
            "Wallpaper motion",
            "SadPSX updates",
            "Game library folder",
            "Rescan library",
            "Back to dashboard",
        ];
        string[] values =
        [
            OnOff(_settings.Fullscreen),
            FormatScaling(_settings.VideoScaling),
            _settings.SmoothVideo ? "SMOOTH" : "PIXEL PERFECT",
            OnOff(_settings.AudioEnabled),
            $"{_settings.AudioVolume}%",
            _settings.DefaultAnalogController ? "DUALSHOCK" : "DIGITAL",
            _settings.EffectiveControllerMapping == GamepadMapping.Default
                ? "DEFAULT"
                : "CUSTOM",
            OnOff(_settings.ShowBootAnimation),
            OnOff(_settings.DownloadCovers),
            OnOff(_settings.UiSounds),
            FormatTheme(_settings.Theme),
            FormatWallpaper(_settings.Wallpaper),
            _settings.CustomWallpaperPath is null
                ? "NOT SELECTED"
                : TrimToWidth(
                    Path.GetFileName(_settings.CustomWallpaperPath),
                    30),
            OnOff(_settings.WallpaperParallax),
            GetUpdateLabel(),
            TrimToWidth(GetLibraryPath(), 54),
            $"{_games.Count} game(s)",
            string.Empty,
        ];
        string[] categories =
        [
            "VIDEO", string.Empty, string.Empty,
            "AUDIO", string.Empty,
            "CONTROLS", string.Empty,
            "INTERFACE", string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty,
            "UPDATES",
            "LIBRARY", string.Empty,
            "SYSTEM",
        ];

        for (int index = 0; index < labels.Length; index++)
        {
            float y = 232 + (index * 39);
            bool selected = _settingsFocus.SelectedIndex == index;
            if (selected)
            {
                DrawFilledRect(
                    new SDL.FRect { X = 300, Y = y - 4, W = 1320, H = 35 },
                    new UiColor(255, 255, 255, 24));
                DrawFilledRect(
                    new SDL.FRect { X = 300, Y = y - 4, W = 5, H = 35 },
                    Accent);
            }
            if (categories[index].Length > 0)
            {
                DrawText(
                    categories[index],
                    330,
                    y + 4,
                    12,
                    selected ? Accent : Dim,
                    FontWeight.SemiBold);
            }
            DrawText(
                labels[index],
                520,
                y,
                17,
                selected ? White : Muted,
                selected ? FontWeight.SemiBold : FontWeight.Regular);
            if (values[index].Length > 0)
            {
                DrawRightAlignedText(
                    values[index],
                    1570,
                    y + 1,
                    15,
                    selected ? White : Dim,
                    FontWeight.Medium);
            }
        }
        DrawCenteredText(_status, 960, 15, Muted, FontWeight.Medium);
        DrawCenteredText(
            "UP / DOWN   NAVIGATE     LEFT / RIGHT   ADJUST     CROSS / ENTER   CHANGE     CIRCLE / ESC   BACK",
            1018,
            15,
            Dim,
            FontWeight.Medium);
    }

    private void RenderControllerMapping()
    {
        DrawBackground();
        DrawOverlay(new UiColor(3, 4, 8, 226));
        DrawBrandHeader(showSettings: false);
        DrawText("Controller Mapping", 180, 130, 48, White, FontWeight.SemiBold);
        DrawText(
            "Select a PlayStation command, then press the desired gamepad button.",
            184,
            190,
            19,
            Muted);

        GamepadMapping mapping = _settings.EffectiveControllerMapping;
        for (int index = 0; index < MappingTargets.Length; index++)
        {
            ControllerButton target = MappingTargets[index];
            SDL.FRect rectangle = GetMappingRectangle(index);
            bool selected = _mappingFocus.SelectedIndex == index;
            DrawFilledRect(
                rectangle,
                new UiColor(255, 255, 255, selected ? (byte)30 : (byte)12));
            if (selected)
            {
                DrawFilledRect(
                    new SDL.FRect
                    {
                        X = rectangle.X,
                        Y = rectangle.Y,
                        W = 5,
                        H = rectangle.H,
                    },
                    Accent);
            }
            DrawText(
                FormatControllerButton(target),
                rectangle.X + 24,
                rectangle.Y + 20,
                17,
                selected ? White : Muted,
                selected ? FontWeight.SemiBold : FontWeight.Regular);
            DrawRightAlignedText(
                GamepadMapping.GetDisplayName(mapping.GetBinding(target)),
                rectangle.X + rectangle.W - 24,
                rectangle.Y + 21,
                13,
                selected ? White : Dim,
                FontWeight.Medium);
        }

        DrawCenteredText(_status, 944, 16, Muted, FontWeight.Medium);
        DrawCenteredText(
            "D-PAD / STICK   NAVIGATE     CROSS / ENTER   REMAP     START   RESTORE DEFAULTS     CIRCLE / ESC   BACK",
            1018,
            14,
            Dim,
            FontWeight.Medium);

        if (_mappingCaptureTarget is ControllerButton captureTarget)
        {
            DrawOverlay(new UiColor(0, 0, 0, 188));
            var panel = new SDL.FRect { X = 560, Y = 382, W = 800, H = 300 };
            DrawFilledRect(panel, new UiColor(14, 16, 24, 252));
            DrawRect(panel, new UiColor(255, 255, 255, 100));
            DrawCenteredTextAt(
                "PRESS A GAMEPAD BUTTON",
                CanvasWidth / 2f,
                446,
                18,
                Accent,
                FontWeight.SemiBold);
            DrawCenteredTextAt(
                FormatControllerButton(captureTarget),
                CanvasWidth / 2f,
                500,
                42,
                White,
                FontWeight.SemiBold);
            DrawCenteredTextAt(
                "Triggers are supported. Press Esc to cancel.",
                CanvasWidth / 2f,
                584,
                17,
                Muted);
        }
    }

    private static SDL.FRect GetMappingRectangle(int index)
    {
        int column = index / 8;
        int row = index % 8;
        return new SDL.FRect
        {
            X = column == 0 ? 180 : 990,
            Y = 246 + (row * 82),
            W = 750,
            H = 64,
        };
    }

    private void DrawBrandHeader(bool showSettings)
    {
        var icon = new SDL.FRect { X = 62, Y = 22, W = 74, H = 74 };
        _sadcatOpen.Render(_renderer, in icon, FadeAlpha(255));
        DrawText("SadPSX", 146, 38, 30, White, FontWeight.SemiBold);
        DrawText("HOME", 344, 48, 15, White, FontWeight.SemiBold);
        DrawText("LIBRARY", 430, 48, 15, Muted, FontWeight.Medium);
        if (_updateInfo is { IsUpdateAvailable: true } update)
        {
            var updateBadge = new SDL.FRect
            {
                X = showSettings ? 1370 : 1538,
                Y = 34,
                W = 176,
                H = 48,
            };
            DrawFilledRect(updateBadge, Accent.WithAlpha(218));
            DrawCenteredTextAt(
                $"UPDATE {update.Tag.ToUpperInvariant()}",
                updateBadge.X + (updateBadge.W / 2),
                updateBadge.Y + 16,
                13,
                White,
                FontWeight.SemiBold);
        }
        DrawRightAlignedText(
            DateTime.Now.ToString("HH:mm"),
            showSettings ? 1660 : 1828,
            39,
            25,
            White,
            FontWeight.Medium);
        if (showSettings)
        {
            DrawFilledRect(SettingsButton, new UiColor(255, 255, 255, 17));
            DrawCenteredTextAt("SETTINGS", 1775, 60, 15, Muted, FontWeight.Medium);
        }
    }

    private void DrawDashboardBackground()
    {
        SdlTexture? background = GetWallpaperTexture(allowGameArtwork: true);

        DrawFilledRect(
            new SDL.FRect { X = 0, Y = 0, W = CanvasWidth, H = CanvasHeight },
            Theme.Background);

        float horizontalOffset = _settings.WallpaperParallax
            ? _carouselPosition.Value * -3 % 20
            : 0;
        float verticalOffset = _settings.WallpaperParallax
            ? MathF.Sin((float)_clock.Elapsed.TotalSeconds * 0.18f) * 3
            : 0;
        var destination = new SDL.FRect
        {
            X = -10 + horizontalOffset,
            Y = -10 + verticalOffset,
            W = CanvasWidth + 20,
            H = CanvasHeight + 20,
        };
        background?.RenderCover(_renderer, in destination, FadeAlpha(105));
        DrawOverlay(Theme.Background.WithAlpha(180));
        DrawVerticalGradient(
            Theme.Surface.WithAlpha(20),
            Theme.Background.WithAlpha(226));
    }

    private void DrawBackground()
    {
        DrawFilledRect(
            new SDL.FRect { X = 0, Y = 0, W = CanvasWidth, H = CanvasHeight },
            Theme.Background);
        SdlTexture? background = GetWallpaperTexture(allowGameArtwork: true);
        if (background is null)
            return;

        float offset = _settings.WallpaperParallax
            ? MathF.Sin((float)_clock.Elapsed.TotalSeconds * 0.14f) * 5
            : 0;
        var destination = new SDL.FRect
        {
            X = -8 + offset,
            Y = -8,
            W = CanvasWidth + 16,
            H = CanvasHeight + 16,
        };
        background.RenderCover(_renderer, in destination, FadeAlpha(255));
    }

    private SdlTexture? GetWallpaperTexture(bool allowGameArtwork)
    {
        if (_settings.Wallpaper == FrontendWallpaperMode.Solid)
            return null;

        if (_settings.Wallpaper == FrontendWallpaperMode.Custom &&
            _settings.CustomWallpaperPath is { } customPath &&
            File.Exists(customPath))
        {
            try
            {
                return _textures.Get(customPath);
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (_settings.Wallpaper == FrontendWallpaperMode.GameArtwork &&
            allowGameArtwork &&
            _dashboardSelection < _games.Count)
        {
            string? coverPath = _coverArt.GetCachedPath(
                _games[_dashboardSelection]);
            if (coverPath is not null)
            {
                try
                {
                    return _textures.Get(coverPath);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return _background;
    }

    private void DrawGameCard(int index, SDL.FRect card)
    {
        bool selected = index == _dashboardSelection;
        byte alpha = FadeAlpha(selected ? (byte)255 : (byte)170);
        DrawFilledRect(
            new SDL.FRect
            {
                X = card.X - (selected ? 10 : 0),
                Y = card.Y - (selected ? 10 : 0),
                W = card.W + (selected ? 20 : 0),
                H = card.H + (selected ? 20 : 0),
            },
            selected
                ? new UiColor(255, 255, 255, 38)
                : new UiColor(8, 10, 16, 150));

        if (index == _games.Count)
        {
            DrawFilledRect(card, new UiColor(19, 21, 29, alpha));
            DrawCenteredTextAt(
                "+",
                card.X + (card.W / 2),
                card.Y + (card.H / 2) - 54,
                selected ? 96 : 74,
                selected ? White : Muted,
                FontWeight.Regular);
            DrawCenteredTextAt(
                "ADD GAME",
                card.X + (card.W / 2),
                card.Y + card.H - 54,
                17,
                selected ? White : Muted,
                FontWeight.SemiBold);
        }
        else
        {
            SdlTexture cover = GetCoverTexture(_games[index]);
            cover.RenderCover(_renderer, in card, alpha);
            DrawFilledRect(
                new SDL.FRect
                {
                    X = card.X,
                    Y = card.Y + card.H - 116,
                    W = card.W,
                    H = 116,
                },
                new UiColor(0, 0, 0, selected ? (byte)182 : (byte)210));
            DrawText(
                TrimToWidth(_games[index].Title, 28),
                card.X + 24,
                card.Y + card.H - 82,
                selected ? 24 : 20,
                White,
                FontWeight.SemiBold);
            DrawText(
                GetGameSubtitle(_games[index]),
                card.X + 24,
                card.Y + card.H - 42,
                13,
                Muted,
                FontWeight.Medium);
        }

        if (selected)
            DrawRect(card, new UiColor(255, 255, 255, 210));
    }

    private SdlTexture GetCoverTexture(GameLibraryEntry game)
    {
        string? path = _coverArt.GetCachedPath(game);
        if (path is null)
            return _coverPlaceholder;
        try
        {
            return _textures.Get(path);
        }
        catch (InvalidOperationException)
        {
            return _coverPlaceholder;
        }
    }

    private void DrawSetupButton(
        SDL.FRect rectangle,
        string label,
        string detail,
        int itemIndex,
        bool enabled)
    {
        bool selected = _setupFocus.SelectedIndex == itemIndex && enabled;
        DrawFilledRect(
            rectangle,
            enabled
                ? selected
                    ? Accent.WithAlpha(226)
                    : new UiColor(255, 255, 255, 22)
                : new UiColor(68, 70, 79, 80));
        DrawRect(rectangle, selected ? White : new UiColor(130, 133, 143));
        DrawCenteredTextAt(
            label,
            rectangle.X + (rectangle.W / 2),
            rectangle.Y + (detail.Length == 0 ? 26 : 25),
            21,
            enabled ? White : Dim,
            FontWeight.SemiBold);
        if (detail.Length > 0)
        {
            DrawCenteredTextAt(
                TrimToWidth(detail, 54),
                rectangle.X + (rectangle.W / 2),
                rectangle.Y + 68,
                16,
                selected ? White : Muted);
        }
    }

    private SDL.FRect GetDashboardCard(int index)
    {
        const float selectedWidth = 344;
        const float selectedHeight = 484;
        const float spacing = 382;
        float distance = index - _carouselPosition.Value;
        float depth = MathF.Min(MathF.Abs(distance), 1);
        float width = selectedWidth - (depth * 52);
        float height = selectedHeight - (depth * 72);
        return new SDL.FRect
        {
            X = 96 + ((index - _carouselPosition.Value) * spacing),
            Y = 402 + (depth * 34),
            W = width,
            H = height,
        };
    }

    private void DrawVerticalGradient(UiColor top, UiColor bottom)
    {
        const int strips = 48;
        float height = CanvasHeight / (float)strips;
        for (int index = 0; index < strips; index++)
        {
            DrawFilledRect(
                new SDL.FRect
                {
                    X = 0,
                    Y = index * height,
                    W = CanvasWidth,
                    H = height + 1,
                },
                UiColor.Lerp(top, bottom, index / (strips - 1f)));
        }
    }

    private void DrawOverlay(UiColor color)
    {
        DrawFilledRect(
            new SDL.FRect { X = 0, Y = 0, W = CanvasWidth, H = CanvasHeight },
            color);
    }

    private void DrawText(
        string value,
        float x,
        float y,
        int size,
        UiColor color,
        FontWeight weight = FontWeight.Regular,
        float opacity = 1)
    {
        _text.Draw(value, x, y, size, color, weight, FadeAlpha(opacity));
    }

    private void DrawCenteredText(
        string value,
        float y,
        int size,
        UiColor color,
        FontWeight weight = FontWeight.Regular,
        float opacity = 1)
    {
        _text.DrawCentered(
            value,
            CanvasWidth / 2f,
            y,
            size,
            color,
            weight,
            FadeAlpha(opacity));
    }

    private void DrawCenteredTextAt(
        string value,
        float centerX,
        float y,
        int size,
        UiColor color,
        FontWeight weight = FontWeight.Regular)
    {
        _text.DrawCentered(
            value,
            centerX,
            y,
            size,
            color,
            weight,
            FadeAlpha(255));
    }

    private void DrawRightAlignedText(
        string value,
        float right,
        float y,
        int size,
        UiColor color,
        FontWeight weight = FontWeight.Regular)
    {
        _text.DrawRightAligned(
            value,
            right,
            y,
            size,
            color,
            weight,
            FadeAlpha(255));
    }

    private void DrawFilledRect(SDL.FRect rectangle, UiColor color)
    {
        SetColor(new UiColor(
            color.Red,
            color.Green,
            color.Blue,
            FadeAlpha(color.Alpha)));
        EnsureSuccess(SDL.RenderFillRect(_renderer, in rectangle), "draw panel");
    }

    private void DrawRect(SDL.FRect rectangle, UiColor color)
    {
        SetColor(new UiColor(
            color.Red,
            color.Green,
            color.Blue,
            FadeAlpha(color.Alpha)));
        EnsureSuccess(SDL.RenderRect(_renderer, in rectangle), "draw border");
    }

    private void BeginScreenTransition()
    {
        _screenFade.SnapTo(0);
        _screenFade.SetTarget(1, TimeSpan.FromMilliseconds(360));
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (!_host.SetFullscreen(fullscreen))
        {
            _status = $"Could not change fullscreen mode: {SDL.GetError()}";
            return;
        }
        UpdateSettings(_settings with { Fullscreen = fullscreen });
    }

    private void UpdateSettings(FrontendSettings settings)
    {
        _settings = settings;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException exception)
        {
            _status = $"Could not save settings: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _status = $"Could not save settings: {exception.Message}";
        }
    }

    private void IncludeRememberedDisc()
    {
        string? remembered = _settings.LastDiscPath;
        if (remembered is null ||
            !File.Exists(remembered) ||
            _games.Any(game => string.Equals(
                game.DiscPath,
                remembered,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        _games.Insert(0, _identity.Identify(
            FormatGameName(remembered),
            remembered));
    }

    private string GetLibraryPath() =>
        _settings.LibraryPath ?? Path.Combine(
            Environment.CurrentDirectory,
            "GamesPS1");

    private static string? FindValidBios(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;
        return new FileInfo(path).Length == 512 * 1024
            ? Path.GetFullPath(path)
            : null;
    }

    private static string? FindLocalBios()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, "BiosPS1");
        if (!Directory.Exists(directory))
            return null;
        try
        {
            return Directory
                .EnumerateFiles(directory)
                .Where(path =>
                    Path.GetExtension(path).Equals(
                        ".bin",
                        StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(path).Equals(
                        ".rom",
                        StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(path => new FileInfo(path).Length == 512 * 1024);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void DisposeDialogFilters()
    {
        if (_dialogFilters is null)
            return;
        foreach (SDL.DialogFileFilter filter in _dialogFilters)
            filter.Dispose();
        _dialogFilters = null;
    }

    private byte FadeAlpha(float opacity) =>
        (byte)Math.Clamp(255 * opacity * _screenFade.Value, 0, 255);

    private byte FadeAlpha(byte alpha) =>
        (byte)Math.Clamp(alpha * _screenFade.Value, 0, 255);

    private void SetColor(UiColor color)
    {
        EnsureSuccess(
            SDL.SetRenderDrawColor(
                _renderer,
                color.Red,
                color.Green,
                color.Blue,
                color.Alpha),
            "set draw color");
    }

    private bool IsSetupItemEnabled(int item) =>
        item != ContinueItem || _biosPath is not null;

    private int DashboardItemCount => _games.Count + 1;

    private static bool Contains(SDL.FRect rectangle, float x, float y) =>
        x >= rectangle.X &&
        x < rectangle.X + rectangle.W &&
        y >= rectangle.Y &&
        y < rectangle.Y + rectangle.H;

    private static string FormatGameName(string path) =>
        Path.GetFileNameWithoutExtension(path)
            .Replace('_', ' ')
            .Replace('-', ' ');

    private static string TrimToWidth(string text, int maximumCharacters)
    {
        if (text.Length <= maximumCharacters)
            return text;
        return $"{text[..(maximumCharacters - 3)]}...";
    }

    private static string FormatLastPlayed(DateTimeOffset? lastPlayedUtc)
    {
        if (lastPlayedUtc is null)
            return "NOT PLAYED YET";

        DateTimeOffset local = lastPlayedUtc.Value.ToLocalTime();
        DateTime today = DateTime.Today;
        if (local.Date == today)
            return $"TODAY, {local:HH:mm}";
        if (local.Date == today.AddDays(-1))
            return $"YESTERDAY, {local:HH:mm}";
        return local.ToString("yyyy-MM-dd");
    }

    private static string FormatPlayTime(TimeSpan playedTime)
    {
        if (playedTime < TimeSpan.FromMinutes(1))
            return "< 1 MIN";
        if (playedTime < TimeSpan.FromHours(1))
            return $"{(int)playedTime.TotalMinutes} MIN";
        return $"{(int)playedTime.TotalHours}H {playedTime.Minutes:00}M";
    }

    private static VideoScalingMode CycleScaling(
        VideoScalingMode current,
        int direction)
    {
        const int modeCount = 3;
        int value = ((int)current + Math.Sign(direction)) % modeCount;
        if (value < 0)
            value += modeCount;
        return (VideoScalingMode)value;
    }

    private static string FormatScaling(VideoScalingMode scaling) =>
        scaling switch
        {
            VideoScalingMode.Stretch => "STRETCH",
            VideoScalingMode.IntegerScale => "INTEGER SCALE",
            _ => "ORIGINAL ASPECT",
        };

    private static FrontendThemeMode CycleTheme(
        FrontendThemeMode current,
        int direction)
    {
        int count = Enum.GetValues<FrontendThemeMode>().Length;
        int value = ((int)current + Math.Sign(direction)) % count;
        if (value < 0)
            value += count;
        return (FrontendThemeMode)value;
    }

    private static FrontendWallpaperMode CycleWallpaper(
        FrontendWallpaperMode current,
        int direction)
    {
        int count = Enum.GetValues<FrontendWallpaperMode>().Length;
        int value = ((int)current + Math.Sign(direction)) % count;
        if (value < 0)
            value += count;
        return (FrontendWallpaperMode)value;
    }

    private static string FormatTheme(FrontendThemeMode theme) => theme switch
    {
        FrontendThemeMode.PlayStation => "PLAYSTATION",
        FrontendThemeMode.Minimal => "MINIMAL",
        FrontendThemeMode.Terminal => "TERMINAL",
        _ => "SADCAT",
    };

    private static string FormatWallpaper(FrontendWallpaperMode wallpaper) =>
        wallpaper switch
        {
            FrontendWallpaperMode.Sadcat => "SADCAT",
            FrontendWallpaperMode.Custom => "CUSTOM",
            FrontendWallpaperMode.Solid => "SOLID",
            _ => "GAME ARTWORK",
        };

    private string GetUpdateLabel()
    {
        string automatic = _settings.CheckForUpdates ? "AUTO | " : string.Empty;
        if (_checkingUpdates)
            return $"{automatic}CHECKING...";
        string status = _updateInfo switch
        {
            { IsUpdateAvailable: true } update =>
                $"{update.Tag.ToUpperInvariant()} AVAILABLE",
            not null => "UP TO DATE",
            _ => "CHECK NOW",
        };
        return automatic + status;
    }

    private static string FormatControllerButton(ControllerButton button) =>
        button switch
        {
            ControllerButton.Up => "D-PAD UP",
            ControllerButton.Right => "D-PAD RIGHT",
            ControllerButton.Down => "D-PAD DOWN",
            ControllerButton.Left => "D-PAD LEFT",
            _ => button.ToString().ToUpperInvariant(),
        };

    private static string OnOff(bool enabled) => enabled ? "ON" : "OFF";

    private static string GetGameSubtitle(GameLibraryEntry game)
    {
        var details = new List<string>();
        if (game.Region != "Unknown")
            details.Add(game.Region.ToUpperInvariant());
        if (game.Serial is not null)
            details.Add(game.Serial);
        if (game.DiscNumber is int discNumber)
            details.Add($"DISC {discNumber}");
        if (game.Revision is not null)
            details.Add($"REV {game.Revision.ToUpperInvariant()}");
        return details.Count == 0
            ? "PLAYSTATION"
            : string.Join("  •  ", details);
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
        Library,
        Wallpaper,
    }

    private readonly record struct DialogResult(
        DialogTarget Target,
        string? Path,
        string? Error);

    private readonly record struct MetadataResult(
        string DiscPath,
        LibretroGameMetadata Metadata);

    private readonly record struct UpdateCheckResult(
        FrontendUpdateInfo? Update,
        string? Error);
}
