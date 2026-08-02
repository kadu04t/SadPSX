# Architecture and Timing

## Goals

SadPSX separates the emulated machine from presentation and host input. The
core models PlayStation hardware state; the frontend translates complete video
frames, audio samples, and user input to SDL3.

The main goals are:

- deterministic device advancement from CPU cycles;
- explicit ownership of registers, FIFOs, and interrupts;
- minimal coupling between hardware subsystems;
- diagnostics that expose state without changing it;
- unit and regression tests at subsystem boundaries.

## Machine Composition

`PsxMachine` owns the R3000A CPU, system bus, and central timing scheduler. The
bus connects RAM, BIOS, scratchpad, memory control, GPU, DMA, CD-ROM, MDEC, SPU,
SIO0, timers, interrupts, and POST diagnostics.

A normal machine step performs three operations:

1. Let DMA hold the CPU bus when an active transfer owns it.
2. Fetch and execute one R3000A instruction.
3. Advance every clocked device by the cycles consumed by that instruction.

The optimized batch path preserves the same order while reducing host-language
overhead.

## Address Space

The bus translates KUSEG, KSEG0, KSEG1, and KSEG2 addresses to physical
addresses, applies RAM mirrors, and routes MMIO to the owning device. RAM,
scratchpad, BIOS, expansion regions, cache control, and memory-control registers
retain separate behavior.

Unhandled MMIO is recorded by access width and direction so compatibility
failures can be diagnosed without flooding the console.

## Central Timing

`TimingScheduler` tracks the system clock and advances the primary hardware
set. It can also schedule cancellable callbacks at exact cycle boundaries.

Clocked devices consume accumulated cycles rather than host time. Video timing
derives dotclock, HBlank, VBlank, scanlines, frames, and IRQ0 from CPU cycles.
The SPU produces one stereo frame per 768 CPU cycles, corresponding to 44.1 kHz
at the PlayStation CPU clock.

Current performance work is focused on avoiding unnecessary per-instruction
device overhead while retaining event ordering. A future event-driven fast
path may batch idle devices until their next observable boundary.

## Interrupts

The interrupt controller implements `I_STAT` and `I_MASK` and propagates the
combined hardware line to COP0. Sources include VBlank, GPU, CD-ROM, DMA,
timers, SIO0, SPU, and expansion devices.

Interrupt delivery respects COP0 status and cause masks, current privilege,
and branch delay completion.

## Diagnostics

Runtime snapshots expose CPU state, video timing, DMA channels, CD-ROM queues,
SIO0 traffic, memory-card commands, exceptions, and unhandled MMIO. Normal
execution keeps bounded histories so failures can be captured after they occur.

## Current Limitations

- Device updates still have significant interpreted per-instruction cost.
- Several timing constants are approximate rather than measured from hardware.
- Host presentation and audio buffering are not part of the deterministic core.
- Save states are not available, so long regressions must currently be replayed.
