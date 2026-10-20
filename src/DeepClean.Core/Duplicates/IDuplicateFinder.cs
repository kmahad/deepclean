namespace DeepClean.Core.Duplicates;

/// <summary>
/// Finds files that hold exactly the same bytes. A scan only ever reads: nothing is reported as
/// removable by itself, and nothing is removed here.
/// </summary>
public interface IDuplicateFinder
{
    Task<DuplicateScanResult> FindAsync(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken);
}
