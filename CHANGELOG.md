# Changelog

All notable changes to SadPSX releases are documented in this file.

The project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.0.2-beta.1] - 2026-08-02

### Added

- Console-style SDL3 dashboard with boot animation, game carousel, fullscreen
  navigation, themes, wallpapers, UI sounds, and persistent settings.
- Game-library scanning, BIN/CUE deduplication, disc identity, optional metadata
  and cover lookup, play history, and accumulated play time.
- Persistent controller remapping and digital/analog controller selection.
- Two SIO0 ports with analog/DualShock protocol support and raw 128 KiB memory
  cards persisted as `.mcr` files.
- Central timing scheduler with cancellable events dispatched at exact system
  cycle boundaries.
- SPU noise, pitch modulation, capture buffers, IRQ9, fractional interpolation,
  current-volume reads, volume sweeps, and expanded ADSR behavior.
- XA-ADPCM decoding, channel filtering, resampling, and CD-ROM stereo volume
  matrix.
- Integer MDEC IDCT driven by uploaded scale tables.
- Runtime histories for SIO0 transactions, memory-card commands, CPU breakpoints,
  GPU packets, DMA transfers, and frame presentation.
- Technical documentation split by architecture, CPU, GPU, audio, devices, and
  frontend.

### Changed

- GTE now implements the documented command set with improved 44-bit MAC wrap,
  saturation, projection division, color commands, and overflow behavior.
- GPU command processing now preserves CPU/DMA ordering, uses a bounded FIFO,
  models backpressure, retains CLUT cache state, and streams long polylines.
- DMA2 now models priorities, linked-list request state, chopping, bus ownership,
  CPU stalls, cyclic-list detection, and GPU arbitration.
- Video presentation captures completed VBlank frames and reports captured,
  presented, dropped, and repeated frames.
- MDEC coefficient placement, saturation, color conversion, block status, and
  output packing follow the hardware data path more closely.
- The core uses optimized memory and timing paths while retaining deterministic
  device order.
- Documentation now keeps the root README focused on status, compatibility, and
  usage.

### Fixed

- Late-session geometry, texture, and color corruption reproduced in Silent
  Hill.
- Periodic stale-frame flashes caused by presenting partially updated frames.
- GPU readiness deadlocks during long flat-polyline streams.
- Several SIO0 selection, acknowledge, analog-pad, and memory-card protocol
  failures affecting commercial games.
- Breakpoint diagnostics now preserve the original instruction and delay-slot
  context.

### Known Issues

- Audio can clip, stutter, echo, or run incorrectly depending on the game and
  host performance.
- GPU rasterization and MDEC color conversion still have visible inaccuracies.
- The interpreted CPU does not sustain real time on every host.
- Compatibility remains limited and untested games may fail to boot or hang.
- Final Fantasy VII save persistence has not been confirmed after a CPU
  breakpoint interrupted the test.

## [0.0.1-beta.1] - 2026-07-27

### Added

- Interpreted R3000A CPU, COP0, interrupts, timers, and memory bus.
- GTE/COP2 geometry and lighting command foundation.
- GP0 rasterization, VRAM transfers, display timing, and SDL3 video output.
- DMA transfers for GPU, MDEC, CD-ROM, SPU, and OTC.
- CD-ROM boot, BIN/CUE support, CD-DA playback, SPU voices, and MDEC decoding.
- Digital keyboard and SDL3 gamepad input.
- Diagnostic CLI, runtime console, logging, and 359 automated tests.
- SadPSX application icon, project logo, and compatibility documentation.
- Basic SDL3 launcher with native BIOS and disc file selection.

### Changed

- Project licensing changed to GNU GPL v3 (`GPL-3.0-only`) to keep distributed
  modifications open.

### Known Issues

- GPU rendering contains visible accuracy problems.
- Audio can stutter or play incorrectly.
- Compatibility is limited and many games may hang or fail to boot.
- Memory cards, analog controllers, and several advanced hardware behaviors are
  not implemented.
