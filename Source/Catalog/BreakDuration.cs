using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
    // Immutable view of a mental state's recovery profile - min/expected/max in ticks and days,
    // derived from the vanilla recovery fields (minTicksBeforeRecovery, recoveryMtbDays, the cap).
    public sealed class BreakDuration
    {
        public const float DefaultRecoveryMtbDays = 1f;
        public const int DefaultMinTicksBeforeRecovery = 500;
        public const int DefaultMaxTicksBeforeRecovery = 99999999;

        public BreakDuration(MentalStateDef state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            MinTicks = Mathf.Max(0, state.minTicksBeforeRecovery);
            MaxTicks = Mathf.Max(MinTicks, state.maxTicksBeforeRecovery);
            RecoveryMtbDays = state.recoveryMtbDays;
            RecoverFromSleep = state.recoverFromSleep;
            RecoverFromDowned = state.recoverFromDowned;
            RecoverFromCaptured = state.recoverFromCaptured;
            RecoverFromCollapsingExhausted = state.recoverFromCollapsingExhausted;
            ExpectedTicks = ComputeExpectedTicks(MinTicks, MaxTicks, RecoveryMtbDays);
            HasUnboundedMax = MaxTicks >= DefaultMaxTicksBeforeRecovery;
        }

        public MentalStateDef State { get; }
        public int MinTicks { get; }
        public int MaxTicks { get; }
        public float RecoveryMtbDays { get; }
        public int ExpectedTicks { get; }
        public bool HasUnboundedMax { get; }

        public bool RecoverFromSleep { get; }
        public bool RecoverFromDowned { get; }
        public bool RecoverFromCaptured { get; }
        public bool RecoverFromCollapsingExhausted { get; }

        public float MinDays => TicksToDays(MinTicks);
        public float MaxDays => TicksToDays(MaxTicks);
        public float ExpectedDays => TicksToDays(ExpectedTicks);

        // True when the MTB recovery roll is disabled and only the hard cap ends the state.
        public bool RecoveryIsDeterministic => RecoveryMtbDays <= 0f;

        // Expected total ticks assuming nothing interrupts (sleep, downed): the MTB average
        // shifted by the eligibility window, capped.
        static int ComputeExpectedTicks(int minTicks, int maxTicks, float recoveryMtbDays)
        {
            if (recoveryMtbDays <= 0f)
                return maxTicks;

            long expected = minTicks + (long)Mathf.Round(recoveryMtbDays * GenDate.TicksPerDay);
            if (expected > maxTicks) expected = maxTicks;
            if (expected < minTicks) expected = minTicks;
            return (int)expected;
        }

        // For an in-progress state, returns min/expected/max remaining ticks honoring its
        // current Age and any forceRecoverAfterTicks override.
        public BreakDurationRemaining GetRemaining(MentalState active)
        {
            if (active is null) throw new ArgumentNullException(nameof(active));

            int age = Mathf.Max(0, active.Age);
            int hardCap = MaxTicks;
            if (active.forceRecoverAfterTicks > 0 && active.forceRecoverAfterTicks < hardCap)
                hardCap = active.forceRecoverAfterTicks;

            int maxRemaining = Mathf.Max(0, hardCap - age);
            int minRemaining = Mathf.Max(0, MinTicks - age);

            int expectedRemaining;
            if (RecoveryIsDeterministic)
            {
                expectedRemaining = maxRemaining;
            }
            else
            {
                long mtb = (long)Mathf.Round(RecoveryMtbDays * GenDate.TicksPerDay);
                long est = minRemaining + mtb;
                if (est > maxRemaining) est = maxRemaining;
                if (est < 0) est = 0;
                expectedRemaining = (int)est;
            }

            return new BreakDurationRemaining(minRemaining, expectedRemaining, maxRemaining);
        }

        static float TicksToDays(int ticks) => ticks / (float)GenDate.TicksPerDay;

        public override string ToString()
            => $"BreakDuration(min={MinTicks}t expected={ExpectedTicks}t max={(HasUnboundedMax ? "unbounded" : MaxTicks + "t")} mtb={RecoveryMtbDays:0.##}d)";
    }

    public readonly struct BreakDurationRemaining
    {
        public BreakDurationRemaining(int minTicks, int expectedTicks, int maxTicks)
        {
            MinTicks = minTicks;
            ExpectedTicks = expectedTicks;
            MaxTicks = maxTicks;
        }

        public int MinTicks { get; }
        public int ExpectedTicks { get; }
        public int MaxTicks { get; }
    }
}
