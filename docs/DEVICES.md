# CD-ROM, MDEC, DMA, Timers, Input, and Storage

## Memory and Bus

The memory map includes 2 MiB RAM with mirrors, 1 KiB scratchpad, 512 KiB BIOS,
expansion regions, memory-control registers, cache control, and MMIO dispatch.
KUSEG/KSEG translation, privilege checks, alignment faults, and approximate
access costs are handled by the bus and CPU.

## CD-ROM

The controller models indexed registers, parameter/result/data FIFOs, IRQ2,
command latency, motor state, seeking, reading, and track playback. Implemented
commands cover BIOS boot and common game flows such as `Getstat`, `Setloc`,
`ReadN`, `ReadS`, `Pause`, `Init`, `GetTN`, `GetTD`, `GetlocL`, `GetlocP`,
`Setmode`, `SeekL`, `SeekP`, `GetID`, `SetSession`, `ReadTOC`, and `Play`.

BIN/CUE media supports data and audio tracks. ISO9660 parsing locates
`SYSTEM.CNF` and the boot executable. DMA3 transfers sector data to RAM.

## MDEC

MDEC accepts compressed data through DMA0 and returns decoded macroblocks
through DMA1. It implements RLE coefficient decoding, quantization tables,
uploaded scale tables, integer IDCT, monochrome/color output, saturation, and
15/24-bit packing.

## DMA

The DMA controller exposes seven channels, `DPCR`, and `DICR`. Functional paths
include MDEC input/output, GPU blocks and linked lists, CD-ROM reads, SPU
transfers, and OTC generation. Priorities, master enables, IRQ flags, 24-bit
addressing, chopping, GPU backpressure, and bus ownership are modeled.

The PIO channel retains registers but does not execute transfers.

## Timers

Three root counters implement value, mode, target, reset, target/overflow
flags, one-shot/repeat IRQs, pulse/toggle behavior, Timer 2 divide-by-eight,
and basic HBlank, VBlank, and dotclock synchronization.

## Controllers and SIO0

SIO0 models status, mode, control, baud, byte timing, acknowledge pulses, and
IRQ7. Two ports may host digital controllers, analog/DualShock controllers, or
memory cards.

The analog controller implements configuration commands used by commercial
games. The SDL3 frontend maps keyboard, gamepad buttons, triggers, and analog
sticks to the emulated pad and supports persistent remapping.

## Memory Cards

Each port can use a raw 128 KiB card. Read, write, identify, checksum, status,
and sector addressing commands are implemented. Dirty cards are saved as
`.mcr` files and can be listed by the BIOS memory-card menu.

## Current Limitations

- Multitap, mouse, light guns, PocketStation, and uncommon peripherals are not
  implemented.
- CD subchannel behavior, drive timing, XA, and error responses are incomplete.
- MDEC output still shows color defects in some FMVs.
- DMA timing remains approximate outside the heavily tested GPU paths.
