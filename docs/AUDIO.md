# SPU and Audio

## SPU Core

The SPU exposes the PlayStation register range, 512 KiB sound RAM, transfer
FIFO, IRQ9 behavior, and 24 independent voices.

Voice processing includes:

- PSX ADPCM block decoding and loop flags;
- pitch stepping and fractional interpolation;
- ADSR attack, decay, sustain, and release states;
- per-voice left/right volumes and volume sweeps;
- key-on, key-off, end flags, noise, and pitch modulation;
- capture buffers and sound-RAM IRQ checks.

The mixer combines voices, CD audio, main volume, and reverb paths into signed
stereo samples at 44.1 kHz.

## CD Audio

CD-DA sectors are converted to stereo PCM and delivered through the CD volume
matrix. XA-ADPCM decoding supports common 37.8 and 18.9 kHz modes, channel
filtering, and resampling before entering the SPU mix.

## Host Output

The core owns emulated sample generation. `SdlAudioOutput` owns host buffering
and submits stereo samples to SDL3. These layers remain separate so SPU tests
do not require an audio device.

Runtime metrics report SPU/CD queue depth, host queue depth, underruns, dropped
frames, silence insertion, device format, and callback demand.

## Current Limitations

- Reverb behavior and several SPU transfer/IRQ details are incomplete.
- Some games produce clipping, incorrect balance, echo-like artifacts, or
  unstable playback.
- Slow emulation can starve the host buffer even when generated samples are
  individually correct.
- XA timing and filtering still need broader game and hardware comparison.

Audio work should distinguish three failure classes: incorrect SPU samples,
incorrect CD/XA samples, and correct samples delivered at the wrong host rate.
Changing host playback frequency must not be used to hide core timing errors.
