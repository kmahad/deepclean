namespace DeepClean.Core.Duplicates;

/// <summary>The outcome of a duplicate scan, including everything it decided not to look at.</summary>
public sealed class DuplicateScanResult
{
    public DuplicateScanResult(
        IReadOnlyList<DuplicateFileGroup> groups,
        int fileCount,
        bool wasCancelled,
        bool hitFileLimit,
        TimeSpan duration)
    {
        Groups = groups;
        FileCount = fileCount;
        WasCancelled = wasCancelled;
        HitFileLimit = hitFileLimit;
        Duration = duration;
    }

    public static DuplicateScanResult Empty { get; } = new([], 0, false, false, TimeSpan.Zero);

    /// <summary>Only the groups that really do hold more than one copy of the same bytes.</summary>
    public IReadOnlyList<DuplicateFileGroup> Groups { get; }

    /// <summary>How many files were considered at all, copies and originals together.</summary>
    public int FileCount { get; }

    /// <summary>True when the user stopped the scan before it finished.</summary>
    public bool WasCancelled { get; }

    /// <summary>True when the scan ran out of its file allowance before it was done.</summary>
    public bool HitFileLimit { get; }

    public TimeSpan Duration { get; }

    public bool IsEmpty => Groups.Count == 0;

    public int GroupCount => Groups.Count;

    /// <summary>How many files are copies of something else, so how many could go.</summary>
    public int DuplicateFileCount => Groups.Sum(group => group.Candidates.Count - 1);

    /// <summary>Bytes that would come back if one copy of every group was kept.</summary>
    public long WastedBytes => Groups.Sum(group => group.WastedBytes);
}
