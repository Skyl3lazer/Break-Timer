using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
	/// <summary>
	/// Persisted record of an in-progress mental break on a pawn. Created when
	/// <c>MentalStateHandler.TryStartMentalState</c> reports success and cleared when the
	/// matching <c>MentalState.RecoverFromState</c> fires.
	/// </summary>
	/// <remarks>
	/// We persist the absolute tick the break began even though <see cref="MentalState.Age"/>
	/// also tracks elapsed time, because: (1) it lets us survive a save where the patch was
	/// absent on the prior session (we backfill from <c>TicksGame - state.age</c>),
	/// (2) downstream UI can format absolute timestamps ("started yesterday at 06:00"),
	/// and (3) keeping the metadata here decouples the timer from MentalState internals.
	/// </remarks>
	public sealed class ActiveBreakRecord : IExposable
	{
		public MentalStateDef? stateDef;
		public MentalBreakDef? breakDef;
		public int startTick;
		public bool causedByMood;
		public bool causedByDamage;
		public bool causedByPsycast;
		public Pawn? causedByPawn;
		public string? reason;

		public ActiveBreakRecord() { }

		public ActiveBreakRecord(
			MentalStateDef stateDef,
			MentalBreakDef? breakDef,
			int startTick,
			bool causedByMood,
			bool causedByDamage,
			bool causedByPsycast,
			Pawn? causedByPawn,
			string? reason)
		{
			this.stateDef = stateDef;
			this.breakDef = breakDef;
			this.startTick = startTick;
			this.causedByMood = causedByMood;
			this.causedByDamage = causedByDamage;
			this.causedByPsycast = causedByPsycast;
			this.causedByPawn = causedByPawn;
			this.reason = reason;
		}

		public int TicksElapsed(int nowTick) => Mathf.Max(0, nowTick - startTick);

		public void ExposeData()
		{
			Scribe_Defs.Look(ref stateDef, "stateDef");
			Scribe_Defs.Look(ref breakDef, "breakDef");
			Scribe_Values.Look(ref startTick, "startTick", 0);
			Scribe_Values.Look(ref causedByMood, "causedByMood", defaultValue: false);
			Scribe_Values.Look(ref causedByDamage, "causedByDamage", defaultValue: false);
			Scribe_Values.Look(ref causedByPsycast, "causedByPsycast", defaultValue: false);
			Scribe_References.Look(ref causedByPawn, "causedByPawn");
			Scribe_Values.Look(ref reason, "reason");
		}
	}

	/// <summary>
	/// Persisted snapshot of a break that has concluded. Held in the per-pawn history
	/// ring buffer for stats / UI ("last 5 breaks") without paying for unlimited growth.
	/// </summary>
	public sealed class CompletedBreakRecord : IExposable
	{
		public MentalStateDef? stateDef;
		public MentalBreakDef? breakDef;
		public int startTick;
		public int endTick;
		public bool causedByMood;
		public bool causedByDamage;
		public bool causedByPsycast;

		public CompletedBreakRecord() { }

		public CompletedBreakRecord(ActiveBreakRecord active, int endTick)
		{
			stateDef = active.stateDef;
			breakDef = active.breakDef;
			startTick = active.startTick;
			this.endTick = endTick;
			causedByMood = active.causedByMood;
			causedByDamage = active.causedByDamage;
			causedByPsycast = active.causedByPsycast;
		}

		public int DurationTicks => Mathf.Max(0, endTick - startTick);

		public void ExposeData()
		{
			Scribe_Defs.Look(ref stateDef, "stateDef");
			Scribe_Defs.Look(ref breakDef, "breakDef");
			Scribe_Values.Look(ref startTick, "startTick", 0);
			Scribe_Values.Look(ref endTick, "endTick", 0);
			Scribe_Values.Look(ref causedByMood, "causedByMood", defaultValue: false);
			Scribe_Values.Look(ref causedByDamage, "causedByDamage", defaultValue: false);
			Scribe_Values.Look(ref causedByPsycast, "causedByPsycast", defaultValue: false);
		}
	}

	/// <summary>
	/// Bounded per-pawn ring buffer of completed breaks. Lives behind a wrapper so the
	/// containing <c>Dictionary&lt;Pawn, PawnBreakHistory&gt;</c> can go through
	/// <see cref="Scribe_Collections"/> with <see cref="LookMode.Deep"/> on values.
	/// </summary>
	public sealed class PawnBreakHistory : IExposable
	{
		public List<CompletedBreakRecord> records = new();

		public PawnBreakHistory() { }

		public int Count => records?.Count ?? 0;

		public void Add(CompletedBreakRecord rec, int cap)
		{
			records ??= new List<CompletedBreakRecord>(cap);
			records.Add(rec);
			while (records.Count > cap)
				records.RemoveAt(0);
		}

		public void ExposeData()
		{
			Scribe_Collections.Look(ref records, "records", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				records ??= new List<CompletedBreakRecord>();
		}
	}
}
