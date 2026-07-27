## What Changed

Describe the concrete change and the affected subsystem.

## Discovery and Reproduction

Explain how the bug or missing behavior was discovered. Include exact
reproduction steps, relevant game/BIOS behavior, addresses, commands, traces,
or test names.

## Root Cause

Explain why the previous implementation was incorrect or incomplete.

## Solution

Explain how the implementation fixes the root cause and why it matches
PlayStation hardware behavior.

## Validation

List every command and compatibility scenario used to test the change.

```text
dotnet build SadPSX.slnx
dotnet test SadPSX.slnx --no-build
```

Describe before/after results and attach relevant logs, traces, or screenshots.
Do not attach BIOS files or game data.

## Regression Risk

List nearby behavior that could regress and how each risk was checked. State
clearly what was not tested.

## Checklist

- [ ] I understand and can explain every submitted change.
- [ ] Code identifiers, comments, tests, and documentation are in English.
- [ ] Focused tests cover the changed behavior where practical.
- [ ] The complete test suite passes.
- [ ] I checked a relevant BIOS or game path when runtime behavior can change.
- [ ] I did not weaken tests only to make them pass.
- [ ] I did not include BIOS data, game data, logs, or generated artifacts.
- [ ] If I used AI assistance, I reviewed and verified all generated content.
