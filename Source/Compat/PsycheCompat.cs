using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BreakTimer
{
    // Optional integration with the Psyche mod. When a pawn carries a psyche scar tied to one of
    // its traits, Psyche throttles that trait's mental-state rolls by the scar's current severity,
    // so the real MTB is the trait's base MTB divided by that chance factor. Resolved by reflection
    // against a single general Psyche entry point (see Psyche ADR 0013); Break Timer never names a
    // trait or mental state. Absent or failed reflection yields factor 1, i.e. the trait's raw MTB.
    public static class PsycheCompat
    {
        static bool resolved;
        static Func<Pawn, Trait, MentalStateDef, float>? chanceFactor;

        // True only when the Psyche API resolved. Callers gate on this to skip the factor path
        // entirely when Psyche is absent, so an uninstalled Psyche costs one bool read and nothing
        // else. Reading it also primes the one-time reflection scan.
        public static bool Available
        {
            get
            {
                EnsureResolved();
                return chanceFactor != null;
            }
        }

        // Resolve the binding now (at startup) so the reflection scan never lands on a gameplay
        // path. Idempotent.
        public static void Prime() => EnsureResolved();

        // Psyche's multiplier on the per-roll chance of this trait giving this mental state: 1 when
        // Psyche is absent, the API is unavailable, or no scar applies; below 1 as a scar throttles
        // the roll. Returns 1 on any reflection failure so the base MTB shows through unchanged.
        public static float MentalStateChanceFactor(Pawn pawn, Trait trait, MentalStateDef state)
        {
            EnsureResolved();
            if (chanceFactor == null || pawn == null || trait == null || state == null) return 1f;
            try { return chanceFactor(pawn, trait, state); }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] Psyche MentalStateChanceFactor threw: {ex.Message}",
                    Once.Id("psyche-chancefactor"));
                return 1f;
            }
        }

        static void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;

            Type? type = AccessTools.TypeByName("Psyche.PsycheScarEffects");
            MethodInfo? method = type != null
                ? AccessTools.Method(type, "MentalStateChanceFactor",
                    new[] { typeof(Pawn), typeof(Trait), typeof(MentalStateDef) })
                : null;
            if (method != null && method.ReturnType == typeof(float))
                chanceFactor = (Func<Pawn, Trait, MentalStateDef, float>)Delegate.CreateDelegate(
                    typeof(Func<Pawn, Trait, MentalStateDef, float>), method);
        }
    }
}
