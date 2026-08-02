# Frontend

## Console-Style Experience

The SDL3 frontend uses a shared fullscreen-capable host for boot animation,
dashboard, settings, and emulation. It intentionally avoids native menu bars
and traditional emulator toolbars.

The boot sequence displays Sadcat artwork and short initialization messages.
The dashboard presents games as a carousel with themed backgrounds, transitions,
focus animation, navigation sounds, and controller-first input.

## Library and Metadata

The library scanner searches configured directories for BIN/CUE images,
prefers CUE sheets over matching BIN tracks, and avoids listing every track as
a separate game.

Disc identity can be derived from the boot executable serial. Cached catalog
metadata records title, region, revision, disc number, cover path, last played
time, accumulated play time, and session count. Optional cover lookup uses
public metadata sources and falls back to a bundled placeholder.

No BIOS, disc image, or copyrighted game artwork is bundled with SadPSX.

## Settings

The dashboard groups settings into video, audio, controls, interface, system,
updates, and debug areas. Current settings include:

- fullscreen, aspect/stretch/integer scaling, and texture filtering;
- audio enable and volume preferences;
- digital/analog default controller and gamepad remapping;
- themes, wallpapers, parallax, UI sounds, and boot animation;
- library/BIOS paths and optional metadata downloads;
- update checks and diagnostic options.

Settings and library state are stored outside the emulated machine and loaded
on the next frontend session.

## Input and Navigation

Keyboard and SDL3 gamepads share abstract UI actions. Focus wraps through
enabled controls, and the screen navigator retains history. During gameplay,
`Escape` or the gamepad Guide button returns to the dashboard; direct command
line sessions exit instead.

Function keys keep runtime diagnostics available even though the application
normally hides the terminal.

## Presentation Boundaries

`SdlFrontendHost` owns the window and renderer. Dashboard textures, fonts, and
game frames share that host, while the emulation core remains unaware of SDL3.
The video output switches logical presentation for each game frame and returns
control to the dashboard without recreating the entire application.

## Current Limitations

- Settings do not yet cover every core accuracy option.
- Update checks do not perform a complete in-app installation workflow.
- Cover matching depends on filenames or discovered disc serials.
- Accessibility, localization, mouse navigation, and small-window layouts need
  additional work.
