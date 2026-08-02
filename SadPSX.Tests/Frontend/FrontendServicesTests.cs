using SadPSX.Core.Controllers;
using SadPSX.Frontend.App;
using SadPSX.Frontend.Input;
using SadPSX.Frontend.Library;
using SadPSX.Frontend.Video;
using SDL3;
using Xunit;

namespace SadPSX.Tests.Frontend;

public sealed class FrontendServicesTests
{
    [Fact]
    public void SettingsPersistBiosAndDiscPaths()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new FrontendSettingsStore(path);
            var expected = new FrontendSettings(
                "bios.bin",
                "game.cue",
                "games",
                Fullscreen: false,
                ShowBootAnimation: false,
                DownloadCovers: false,
                UiSounds: false);

            store.Save(expected);

            Assert.Equal(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LegacySettingsReceiveConsoleExperienceDefaults()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-legacy-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "BiosPath": "bios.bin",
                  "LastDiscPath": "game.cue"
                }
                """);

            FrontendSettings settings = new FrontendSettingsStore(path).Load();

            Assert.True(settings.Fullscreen);
            Assert.True(settings.ShowBootAnimation);
            Assert.True(settings.DownloadCovers);
            Assert.True(settings.UiSounds);
            Assert.Equal(VideoScalingMode.AspectRatio, settings.VideoScaling);
            Assert.False(settings.SmoothVideo);
            Assert.True(settings.AudioEnabled);
            Assert.Equal(100, settings.AudioVolume);
            Assert.True(settings.DefaultAnalogController);
            Assert.Equal(FrontendThemeMode.Sadcat, settings.Theme);
            Assert.Equal(
                FrontendWallpaperMode.GameArtwork,
                settings.Wallpaper);
            Assert.True(settings.WallpaperParallax);
            Assert.True(settings.CheckForUpdates);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CoverLookupAddsCommonRegionsAndNormalizesRomanNumerals()
    {
        IReadOnlyList<string> names =
            CoverArtService.GetCandidateNames("Final Fantasy Vii");

        Assert.Equal(
            [
                "Final Fantasy VII",
                "Final Fantasy VII (USA)",
                "Final Fantasy VII (Europe)",
            ],
            names);
    }

    [Fact]
    public void CoverLookupNormalizesRegionAcronyms()
    {
        IReadOnlyList<string> names =
            CoverArtService.GetCandidateNames("Silent Hill (Usa)");

        Assert.Equal(["Silent Hill (USA)"], names);
    }

    [Fact]
    public void LibraryPrefersCueOverMatchingBin()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-library-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "silent-hill.cue"),
                "FILE \"silent-hill.bin\" BINARY");
            File.WriteAllText(Path.Combine(directory, "silent-hill.bin"), "");
            File.WriteAllText(Path.Combine(directory, "rayman.bin"), "");

            IReadOnlyList<GameLibraryEntry> games =
                new GameLibraryScanner().Scan(directory);

            Assert.Equal(2, games.Count);
            Assert.Equal("Rayman", games[0].DisplayName);
            Assert.Equal("Silent Hill", games[1].DisplayName);
            Assert.EndsWith(".cue", games[1].DiscPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LibraryFindsGamesInSubdirectoriesWithoutAddingEveryTrack()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-library-{Guid.NewGuid():N}");
        string gameDirectory = Path.Combine(directory, "Rayman USA");
        Directory.CreateDirectory(gameDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(gameDirectory, "Rayman.cue"),
                "FILE \"Track 01.bin\" BINARY\nFILE \"Track 02.bin\" BINARY");
            File.WriteAllText(Path.Combine(gameDirectory, "Track 01.bin"), "");
            File.WriteAllText(Path.Combine(gameDirectory, "Track 02.bin"), "");

            IReadOnlyList<GameLibraryEntry> games =
                new GameLibraryScanner().Scan(directory);

            GameLibraryEntry game = Assert.Single(games);
            Assert.Equal("Rayman", game.DisplayName);
            Assert.EndsWith(".cue", game.DiscPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LibraryFormatsRomanNumeralsAndRegionAcronyms()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-library-title-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "final-fantasy-vii-(usa).cue"),
                "FILE \"disc.bin\" BINARY");
            File.WriteAllText(Path.Combine(directory, "disc.bin"), "");

            GameLibraryEntry game = Assert.Single(
                new GameLibraryScanner().Scan(directory));

            Assert.Equal("Final Fantasy VII (USA)", game.DisplayName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("cdrom:\\SLUS_007.07;1", "SLUS-00707", "USA")]
    [InlineData("cdrom:\\SLES_015.14;1", "SLES-01514", "Europe")]
    [InlineData("cdrom:\\SLPS_012.22;1", "SLPS-01222", "Japan")]
    public void GameIdentityNormalizesExecutableSerialAndRegion(
        string executable,
        string expectedSerial,
        string expectedRegion)
    {
        string? serial = GameIdentityService.ParseSerial(executable);

        Assert.Equal(expectedSerial, serial);
        Assert.Equal(expectedRegion, GameIdentityService.GetRegion(serial));
    }

    [Fact]
    public void LibraryCatalogRestoresCachedIdentityWhenScanIsIncomplete()
    {
        string path = Path.GetFullPath("game.cue");
        GameLibraryEntry scanned = new("Game", path);
        GameLibraryEntry cached = new(
            "Game",
            path,
            "SLUS-00000",
            "USA",
            1,
            "SLUS_000.00;1");

        GameLibraryEntry merged = Assert.Single(
            GameLibraryCatalogStore.Merge([scanned], [cached]));

        Assert.Equal("SLUS-00000", merged.Serial);
        Assert.Equal("USA", merged.Region);
        Assert.Equal(1, merged.DiscNumber);
    }

    [Fact]
    public void LibretroCatalogMapsSerialToExactReleaseMetadata()
    {
        const string catalog =
            """
            game (
                name "Final Fantasy VII (USA) (Disc 2) (Rev 1)"
                region "USA"
                serial "SCUS-94164"
                rom ( name "disc.bin" serial "SCUS-94164" )
            )
            """;

        IReadOnlyDictionary<string, LibretroGameMetadata> games =
            LibretroGameMetadataService.ParseCatalog(catalog);

        LibretroGameMetadata game = games["SCUS-94164"];
        Assert.Equal("Final Fantasy VII (USA) (Disc 2) (Rev 1)", game.Name);
        Assert.Equal("USA", game.Region);
        Assert.Equal(2, game.DiscNumber);
        Assert.Equal("1", game.Revision);
    }

    [Fact]
    public void LibraryCatalogKeepsResolvedTitleWithFreshDiscIdentity()
    {
        string path = Path.GetFullPath("disc.cue");
        GameLibraryEntry scanned = new(
            "Unknown File Name",
            path,
            "SLUS-00005",
            "USA");
        GameLibraryEntry cached = scanned with
        {
            CatalogName = "Rayman (USA)",
        };

        GameLibraryEntry merged = Assert.Single(
            GameLibraryCatalogStore.Merge([scanned], [cached]));

        Assert.Equal("Rayman (USA)", merged.Title);
    }

    [Fact]
    public void GameActivityPersistsSessionsAndAccumulatedPlayTime()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-activity-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "activity.json");
        string discPath = Path.Combine(directory, "game.cue");

        try
        {
            var store = new GameActivityStore(path);
            var startedAt = new DateTimeOffset(
                2026,
                8,
                1,
                12,
                30,
                0,
                TimeSpan.Zero);

            store.BeginSession(discPath, "SLUS-00005", startedAt);
            store.CompleteSession(
                discPath,
                "SLUS-00005",
                TimeSpan.FromMinutes(40));
            store.BeginSession(
                discPath,
                "SLUS-00005",
                startedAt.AddDays(1));
            store.CompleteSession(
                discPath,
                "SLUS-00005",
                TimeSpan.FromMinutes(35));

            GameActivityEntry activity = new GameActivityStore(path).Get(
                Path.Combine(directory, "moved-game.cue"),
                "SLUS-00005");
            Assert.Equal(2, activity.Sessions);
            Assert.Equal("SLUS-00005", activity.Serial);
            Assert.Equal(startedAt.AddDays(1), activity.LastPlayedUtc);
            Assert.Equal(TimeSpan.FromMinutes(75), activity.TotalPlayed);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        (int)VideoScalingMode.AspectRatio,
        SDL.RendererLogicalPresentation.Letterbox)]
    [InlineData(
        (int)VideoScalingMode.Stretch,
        SDL.RendererLogicalPresentation.Stretch)]
    [InlineData(
        (int)VideoScalingMode.IntegerScale,
        SDL.RendererLogicalPresentation.IntegerScale)]
    public void VideoScalingMapsToSdlPresentation(
        int scaling,
        SDL.RendererLogicalPresentation expected)
    {
        Assert.Equal(
            expected,
            SdlVideoOutput.GetPresentation((VideoScalingMode)scaling));
    }

    [Fact]
    public void SettingsNormalizeInvalidRuntimeValues()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-settings-normalize-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "AudioVolume": 500,
                  "VideoScaling": 99
                }
                """);

            FrontendSettings settings = new FrontendSettingsStore(path).Load();

            Assert.Equal(100, settings.AudioVolume);
            Assert.Equal(VideoScalingMode.AspectRatio, settings.VideoScaling);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GamepadMappingUsesPlayStationFriendlyDefaults()
    {
        GamepadMapping mapping = GamepadMapping.Default;

        Assert.Equal(
            GamepadBinding.South,
            mapping.GetBinding(ControllerButton.Cross));
        Assert.Equal(
            GamepadBinding.East,
            mapping.GetBinding(ControllerButton.Circle));
        Assert.Equal(
            GamepadBinding.LeftTrigger,
            mapping.GetBinding(ControllerButton.L2));
    }

    [Fact]
    public void RebindingSwapsConflictingButtons()
    {
        GamepadMapping mapping = GamepadMapping.Default.Rebind(
            ControllerButton.Cross,
            GamepadBinding.East);

        Assert.Equal(
            GamepadBinding.East,
            mapping.GetBinding(ControllerButton.Cross));
        Assert.Equal(
            GamepadBinding.South,
            mapping.GetBinding(ControllerButton.Circle));
    }

    [Fact]
    public void SettingsPersistCustomControllerMapping()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-controller-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new FrontendSettingsStore(path);
            GamepadMapping mapping = GamepadMapping.Default.Rebind(
                ControllerButton.Cross,
                GamepadBinding.North);
            store.Save(new FrontendSettings(ControllerMapping: mapping));

            FrontendSettings settings = store.Load();

            Assert.Equal(
                GamepadBinding.North,
                settings.EffectiveControllerMapping.GetBinding(
                    ControllerButton.Cross));
            Assert.Equal(
                GamepadBinding.South,
                settings.EffectiveControllerMapping.GetBinding(
                    ControllerButton.Triangle));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("v0.0.2-beta.1", 0, 0, 2)]
    [InlineData("Beta 0.1.0V", 0, 1, 0)]
    [InlineData("1.4", 1, 4, 0)]
    public void UpdateTagsAcceptReleaseNamingStyles(
        string tag,
        int major,
        int minor,
        int build)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out Version? version));
        Assert.NotNull(version);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build < 0 ? 0 : version.Build);
    }

    [Fact]
    public void SettingsPersistThemeAndCustomWallpaper()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sadpsx-theme-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new FrontendSettingsStore(path);
            store.Save(new FrontendSettings(
                Theme: FrontendThemeMode.Terminal,
                Wallpaper: FrontendWallpaperMode.Custom,
                CustomWallpaperPath: "wallpaper.webp",
                WallpaperParallax: false));

            FrontendSettings settings = store.Load();

            Assert.Equal(FrontendThemeMode.Terminal, settings.Theme);
            Assert.Equal(FrontendWallpaperMode.Custom, settings.Wallpaper);
            Assert.Equal("wallpaper.webp", settings.CustomWallpaperPath);
            Assert.False(settings.WallpaperParallax);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
