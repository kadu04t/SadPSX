# Changelog

All notable changes to SadPSX releases are documented in this file.

The project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Central timing scheduler with deterministic device advancement.
- Cancellable central timing events dispatched at exact system-cycle
  boundaries.
- SPU noise generation, pitch modulation, capture buffers, and IRQ9.
- GPU DMA FIFO with backpressure, DPCR priority arbitration, and burst
  chopping.
- XA-ADPCM decoding with 37.8/18.9 kHz resampling and CD-ROM channel filtering.
- CD-ROM stereo volume matrix and SPU fixed/linear/exponential volume sweeps.
- Integer MDEC IDCT driven by the uploaded scale table.

### Changed

- SPU voices now use fractional sample interpolation and more accurate
  exponential ADSR attack timing.
- GPU DMA words are consumed on GPU clock ticks instead of executing
  immediately when written by DMA2.
- MDEC coefficient placement, saturation, color conversion, and current-block
  status now follow the hardware data path more closely.

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
