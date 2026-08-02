# SadPSX Compatibility

SadPSX is an early experimental emulator. A game appearing in this document
means that a specific test session reached the listed milestone. It does not
guarantee support for every region, revision, disc image, or later area of the
game.

No BIOS or game images are distributed with SadPSX.

## Test Environment

The results below were observed through August 2, 2026 with:

- An SCPH-1001 BIOS dump.
- BIN/CUE disc images.
- A Release configuration build.
- SDL3 video, audio, and gamepad output.
- The console-style fullscreen dashboard or direct command-line startup.
- A standard analog controller connected to port 1.
- A raw 128 KiB memory card connected to port 1.

Disc serials and revisions were not recorded. These results should therefore be
treated as development observations rather than definitive compatibility
ratings.

## Current Results

| Game | Milestone | Controller | Memory card | Known issues |
| --- | --- | --- | --- | --- |
| Rayman | Playable gameplay | Working | Save observed working | Audio can stutter or play incorrectly. Performance and graphics still need accuracy work. |
| Silent Hill | Playable gameplay; extended retest pending | Working | Save and BIOS listing observed working | SIO0 input and memory-card persistence work. Recent GPU, GTE, DMA, and frame-presentation fixes removed the major color corruption and periodic stale-frame flashes. Audio remains inaccurate, and the latest long-polyline deadlock fix needs an extended retest. |
| Final Fantasy VII | Reaches gameplay and battles | Working | Save not confirmed | Audio and FMV colors are inaccurate. A save attempt reached a CPU `BREAK` exception at `0x800A916C` before persistence could be confirmed. |

## Recent Accuracy Results

- The SCPH-1001 BIOS now reaches its menu without the temporary geometry
  corruption seen in earlier tests and displays persisted memory-card saves.
- Silent Hill no longer reports unhandled MMIO accesses in the latest sessions.
- Hardware-style GTE 44-bit wrapping and persistent GPU CLUT caching fixed the
  late-session vertex, texture, and color corruption reproduced during longer
  Silent Hill runs.
- DMA2 bus ownership, ordered GP0 submission, and VBlank frame capture removed
  the repeated pause-and-flash behavior seen while the CPU continued running.
- A later long flat-polyline stream exposed a `GPUSTAT` readiness deadlock. The
  streaming handshake has been corrected, but the same long session still
  needs to be repeated before the regression is considered closed.

## Regression Procedure

Use a Release build for compatibility testing:

```powershell
dotnet run -c Release --project SadPSX.Frontend -- `
  .\BiosPS1\SCPH1001.BIN --disc .\GamesPS1\Game.cue
```

For each test:

1. Start through the BIOS instead of skipping directly to the executable.
2. Record the game revision or disc serial when available.
3. Confirm that FMVs complete and gameplay is reached.
4. Test directional input, face buttons, Start, and Select.
5. Observe display size, aspect ratio, colors, textures, and screen edges.
6. Check whether music and effects continue without looping or stalling.
7. Save, restart the emulator, and load the saved data when the game supports
   memory cards.
8. Record approximate MIPS and capture diagnostics before closing the emulator.

## Diagnostic Capture

The frontend writes `Logs/SadPSX.log` next to the executable. During a failure,
capture:

- `F1`: General CPU, video, IRQ, CD-ROM, DMA, and MMIO state.
- `F2`: Current instruction and CPU registers.
- `F3`: Unhandled MMIO accesses.
- `F4`: Recent CPU exceptions.
- `F5`: Recent SIO0 byte transactions.
- `F6`: Completed memory-card commands, sectors, checksums, and results.

CPU `BREAK` exceptions automatically include the raw instruction, break code,
recent SIO0 transactions, and recent memory-card commands in the log.

## Next Retests

1. **Silent Hill:** repeat the longest available session and revisit the scene
   that previously stalled on a flat polyline while monitoring pending GP0 and
   DMA state.
2. **Final Fantasy VII:** reproduce the save attempt and inspect the `F5` and
   `F6` histories if the exception returns.
3. **Rayman:** reload a persisted save and perform a longer gameplay test to
   check audio, pacing, and rendering stability.

The performance block now includes a reproducible benchmark, RAM fast paths,
lower idle-device overhead, and separate normal/full diagnostic modes.
Compatibility results should still be captured before and after performance
changes so timing or rendering regressions remain visible.
