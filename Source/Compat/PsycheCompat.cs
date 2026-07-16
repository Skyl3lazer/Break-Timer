using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BreakTimer
{
    // Optional integration with the Psyche mod.
    public static class PsycheCompat
    {
        static bool resolved;
        static Func<Pawn, Trait, MentalStateDef, float>? chanceFactor;

        public static bool Available
        {
            get
            {
                EnsureResolved();
                return chanceFactor != null;
            }
        }

        public static void Prime() => EnsureResolved();

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
