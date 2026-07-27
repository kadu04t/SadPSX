# Contributing to SadPSX

Thank you for helping improve SadPSX.

SadPSX prioritizes hardware fidelity, reproducible behavior, and maintainable
subsystem boundaries. Contributions should improve that foundation rather than
introduce game-specific shortcuts.

## Core Expectations

- Keep changes focused on one subsystem or behavior.
- Prefer hardware documentation and reproducible tests over game-specific
  workarounds.
- Understand every line you submit and be prepared to explain the hardware
  behavior behind it.
- Check that the change does not regress BIOS boot, tested games, diagnostics,
  or unrelated hardware paths.
- Do not commit BIOS files, game images, copyrighted assets, logs, or generated
  build output.
- Use English for code identifiers, code comments, public documentation, issue
  titles, commit messages, tests, and new diagnostic messages.
- Preserve the existing subsystem folder and namespace organization.

## AI-Assisted Contributions

AI tools may assist development, but they do not replace understanding or
verification.

- Never submit generated code blindly.
- Review and understand every generated line before including it.
- Verify hardware claims against reliable documentation or observed behavior.
- Check generated code for invented APIs, incorrect edge cases, copied
  material, licensing problems, and unnecessary complexity.
- You remain fully responsible for the correctness, legality, maintainability,
  and regression impact of the submitted change.
- If you cannot explain how the change works and why it is correct, it is not
  ready for review.

## Development Workflow

1. Create a branch from `main`.
2. Reproduce the bug or define the missing hardware behavior before editing.
3. Add or identify a focused test that demonstrates the expected behavior.
4. Build the solution:

   ```powershell
   dotnet build SadPSX.slnx
   ```

5. Run the complete test suite:

   ```powershell
   dotnet test SadPSX.slnx --no-build
   ```

6. Re-run relevant BIOS or game compatibility scenarios when the change can
   affect runtime behavior.
7. Review the final diff for unrelated files, temporary artifacts, BIOS/game
   data, and accidental formatting changes.

Do not change expected test values merely to make a failure disappear. Confirm
that the new expectation represents actual PlayStation hardware behavior.

## Regression Requirements

Before opening a pull request:

- Run focused tests for the changed subsystem.
- Run the full test suite.
- Confirm that existing tests were not removed or weakened without a clear
  reason.
- Exercise at least one relevant BIOS or game path for changes involving CPU,
  GPU, DMA, CD-ROM, GTE, SPU, MDEC, timing, controllers, or memory.
- Compare before/after output when fixing rendering, audio, timing, or
  compatibility bugs.
- Document any known behavior that remains incorrect.

If a complete compatibility test is impractical, state exactly what was and
was not tested.

## Code and Comments

- Keep namespaces aligned with their subsystem folders.
- Follow the surrounding style and avoid unrelated refactors.
- Write comments in English.
- Comments should explain hardware intent, constraints, or non-obvious
  reasoning instead of repeating the code.
- Prefer small, testable changes over broad rewrites.
- Avoid hardcoded game-specific behavior unless it models documented hardware.

## Pull Request Description

Every pull request should explain:

1. **What changed:** the concrete behavior and affected subsystem.
2. **How the problem was discovered:** failing game, BIOS trace, test,
   hardware documentation, log, screenshot, or another reproducible signal.
3. **How to reproduce it:** exact steps, inputs, relevant addresses, commands,
   or test names.
4. **Root cause:** why the previous implementation was wrong or incomplete.
5. **Why the solution is correct:** hardware evidence and design reasoning.
6. **How it was tested:** exact commands, tests, games, BIOS revision, and
   before/after results.
7. **Regression risk:** nearby behavior that could be affected and how it was
   checked.

Include logs, traces, screenshots, or references when they materially support
the change. Never attach copyrighted BIOS or game data.

## Commit Messages

The project uses Conventional Commits:

```text
feat(gpu): implement texture window masking
fix(dma): preserve channel address after transfer
test(gte): cover saturation edge cases
docs: update build instructions
```

## Legal Requirements

Contributions must be your own work or material you are allowed to submit.
Never include proprietary SDK code, leaked source code, BIOS data, game data,
or copied assets.

By submitting a contribution, you agree that it is licensed under the same
[GNU General Public License v3](LICENSE) (`GPL-3.0-only`) used by SadPSX.
