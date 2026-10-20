using System.Diagnostics;
using DeepClean.Core.Cleaning;
using Microsoft.Extensions.Logging;

namespace DeepClean.Core.Duplicates;

/// <summary>
/// Removes the copies the user picked. Each one goes through the same safety re-check and the same
/// Recycle Bin default as the junk cleaner, and a copy another program is holding is skipped
/// rather than forced.
/// </summary>
public sealed class DuplicateCleaner : IDuplicateCleaner
{
    private readonly IFileRemover _remover;
    private readonly ILogger<DuplicateCleaner>? _logger;
    private readonly IReadOnlyCollection<string>? _personalFolders;

    /// <param name="personalFolders">
    /// Which folders count as the user's own, where a copy may only be recycled. Null, the normal
    /// case, uses the folders Windows reports.
    /// </param>
    public DuplicateCleaner(
        IFileRemover remover,
        ILogger<DuplicateCleaner>? logger = null,
        IReadOnlyCollection<string>? personalFolders = null)
    {
        _remover = remover ?? throw new ArgumentNullException(nameof(remover));
        _logger = logger;
        _personalFolders = personalFolders;
    }

    public Task<CleanupResult> DeleteAsync(
        IReadOnlyCollection<DuplicateCandidate> candidates,
        CleanupOptions options,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(() => Delete(candidates, options, progress, cancellationToken), cancellationToken);
    }

    private CleanupResult Delete(
        IReadOnlyCollection<DuplicateCandidate> candidates,
        CleanupOptions options,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<CleanupItemResult>(candidates.Count);
        var total = candidates.Count;
        var completed = 0;
        var reclaimed = 0L;
        var cancelled = false;

        _logger?.LogInformation(
            "Removing {Count} duplicate copies ({Mode})",
            total,
            options.PermanentlyDelete ? "permanent delete" : "recycle bin");

        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var outcome = ItemRemoval.Remove(
                candidate.Path,
                candidate.RootPath,
                isDirectory: false,
                options,
                _remover,
                _logger,
                forDuplicates: true,
                _personalFolders,
                out var message);

            if (outcome is CleanupOutcome.Recycled or CleanupOutcome.Deleted)
            {
                reclaimed += candidate.SizeBytes;
            }

            results.Add(new CleanupItemResult(candidate.Path, null, candidate.SizeBytes, outcome, message));
            completed++;

            progress?.Report(new CleanupProgress(completed, total, candidate.Path, reclaimed));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        stopwatch.Stop();

        var result = new CleanupResult(results, cancelled, stopwatch.Elapsed);

        _logger?.LogInformation(
            "Duplicate removal finished: {Reclaimed} removed, {Skipped} skipped, {Failed} failed in {Elapsed}",
            result.RecycledCount + result.DeletedCount,
            result.SkippedCount,
            result.FailedCount,
            stopwatch.Elapsed);

        return result;
    }
}
