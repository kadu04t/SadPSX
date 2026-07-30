using SadPSX.Core.Bus;
using SadPSX.Core.Interrupts;

namespace SadPSX.Core.Timers;

public sealed class RootCounters : IClockedDevice, IMmioDevice
{
    public const uint Timer0BaseAddress = 0x1F80_1100;
    public const uint Timer1BaseAddress = 0x1F80_1110;
    public const uint Timer2BaseAddress = 0x1F80_1120;

    private const uint TimerStride = 0x10;
    private const uint CounterOffset = 0;
    private const uint ModeOffset = 4;
    private const uint TargetOffset = 8;

    private const ushort ResetAtTargetBit = 1 << 3;
    private const ushort IrqAtTargetBit = 1 << 4;
    private const ushort IrqAtOverflowBit = 1 << 5;
    private const ushort IrqRepeatBit = 1 << 6;
    private const ushort IrqToggleBit = 1 << 7;
    private const ushort InterruptRequestBit = 1 << 10;
    private const ushort ReachedTargetBit = 1 << 11;
    private const ushort ReachedOverflowBit = 1 << 12;
    private const ushort WritableModeMask = 0x03FF;

    private readonly InterruptController _interruptController;
    private readonly TimerState[] _timers =
    [
        new(),
        new(),
        new(),
    ];

    public RootCounters(InterruptController interruptController)
    {
        _interruptController = interruptController ??
            throw new ArgumentNullException(nameof(interruptController));
        Reset();
    }

    public ushort GetCounter(int timerIndex) => GetTimer(timerIndex).Counter;

    public ushort GetTarget(int timerIndex) => GetTimer(timerIndex).Target;

    public ushort GetMode(int timerIndex) =>
        ComposeMode(GetTimer(timerIndex));

    public void Reset()
    {
        foreach (TimerState timer in _timers)
            timer.Reset();
    }

    public void Tick(uint cycles)
    {
        if (cycles == 0)
            return;

        for (int timerIndex = 0; timerIndex < _timers.Length; timerIndex++)
            TickTimer(timerIndex, cycles);
    }

    public void TickDotClock(uint ticks)
    {
        TimerState timer = _timers[0];
        uint clockSource = (uint)(timer.Mode >> 8) & 3;

        if (ticks != 0 &&
            (clockSource & 1) != 0 &&
            timer.HoldCycles == 0 &&
            CanCount(0, timer))
        {
            Advance(0, timer, ticks);
        }
    }

    public void SetHBlank(bool active)
    {
        bool risingEdge = SetBlankSignal(0, active);
        TimerState timer = _timers[1];
        uint clockSource = (uint)(timer.Mode >> 8) & 3;

        if (risingEdge &&
            (clockSource & 1) != 0 &&
            timer.HoldCycles == 0 &&
            CanCount(1, timer))
        {
            Advance(1, timer, 1);
        }
    }

    public void SetVBlank(bool active)
    {
        SetBlankSignal(1, active);
    }

    public bool Handles(uint address)
    {
        if (address < Timer0BaseAddress ||
            address >= Timer0BaseAddress + TimerStride * 3)
        {
            return false;
        }

        uint registerOffset = (address - Timer0BaseAddress) % TimerStride;
        return (registerOffset & ~3u) <= TargetOffset;
    }

    public byte Read8(uint address)
    {
        ushort value = ReadRegister(address, clearFlags: true);
        int shift = (int)((address & 3) * 8);
        return shift < 16 ? (byte)(value >> shift) : (byte)0;
    }

    public ushort Read16(uint address)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Leitura de 16 bits desalinhada em timer: 0x{address:X8}.");

        if ((address & 2) != 0)
            return 0;

        return ReadRegister(address, clearFlags: true);
    }

    public uint Read32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada em timer: 0x{address:X8}.");

        return ReadRegister(address, clearFlags: true);
    }

    public uint Peek32(uint address)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Leitura de 32 bits desalinhada em timer: 0x{address:X8}.");

        return ReadRegister(address, clearFlags: false);
    }

    public void Write8(uint address, byte value)
    {
        int shift = (int)((address & 3) * 8);
        if (shift >= 16)
            return;

        ushort currentValue = ReadRegister(address, clearFlags: false);
        ushort writeMask = (ushort)(0xFF << shift);
        ushort mergedValue = (ushort)(
            (currentValue & ~writeMask) |
            (value << shift));
        WriteRegister(address, mergedValue);
    }

    public void Write16(uint address, ushort value)
    {
        if ((address & 1) != 0)
            throw new InvalidOperationException(
                $"Escrita de 16 bits desalinhada em timer: 0x{address:X8}.");

        if ((address & 2) == 0)
            WriteRegister(address, value);
    }

    public void Write32(uint address, uint value)
    {
        if ((address & 3) != 0)
            throw new InvalidOperationException(
                $"Escrita de 32 bits desalinhada em timer: 0x{address:X8}.");

        WriteRegister(address, (ushort)value);
    }

    public string GetRegisterName(uint address)
    {
        (int timerIndex, uint registerOffset) = DecodeAddress(address);
        string suffix = registerOffset switch
        {
            CounterOffset => "VALUE",
            ModeOffset => "MODE",
            TargetOffset => "TARGET",
            _ => "UNKNOWN",
        };

        return $"TMR{timerIndex}_{suffix}";
    }

    private void TickTimer(int timerIndex, uint cycles)
    {
        TimerState timer = _timers[timerIndex];

        if (timer.PulseRestorePending)
        {
            timer.InterruptRequestHigh = true;
            timer.PulseRestorePending = false;
        }

        uint availableCycles = cycles;
        if (timer.HoldCycles != 0)
        {
            uint consumedCycles = Math.Min(timer.HoldCycles, availableCycles);
            timer.HoldCycles -= consumedCycles;
            availableCycles -= consumedCycles;
        }

        if (availableCycles == 0 || !CanCount(timerIndex, timer))
            return;

        uint clockSource = (uint)(timer.Mode >> 8) & 3;
        if (timerIndex < 2 && (clockSource & 1) != 0)
            return;

        ulong ticks = availableCycles;
        if (timerIndex == 2 && (clockSource & 2) != 0)
        {
            ulong accumulated = timer.DividerRemainder + availableCycles;
            ticks = accumulated / 8;
            timer.DividerRemainder = accumulated % 8;
        }

        if (ticks != 0)
            Advance(timerIndex, timer, ticks);
    }

    private bool SetBlankSignal(int timerIndex, bool active)
    {
        TimerState timer = _timers[timerIndex];
        bool risingEdge = active && !timer.BlankActive;
        timer.BlankActive = active;

        if (!risingEdge || (timer.Mode & 1) == 0)
            return risingEdge;

        int synchronizationMode = (timer.Mode >> 1) & 3;
        if (synchronizationMode is 1 or 2)
        {
            timer.Counter = 0;
            timer.DividerRemainder = 0;
        }
        else if (synchronizationMode == 3)
        {
            timer.SyncReleased = true;
        }

        return true;
    }

    private static bool CanCount(int timerIndex, TimerState timer)
    {
        bool synchronizationEnabled = (timer.Mode & 1) != 0;
        if (!synchronizationEnabled)
            return true;

        int synchronizationMode = (timer.Mode >> 1) & 3;
        if (timerIndex == 2)
            return synchronizationMode is 1 or 2;

        return synchronizationMode switch
        {
            0 => !timer.BlankActive,
            1 => true,
            2 => timer.BlankActive,
            3 => timer.SyncReleased,
            _ => false,
        };
    }

    private void Advance(int timerIndex, TimerState timer, ulong ticks)
    {
        const ushort eventModeMask =
            ResetAtTargetBit |
            IrqAtTargetBit |
            IrqAtOverflowBit;
        if ((timer.Mode & eventModeMask) == 0 &&
            timer.ReachedTarget &&
            timer.ReachedOverflow)
        {
            timer.Counter = (ushort)(timer.Counter + ticks);
            return;
        }

        ulong targetHits;
        ulong overflowHits;

        if ((timer.Mode & ResetAtTargetBit) != 0)
        {
            AdvanceWithTargetReset(
                timer,
                ticks,
                out targetHits,
                out overflowHits);
        }
        else
        {
            targetHits = CountHits(timer.Counter, timer.Target, ticks, 0x1_0000);
            overflowHits = CountHits(timer.Counter, 0xFFFF, ticks, 0x1_0000);
            timer.Counter = (ushort)(timer.Counter + ticks);
        }

        if (targetHits != 0)
            timer.ReachedTarget = true;

        if (overflowHits != 0)
            timer.ReachedOverflow = true;

        ulong irqEvents = 0;
        if ((timer.Mode & IrqAtTargetBit) != 0)
            irqEvents += targetHits;

        if ((timer.Mode & IrqAtOverflowBit) != 0)
        {
            irqEvents = timer.Target == 0xFFFF &&
                        (timer.Mode & IrqAtTargetBit) != 0
                ? Math.Max(irqEvents, overflowHits)
                : irqEvents + overflowHits;
        }

        TriggerInterrupt(timerIndex, timer, irqEvents);
    }

    private static void AdvanceWithTargetReset(
        TimerState timer,
        ulong ticks,
        out ulong targetHits,
        out ulong overflowHits)
    {
        targetHits = 0;
        overflowHits = 0;

        if (timer.Counter > timer.Target)
        {
            ulong ticksToWrap = 0x1_0000ul - timer.Counter;
            ulong firstOverflow = 0xFFFFul - timer.Counter;

            if (ticks <= firstOverflow)
            {
                overflowHits = ticks == firstOverflow ? 1ul : 0ul;
                timer.Counter = (ushort)(timer.Counter + ticks);
                return;
            }

            overflowHits = 1;
            if (ticks < ticksToWrap)
            {
                timer.Counter = (ushort)(timer.Counter + ticks);
                return;
            }

            ticks -= ticksToWrap;
            timer.Counter = 0;
        }

        ulong period = (ulong)timer.Target + 1;
        targetHits = CountHits(timer.Counter, timer.Target, ticks, period);
        timer.Counter = (ushort)((timer.Counter + ticks) % period);
    }

    private static ulong CountHits(
        ushort current,
        ushort eventValue,
        ulong ticks,
        ulong period)
    {
        ulong firstHit = eventValue >= current
            ? (ulong)eventValue - current
            : period - current + eventValue;

        if (firstHit == 0)
            firstHit = period;

        return ticks < firstHit
            ? 0
            : 1 + (ticks - firstHit) / period;
    }

    private void TriggerInterrupt(
        int timerIndex,
        TimerState timer,
        ulong eventCount)
    {
        if (eventCount == 0)
            return;

        bool repeat = (timer.Mode & IrqRepeatBit) != 0;
        if (!repeat)
        {
            if (!timer.IrqArmed)
                return;

            eventCount = 1;
            timer.IrqArmed = false;
        }

        bool requestInterrupt;
        if ((timer.Mode & IrqToggleBit) == 0)
        {
            timer.InterruptRequestHigh = false;
            timer.PulseRestorePending = true;
            requestInterrupt = true;
        }
        else
        {
            bool startedHigh = timer.InterruptRequestHigh;
            requestInterrupt = startedHigh || eventCount > 1;

            if ((eventCount & 1) != 0)
                timer.InterruptRequestHigh = !timer.InterruptRequestHigh;
        }

        if (requestInterrupt)
        {
            _interruptController.Request(
                (InterruptSource)((int)InterruptSource.Timer0 + timerIndex));
        }
    }

    private ushort ReadRegister(uint address, bool clearFlags)
    {
        (int timerIndex, uint registerOffset) = DecodeAddress(address);
        TimerState timer = _timers[timerIndex];

        return registerOffset switch
        {
            CounterOffset => timer.Counter,
            ModeOffset => ReadMode(timer, clearFlags),
            TargetOffset => timer.Target,
            _ => throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence aos timers."),
        };
    }

    private static ushort ReadMode(TimerState timer, bool clearFlags)
    {
        ushort value = ComposeMode(timer);

        if (clearFlags)
        {
            timer.ReachedTarget = false;
            timer.ReachedOverflow = false;
        }

        return value;
    }

    private void WriteRegister(uint address, ushort value)
    {
        (int timerIndex, uint registerOffset) = DecodeAddress(address);
        TimerState timer = _timers[timerIndex];

        switch (registerOffset)
        {
            case CounterOffset:
                timer.Counter = value;
                timer.HoldCycles = 2;
                break;

            case ModeOffset:
                timer.Mode = (ushort)(value & WritableModeMask);
                timer.Counter = 0;
                timer.DividerRemainder = 0;
                timer.HoldCycles = 2;
                timer.InterruptRequestHigh = true;
                timer.PulseRestorePending = false;
                timer.ReachedTarget = false;
                timer.ReachedOverflow = false;
                timer.IrqArmed = true;
                timer.SyncReleased = false;
                break;

            case TargetOffset:
                timer.Target = value;
                break;

            default:
                throw new InvalidOperationException(
                    $"Endereço 0x{address:X8} não pertence aos timers.");
        }
    }

    private static ushort ComposeMode(TimerState timer)
    {
        ushort value = timer.Mode;

        if (timer.InterruptRequestHigh)
            value |= InterruptRequestBit;

        if (timer.ReachedTarget)
            value |= ReachedTargetBit;

        if (timer.ReachedOverflow)
            value |= ReachedOverflowBit;

        return value;
    }

    private static (int TimerIndex, uint RegisterOffset) DecodeAddress(uint address)
    {
        uint relativeAddress = address - Timer0BaseAddress;
        int timerIndex = (int)(relativeAddress / TimerStride);
        uint registerOffset = (relativeAddress % TimerStride) & ~3u;

        if ((uint)timerIndex >= 3 || registerOffset > TargetOffset)
        {
            throw new InvalidOperationException(
                $"Endereço 0x{address:X8} não pertence aos timers.");
        }

        return (timerIndex, registerOffset);
    }

    private TimerState GetTimer(int timerIndex)
    {
        if ((uint)timerIndex >= _timers.Length)
            throw new ArgumentOutOfRangeException(nameof(timerIndex));

        return _timers[timerIndex];
    }

    private sealed class TimerState
    {
        public ushort Counter;
        public ushort Mode;
        public ushort Target;
        public bool InterruptRequestHigh;
        public bool ReachedTarget;
        public bool ReachedOverflow;
        public bool IrqArmed;
        public bool PulseRestorePending;
        public bool BlankActive;
        public bool SyncReleased;
        public uint HoldCycles;
        public ulong DividerRemainder;

        public void Reset()
        {
            Counter = 0;
            Mode = 0;
            Target = 0;
            InterruptRequestHigh = true;
            ReachedTarget = false;
            ReachedOverflow = false;
            IrqArmed = true;
            PulseRestorePending = false;
            BlankActive = false;
            SyncReleased = false;
            HoldCycles = 0;
            DividerRemainder = 0;
        }
    }
}
