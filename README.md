# SadPSX

<p align="center">
  <img src="docs/assets/sadpsx-logo.png" alt="SadPSX — A PlayStation Emulator" width="360">
</p>

<p align="center">
  An experimental PlayStation emulator written in C# and .NET 10.
</p>

**English** | [Português (Brasil)](README.pt-BR.md)

SadPSX is built around hardware-oriented subsystem boundaries, deterministic
timing, and diagnostics that make emulation failures easier to understand. The
project is still an early beta: several commercial games reach gameplay, but
graphics, audio, performance, and compatibility remain incomplete.

Current release: **0.0.2-beta.1**.

## Current Status

- The SCPH-1001 BIOS reaches the console menu and displays persisted saves.
- Rayman and Silent Hill reach playable gameplay.
- Final Fantasy VII reaches gameplay and battles.
- Digital and analog controllers work through keyboard or SDL3 gamepads.
- Raw 128 KiB memory cards are persisted automatically as `.mcr` files.
- BIN/CUE discs boot through the emulated BIOS and CD-ROM controller.
- The fullscreen dashboard provides a game library, covers, settings, themes,
  controller remapping, play history, and a boot animation.

| Area | State |
| --- | --- |
| CPU, COP0, GTE | Interpreted R3000A with exceptions, delays, interrupts, and the documented GTE command set |
| GPU and DMA | Software GP0 rasterizer, 1 MiB VRAM, transfers, FIFO backpressure, linked lists, and bus arbitration |
| CD-ROM and MDEC | BIN/CUE reading, ISO9660 boot discovery, CD-DA/XA paths, DMA, and software video decoding |
| SPU | 24 voices, ADPCM, ADSR, mixing, noise, modulation, reverb foundation, and SDL3 output |
| Input and storage | Two SIO0 ports, digital/analog pads, SDL3 mapping, and persistent memory cards |
| Timing and diagnostics | Central cycle scheduler, root counters, IRQs, runtime console, traces, and automated tests |

Detailed implementation notes are available in the [technical documentation](docs/README.md).

## Compatibility

Results describe specific development sessions, not guaranteed support for
every region, revision, or disc image.

| Game | Result | Main known issues |
| --- | --- | --- |
| Rayman | Playable | Audio and performance remain inaccurate; some rendering issues remain |
| Silent Hill | Playable | Long sessions still need regression testing; audio remains inaccurate |
| Final Fantasy VII | In-game and battles | FMV colors and audio are inaccurate; save persistence is not yet confirmed |

See [COMPATIBILITY.md](docs/COMPATIBILITY.md) for the test environment,
diagnostic procedure, and current regression targets.

<p align="center">
  <img src="docs/screenshots/rayman-gameplay.png" alt="Rayman running in SadPSX" width="31%">
  <img src="docs/screenshots/silent-hill-gameplay.png" alt="Silent Hill running in SadPSX" width="31%">
  <img src="docs/screenshots/final-fantasy-vii-battle.png" alt="Final Fantasy VII running in SadPSX" width="31%">
</p>

## Quick Start

SadPSX does not include a PlayStation BIOS or game images. Use only dumps made
legally from hardware and media you own.

### Release build

1. Download the Windows archive from [GitHub Releases](https://github.com/kadu04t/SadPSX/releases).
2. Extract the archive and run `SadPSX.exe`.
3. Select a legally dumped 512 KiB BIOS.
4. Add a game folder or select a `.cue`/`.bin` image.
5. Choose a game from the dashboard and start it.

### From the repository

Open the dashboard:

```powershell
dotnet run -c Release --project SadPSX.Frontend
```

Start directly with a BIOS and optional disc:

```powershell
dotnet run -c Release --project SadPSX.Frontend -- `
  .\BiosPS1\SCPH1001.BIN --disc .\GamesPS1\Game.cue
```

The command-line diagnostic frontend remains available through
`SadPSX.Cli`.

## Default Controls

| Keyboard | PlayStation control |
| --- | --- |
| Arrow keys | D-pad |
| `Z` / `X` | Cross / Circle |
| `A` / `S` | Square / Triangle |
| `Q` / `W` | L1 / R1 |
| `E` / `D` | L2 / R2 |
| `Enter` / `Backspace` | Start / Select |
| `Space` | Pause or resume |
| `R` | Reset console |
| `F1`-`F6` | Runtime diagnostics |
| `F7` / `F8` | MMIO trace / controller type |
| `F11` | Toggle fullscreen |
| `Escape` | Return to dashboard or exit |

SDL3-compatible controllers can be remapped from the frontend settings.

## Building and Testing

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows x64 for the provided release package
- SDL3 native libraries are restored through the project packages

```powershell
dotnet restore SadPSX.slnx
dotnet build SadPSX.slnx -c Release
dotnet test SadPSX.Tests -c Release
```

Create a self-contained Windows release archive:

```powershell
.\scripts\publish.ps1 -Version 0.0.2-beta.1 -Runtime win-x64
```

## Documentation

- [Technical documentation index](docs/README.md)
- [Architecture and timing](docs/ARCHITECTURE.md)
- [CPU, COP0, and GTE](docs/CPU.md)
- [GPU and video](docs/GPU.md)
- [SPU and audio](docs/AUDIO.md)
- [CD-ROM, MDEC, DMA, timers, input, and storage](docs/DEVICES.md)
- [Frontend](docs/FRONTEND.md)
- [Compatibility](docs/COMPATIBILITY.md)
- [Contributing](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)

## Project Layout

```text
SadPSX.Core/       Emulated PlayStation hardware
SadPSX.Frontend/   SDL3 dashboard, video, audio, and input
SadPSX.Cli/        Diagnostic command-line tools
SadPSX.Tests/      Unit, conformance, and regression tests
SadPSX.Benchmarks/ Reproducible performance benchmarks
docs/              Technical and compatibility documentation
scripts/           Validation and release packaging
```

## Known Limitations

- Audio timing, reverb, XA playback, and mixing still need accuracy work.
- The interpreter does not always sustain real-time emulation on every host.
- GPU rasterization and MDEC color conversion still produce visible defects.
- Compatibility is limited and untested games may hang, crash, or fail to boot.
- Save states, debugger UI, netplay, achievements, and hardware rendering are
  not implemented.

## Philosophy

SadPSX is designed for high hardware fidelity and maintainable subsystem
boundaries. ProjectPSX was designed primarily to be simple and educational;
SadPSX instead uses explicit timing, hardware state, and diagnostics as its
foundation while remaining open and educational.

## Legal Notice

PlayStation is a trademark of Sony Interactive Entertainment. SadPSX is an
independent project and is not affiliated with or endorsed by Sony. No BIOS,
games, encryption keys, or copyrighted console software are distributed.

## License

SadPSX is licensed under the [GNU GPL v3](LICENSE).

The goal is to remain open, educational, and collaborative. If you distribute
modified versions of SadPSX, those modifications must also remain open under
the GPL.
