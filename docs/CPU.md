# CPU, COP0, and GTE

## R3000A Core

The CPU is an interpreter for the little-endian MIPS R3000A used by the
PlayStation. It models the 32 general-purpose registers, `HI`, `LO`, program
counter state, branch delay slots, load delays, and instruction cycle costs.

Implemented instruction groups include:

- signed and unsigned arithmetic with overflow exceptions;
- logical operations, comparisons, immediate and variable shifts;
- multiplication and division with hardware edge cases;
- conditional branches and `J`, `JAL`, `JR`, and `JALR`;
- byte, halfword, and word loads and stores;
- `LWL`, `LWR`, `SWL`, and `SWR` unaligned merge operations;
- COP0 and COP2 transfers and commands.

The `$zero` register is enforced after execution. Branch targets and link
addresses follow delay-slot semantics, and delayed loads are committed in the
same order as the hardware pipeline model used by the project.

## Exceptions and COP0

COP0 implements status, cause, EPC, processor identification, breakpoint, and
related control registers. Supported exceptions include:

- interrupts;
- address and bus errors;
- syscall and breakpoint;
- reserved instruction and coprocessor unusable;
- signed arithmetic overflow.

Exception entry records whether the fault occurred in a branch delay slot,
selects the vector through `SR.BEV`, updates the privilege stack, and cancels
pending control flow when required. `MFC0`, `MTC0`, and `RFE` are implemented
with register-specific masks.

## GTE and COP2

COP2 implements register transfers through `MFC2`, `MTC2`, `CFC2`, `CTC2`,
`LWC2`, and `SWC2`. Transfers back to the CPU participate in load-delay
behavior.

The GTE models data and control registers, vector/color FIFOs, saturation flags,
UNR projection division, and intermediate/final 44-bit MAC wrapping. The
documented command set is present:

`RTPS`, `RTPT`, `NCLIP`, `OP`, `DPCS`, `INTPL`, `MVMVA`, `NCDS`, `CDP`,
`NCDT`, `NCCS`, `CC`, `NCS`, `NCT`, `SQR`, `DCPL`, `DPCT`, `AVSZ3`, `AVSZ4`,
`GPF`, `GPL`, and `NCCT`.

## Validation

CPU tests cover instruction behavior, delays, branches, exceptions, interrupts,
loads/stores, and conformance programs. GTE tests focus on command output,
flags, saturation boundaries, wrapping, and projection edge cases.

## Current Limitations

- The CPU is interpreted and has no block cache or dynamic recompiler.
- Cycle costs and cache-isolation behavior are not complete in every path.
- GTE edge cases continue to be compared against hardware-oriented references.
