# Contributing to SadPSX

Thank you for helping improve SadPSX.

## Before Opening a Change

- Keep changes focused on one subsystem or behavior.
- Prefer hardware documentation and reproducible tests over game-specific
  workarounds.
- Do not commit BIOS files, game images, copyrighted assets, logs, or generated
  build output.
- Use English for public documentation, issue titles, commit messages, code
  identifiers, and new diagnostic messages.
- Preserve the existing subsystem folder and namespace organization.

## Development Workflow

1. Create a branch from `main`.
2. Build the solution:

   ```powershell
   dotnet build SadPSX.slnx
   ```

3. Run the test suite:

   ```powershell
   dotnet test SadPSX.slnx --no-build
   ```

4. Add focused tests for hardware behavior whenever practical.
5. Describe the hardware behavior, evidence, and compatibility impact in the
   pull request.

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
