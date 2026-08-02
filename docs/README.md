# SadPSX Technical Documentation

This directory contains implementation-oriented documentation for SadPSX. The
repository root README intentionally stays focused on project status,
compatibility, and usage.

## Hardware and Architecture

- [Architecture and timing](ARCHITECTURE.md): machine composition, bus,
  scheduler, interrupts, and execution model.
- [CPU, COP0, and GTE](CPU.md): R3000A execution, exceptions, delays, and COP2.
- [GPU and video](GPU.md): GP0/GP1, VRAM, rasterization, DMA, and presentation.
- [SPU and audio](AUDIO.md): voices, ADPCM, ADSR, CD audio, mixing, and SDL3.
- [Other devices](DEVICES.md): CD-ROM, MDEC, DMA, timers, SIO0, memory cards,
  and memory map.

## Application and Validation

- [Frontend](FRONTEND.md): dashboard, library, settings, input, and diagnostics.
- [Compatibility](COMPATIBILITY.md): tested games and regression procedure.
- [Contributing](../CONTRIBUTING.md): coding, testing, and pull-request rules.
- [Changelog](../CHANGELOG.md): release history.

## Source Layout

Each hardware subsystem has a matching directory under `SadPSX.Core`. Tests
mirror those boundaries under `SadPSX.Tests`. The SDL3 application is isolated
in `SadPSX.Frontend`, allowing core tests to run without creating a window or
audio device.

The documentation describes the current implementation rather than claiming
complete hardware accuracy. When code and documentation disagree, the code and
tests define the actual behavior and the documentation should be corrected.
