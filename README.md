# SadPSX

**English** | [Português (Brasil)](README.pt-BR.md)

SadPSX is an experimental PlayStation emulator written in C# and .NET 10.

The project prioritizes correctness, maintainable subsystem boundaries, and
diagnostics that make hardware behavior easier to understand. It is an early
beta: commercial software can boot and reach gameplay, but compatibility,
graphics, audio, and timing are still incomplete.

## Current Status

SadPSX currently provides:

- An interpreted MIPS R3000A CPU with delay slots, load delays, exceptions,
  COP0, interrupts, and unaligned memory operations.
- A GTE/COP2 implementation covering the geometry and lighting commands needed
  by early compatibility tests.
- A 1024×512 GPU VRAM, GP0 command parser, textured and Gouraud primitives,
  transfers, masking, dithering, and video timing.
- DMA channels for MDEC, GPU, CD-ROM, SPU, and OTC transfers.
- CD-ROM commands, IRQs, ISO9660 boot discovery, BIN/CUE images, and CD-DA
  playback.
- SPU voices with ADPCM, ADSR, mixing, and SDL3 audio output.
- MDEC RLE/IDCT decoding and DMA transfers.
- Digital controller input through keyboard or SDL3-compatible gamepads.
- An SDL3 video frontend and diagnostic console.

The SCPH-1001 BIOS reaches the console menu. Rayman has been tested through its
intro and into playable gameplay with a controller. This is not a compatibility
guarantee: visual corruption, imperfect audio, missing features, hangs, and
crashes are expected.

## Beta Download

The initial Windows package is built as a self-contained `win-x64` executable,
so users do not need to install the .NET runtime.

SadPSX does **not** include a BIOS or any game. You must provide:

- A legally dumped 512 KiB PlayStation BIOS.
- Legally obtained BIN/CUE disc images from media you own.

To build the package locally:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

The archive is created under `artifacts/releases/`.

## Running

From source:

```powershell
dotnet run -c Release --project SadPSX.Frontend -- `
  .\BiosPS1\SCPH1001.BIN `
  --disc .\GamesPS1\Game.cue
```

From the Windows release:

```powershell
.\SadPSX.exe .\SCPH1001.BIN --disc .\Game.cue
```

Controls:

- SDL3-compatible Xbox, PlayStation, and generic gamepads are detected while
  the emulator is running.
- Arrow keys: D-pad.
- `Z` / `X`: Cross / Circle.
- `A` / `S`: Square / Triangle.
- `Q` / `W`: L1 / R1; `E` / `D`: L2 / R2.
- `Enter`: Start; `Backspace`: Select.
- `Space`: Pause; `R`: Reset; `F11`: Fullscreen; `Esc`: Exit.

Diagnostic shortcuts:

- `F1`: General CPU, video, IRQ, CD-ROM, DMA, and MMIO state.
- `F2`: Current instruction and CPU registers.
- `F3`: Unhandled MMIO accesses.
- `F4`: Recent CPU exceptions.

Logs are written to the `Logs/` directory next to the executable.

## Building

Requirements:

- [.NET SDK 10](https://dotnet.microsoft.com/download)

Commands:

```powershell
dotnet build SadPSX.slnx
dotnet test SadPSX.slnx
```

The command-line diagnostic runner is also available:

```powershell
dotnet run -c Release --project SadPSX.Cli -- `
  .\BiosPS1\SCPH1001.BIN 1000000 --validate
```

## Project Structure

```text
SadPSX.Core/       Emulator core and hardware subsystems
SadPSX.Cli/        BIOS and disc diagnostic runner
SadPSX.Frontend/   SDL3 interactive frontend
SadPSX.Tests/      Unit, conformance, and subsystem tests
scripts/           Validation and release packaging
```

Core code is organized by emulated hardware under `Cpu/`, `Gpu/`, `Memory/`,
`Bus/`, `Bios/`, `CdRom/`, `Dma/`, `Gte/`, `Mdec/`, `Timers/`, `Interrupts/`,
`Spu/`, and `Controllers/`.

## Known Limitations

- GPU rasterization and DMA/FIFO timing are not pixel- or cycle-perfect.
- GTE commands and saturation behavior are not yet complete.
- Memory cards, DualShock, and analog controller protocols are missing.
- SPU reverb, noise, pitch modulation, XA-ADPCM, and envelope accuracy need
  further work.
- MDEC IDCT and FIFO timing need additional precision.
- Central timing, bus contention, and instruction cache behavior are
  approximate.
- There is no graphical configuration, BIOS selector, or game library yet.

See [CONTRIBUTING.md](CONTRIBUTING.md) before submitting changes.
Release history is documented in [CHANGELOG.md](CHANGELOG.md).

## Legal Notice

No Sony BIOS, games, keys, or copyrighted console assets are included. Use only
BIOS and disc images dumped legally from hardware and media you own.

SadPSX is an independent project and is not affiliated with, associated with,
authorized by, endorsed by, or in any way officially connected with Sony
Interactive Entertainment.

## License

SadPSX is licensed under the [MIT License](LICENSE). Distributed dependency
notices are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
