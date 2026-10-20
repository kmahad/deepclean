namespace DeepClean.Core.Cleaning;

/// <summary>Scans for removable junk and, once the user picks something, removes it.</summary>
public interface ISystemCleaner
{
    /// <summary>Every location this cleaner knows how to look at on this machine.</summary>
    IReadOnlyList<JunkRule> Rules { get; }

    /// <summary>
    /// The rules worth running right now: those in the requested categories that do not need
    /// rights the process does not have.
    /// </summary>
    IReadOnlyList<JunkRule> GetActiveRules(IReadOnlyCollection<JunkCategory>? categories, bool isElevated);

    /// <summary>
    /// Walks every active rule and reports what could be removed. Never changes a file.
    /// </summary>
    Task<JunkScanResult> ScanAsync(
        JunkScanOptions options,
        IProgress<JunkScanProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the given items. Each item is re-checked against its originating rule before
    /// anything happens to it.
    /// </summary>
    Task<CleanupResult> CleanAsync(
        IReadOnlyCollection<JunkItem> items,
        CleanupOptions options,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken);
}
