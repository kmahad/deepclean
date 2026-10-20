namespace DeepClean.Core.Cleaning;

/// <summary>How a single item ended up.</summary>
public enum CleanupOutcome
{
    /// <summary>Moved to the Recycle Bin and still recoverable.</summary>
    Recycled,

    /// <summary>Deleted outright. Only used when the user asked for it.</summary>
    Deleted,

    /// <summary>Left alone, for example because a file was in use or the safety checks said no.</summary>
    Skipped,

    /// <summary>Windows refused the operation. The item is still there.</summary>
    Failed,
}

/// <summary>
/// One line of the report shown after a clean. The category is null when the removal did not come
/// from a junk category, as is the case for the copies the duplicate finder removes.
/// </summary>
public sealed record CleanupItemResult(
    string Path,
    JunkCategory? Category,
    long SizeBytes,
    CleanupOutcome Outcome,
    string? Message)
{
    public bool IsReclaimed => Outcome is CleanupOutcome.Recycled or CleanupOutcome.Deleted;
}

/// <summary>The full outcome of a clean, including everything it decided not to do.</summary>
public sealed class CleanupResult
{
    public CleanupResult(IReadOnlyList<CleanupItemResult> items, bool wasCancelled, TimeSpan duration)
    {
        Items = items;
        WasCancelled = wasCancelled;
        Duration = duration;
    }

    public static CleanupResult Empty { get; } = new([], false, TimeSpan.Zero);

    public IReadOnlyList<CleanupItemResult> Items { get; }

    /// <summary>True when the user stopped the clean before it finished.</summary>
    public bool WasCancelled { get; }

    public TimeSpan Duration { get; }

    public int RecycledCount => Items.Count(item => item.Outcome == CleanupOutcome.Recycled);

    public int DeletedCount => Items.Count(item => item.Outcome == CleanupOutcome.Deleted);

    public int SkippedCount => Items.Count(item => item.Outcome == CleanupOutcome.Skipped);

    public int FailedCount => Items.Count(item => item.Outcome == CleanupOutcome.Failed);

    /// <summary>Bytes that actually left their original location.</summary>
    public long ReclaimedBytes => Items.Where(item => item.IsReclaimed).Sum(item => item.SizeBytes);

    /// <summary>Bytes that were reported but deliberately left on disk.</summary>
    public long SkippedBytes => Items.Where(item => !item.IsReclaimed).Sum(item => item.SizeBytes);

    public IReadOnlyList<CleanupItemResult> Failures =>
        [.. Items.Where(item => item.Outcome == CleanupOutcome.Failed)];

    public IReadOnlyList<CleanupItemResult> Skips =>
        [.. Items.Where(item => item.Outcome == CleanupOutcome.Skipped)];
}
