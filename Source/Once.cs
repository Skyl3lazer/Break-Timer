namespace BreakTimer
{
	// Stable per-session dedup IDs for Log.ErrorOnce / Log.WarningOnce. Only needs to be
	// unique per logical call site within a run, so a hash of a readable key does the job.
	internal static class Once
	{
		public static int Id(string category) => category.GetHashCode();

		public static int Id(string category, string? key)
			=> unchecked(category.GetHashCode() * 397 ^ (key?.GetHashCode() ?? 0));
	}
}
