# GPU and Video

## Registers and Commands

The GPU implements the GP0 data port, GP1 control port, `GPUSTAT`, and GPU read
data. GP0 commands are collected into packets, including variable-length
polylines and CPU-to-VRAM transfers.

GP1 controls reset, display enable, DMA direction, display start, horizontal
and vertical ranges, display mode, and GPU information queries.

## VRAM and Transfers

VRAM is a 1024×512 array of 16-bit pixels. Implemented transfer paths include:

- CPU to VRAM image upload;
- VRAM to CPU image readback;
- overlapping VRAM-to-VRAM copies;
- fill rectangles;
- DMA2 block and linked-list command submission.

Mask-bit checks and writes are applied during rasterization and transfers.
Texture data supports 4-bit, 8-bit, and 15-bit formats with CLUT lookup and
texture-window addressing.

## Software Rasterizer

The GP0 rasterizer supports flat and Gouraud polygons, textured polygons,
rectangles, sprites, lines, polylines, dithering, semi-transparency, draw-area
clipping, drawing offsets, and coordinate wrapping.

Recent fixes preserve CPU/DMA command ordering, retain CLUT cache state across
draws, stream long polylines without deadlocking readiness bits, and reject
invalid primitive halves independently.

## FIFO and DMA

DMA writes enter a bounded GPU FIFO. `GPUSTAT` readiness and DREQ reflect FIFO
capacity, packet collection, image transfer state, and configured DMA direction.
DMA2 models priorities, request acceptance, chopping, linked-list nodes, bus
ownership, and CPU stalls.

## Video Timing and Presentation

`VideoTiming` advances NTSC or PAL raster state from CPU cycles. It derives
scanlines, horizontal blanking, vertical blanking, dotclock, frames, and VBlank
interrupts.

The frontend captures a completed display image at VBlank and presents the
newest frame through SDL3. Aspect-ratio, stretch, integer-scale, nearest, and
linear presentation options are available.

## Diagnostics

Snapshots include GP0/GP1 counts, FIFO depth, pending packet words, rejected
primitives, display ranges, VRAM start, captured/presented/dropped frames, and
DMA2 transfer age.

## Current Limitations

- Rasterization is software-only and not pixel-perfect.
- Texture, blending, dithering, and edge rules still have game-visible errors.
- Interlacing and some display-range transitions need more hardware testing.
- Uploading and presenting full frames remains a performance cost.
