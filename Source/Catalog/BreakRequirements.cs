using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BreakTimer
{
    // Declarative gates on whether a break can happen to a pawn, pulled from the
    // MentalBreakDef and its MentalStateDef. Mirrors what MentalBreakWorker.BreakCanOccur
    // and MentalStateWorker.StateCanOccur evaluate at runtime.
    public sealed class BreakRequirements
    {
        public BreakRequirements(MentalBreakDef breakDef)
        {
            if (breakDef is null) throw new ArgumentNullException(nameof(breakDef));

            RequiredTrait = breakDef.requiredTrait;
            RequiredGene = breakDef.requiredGene;
            RequiredPrecept = breakDef.requiredPrecept;
            AnomalousBreak = breakDef.anomalousBreak;
            QuestLodgersCanDo = breakDef.questLodgersCanDo;
            LayerWhitelist = breakDef.layerWhitelist != null && breakDef.layerWhitelist.Count > 0
                ? breakDef.layerWhitelist.ToArray()
                : Array.Empty<PlanetLayerDef>();

            MentalStateDef state = breakDef.mentalState;
            if (state != null)
            {
                ColonistsOnly = state.colonistsOnly;
                SlavesOnly = state.slavesOnly;
                PrisonersCanDo = state.prisonersCanDo;
                SlavesCanDo = state.slavesCanDo;
                InCaravanCanDo = state.inCaravanCanDo;
                DownedCanDo = state.downedCanDo;
                UnspawnedNotInCaravanCanDo = state.unspawnedNotInCaravanCanDo;
                AllowGuilty = state.allowGuilty;
                RequiredCapacities = state.requiredCapacities != null && state.requiredCapacities.Count > 0
                    ? state.requiredCapacities.ToArray()
                    : Array.Empty<PawnCapacityDef>();
            }
            else
            {
                PrisonersCanDo = true;
                SlavesCanDo = true;
                AllowGuilty = true;
                RequiredCapacities = Array.Empty<PawnCapacityDef>();
            }
        }

        public TraitDef? RequiredTrait { get; }
        public GeneDef? RequiredGene { get; }
        public PreceptDef? RequiredPrecept { get; }
        public bool AnomalousBreak { get; }
        public bool QuestLodgersCanDo { get; }
        public IReadOnlyList<PlanetLayerDef> LayerWhitelist { get; }

        public bool ColonistsOnly { get; }
        public bool SlavesOnly { get; }
        public bool PrisonersCanDo { get; }
        public bool SlavesCanDo { get; }
        public bool InCaravanCanDo { get; }
        public bool DownedCanDo { get; }
        public bool UnspawnedNotInCaravanCanDo { get; }
        public bool AllowGuilty { get; }
        public IReadOnlyList<PawnCapacityDef> RequiredCapacities { get; }

        // True when no declarative requirement blocks the pawn.
        public bool DeclarativelyAllowsPawn(Pawn pawn)
        {
            if (pawn is null) return false;
            return !GetUnmetReasons(pawn).Any();
        }

        // Human-readable reasons this break's declarative requirements would block the pawn;
        // empty when all are satisfied.
        public IEnumerable<string> GetUnmetReasons(Pawn pawn)
        {
            if (pawn is null) yield break;

            if (RequiredTrait != null && (pawn.story == null || !pawn.story.traits.HasTrait(RequiredTrait)))
                yield return "BreakTimer.ReqTrait".Translate(RequiredTrait.LabelCap.ToString());

            if (RequiredGene != null && (pawn.genes == null || !pawn.genes.HasActiveGene(RequiredGene)))
                yield return "BreakTimer.ReqGene".Translate(RequiredGene.LabelCap.ToString());

            if (RequiredPrecept != null && (pawn.Ideo == null || !pawn.Ideo.HasPrecept(RequiredPrecept)))
                yield return "BreakTimer.ReqPrecept".Translate(RequiredPrecept.LabelCap.ToString());

            if (AnomalousBreak && !ModsConfig.AnomalyActive)
                yield return "BreakTimer.ReqAnomaly".Translate();

            if (ColonistsOnly && pawn.Faction != Faction.OfPlayer)
                yield return "BreakTimer.ReqColonistsOnly".Translate();

            if (SlavesOnly && !pawn.IsSlaveOfColony)
                yield return "BreakTimer.ReqSlavesOnly".Translate();

            if (!PrisonersCanDo && pawn.IsPrisoner)
                yield return "BreakTimer.ReqNoPrisoners".Translate();

            if (!SlavesCanDo && pawn.IsSlave)
                yield return "BreakTimer.ReqNoSlaves".Translate();

            if (!QuestLodgersCanDo && pawn.IsQuestLodger())
                yield return "BreakTimer.ReqNoQuestLodgers".Translate();

            if (!DownedCanDo && pawn.Downed)
                yield return "BreakTimer.ReqDowned".Translate();

            if (RequiredCapacities.Count > 0 && pawn.health?.capacities != null)
            {
                foreach (PawnCapacityDef cap in RequiredCapacities)
                {
                    if (!pawn.health.capacities.CapableOf(cap))
                        yield return "BreakTimer.ReqCapacity".Translate(cap.LabelCap.ToString());
                }
            }

            if (LayerWhitelist.Count > 0 && pawn.SpawnedOrAnyParentSpawned)
            {
                var heldMap = pawn.MapHeld;
                if (heldMap == null || !heldMap.Tile.Valid || !LayerWhitelist.Contains(heldMap.Tile.LayerDef))
                    yield return "BreakTimer.ReqLayer".Translate();
            }
        }
    }
}
