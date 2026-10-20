using DeepClean.Core.Cleaning;

namespace DeepClean.Core.Duplicates;

/// <summary>
/// Removes the copies the user picked. Kept apart from the finder, so the part that looks can
/// never remove anything, and every copy still goes through the same safety re-check as the
/// junk cleaner before it is touched.
/// </summary>
public interface IDuplicateCleaner
{
    Task<CleanupResult> DeleteAsync(
        IReadOnlyCollection<DuplicateCandidate> candidates,
        CleanupOptions options,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken);
}
