using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using Microsoft.Extensions.Logging;

namespace DeepClean.Core.Cleaning;

/// <summary>
/// Walks the allow listed folders and reports what could go. The scan itself is strictly
/// read only; nothing is removed until the user picks items and confirms, and every item is
/// checked again against its originating rule at that point.
/// </summary>
public sealed class FileSystemCleaner : ISystemCleaner
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(150);

    private readonly IFileRemover _remover;
    private readonly ILogger<FileSystemCleaner>? _logger;

    public FileSystemCleaner(IFileRemover remover, ILogger<FileSystemCleaner>? logger = null)
        : this(remover, JunkRuleCatalog.Create(), logger)
    {
    }

    /// <summary>Builds a cleaner over an explicit rule list, so the walking logic can be tested in isolation.</summary>
    public FileSystemCleaner(IFileRemover remover, IReadOnlyList<JunkRule> rules, ILogger<FileSystemCleaner>? logger = null)
    {
        _remover = remover ?? throw new ArgumentNullException(nameof(remover));
        _logger = logger;
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public IReadOnlyList<JunkRule> Rules { get; }

    public IReadOnlyList<JunkRule> GetActiveRules(IReadOnlyCollection<JunkCategory>? categories, bool isElevated)
    {
        return
        [
            .. Rules.Where(rule =>
                (categories is null || categories.Count == 0 || categories.Contains(rule.Category))
                && (!rule.RequiresElevation || isElevated)),
        ];
    }

    public Task<JunkScanResult> ScanAsync(
        JunkScanOptions options,
        IProgress<JunkScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(() => Scan(options, progress, cancellationToken), cancellationToken);
    }

    public Task<CleanupResult> CleanAsync(
        IReadOnlyCollection<JunkItem> items,
        CleanupOptions options,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(() => Clean(items, options, progress, cancellationToken), cancellationToken);
    }

    private JunkScanResult Scan(
        JunkScanOptions options,
        IProgress<JunkScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var activeRules = GetActiveRules(options.Categories, options.IsElevated);
        var state = new ScanState();
        var cancelled = false;

        _logger?.LogInformation(
            "Scan started for {RuleCount} rules in {CategoryCount} categories",
            activeRules.Count,
            activeRules.Select(rule => rule.Category).Distinct().Count());

        foreach (var rule in activeRules)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            if (!Directory.Exists(rule.TargetPath))
            {
                continue;
            }

            progress?.Report(new JunkScanProgress(
                rule.Category,
                rule.Title,
                state.Items.Count,
                state.TotalBytes,
                rule.TargetPath));

            try
            {
                WalkRule(rule, options, state, progress, cancellationToken);

                if (state.HitItemLimit)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                // A folder that cannot be read is not a reason to abandon the whole scan.
                _logger?.LogWarning(ex, "Scanning {Path} stopped early", rule.TargetPath);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        stopwatch.Stop();

        _logger?.LogInformation(
            "Scan finished with {Count} items ({Bytes} bytes) in {Elapsed}",
            state.Items.Count,
            state.TotalBytes,
            stopwatch.Elapsed);

        return new JunkScanResult(state.Items, cancelled, state.HitItemLimit, stopwatch.Elapsed);
    }

    private void WalkRule(
        JunkRule rule,
        JunkScanOptions options,
        ScanState state,
        IProgress<JunkScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var floorDays = Math.Max(rule.MinimumAgeDays, options.MinimumAgeDays);
        var cutoff = DateTime.UtcNow.AddDays(-floorDays);

        // Folders are only ever reported as a single unit when the rule wants everything in
        // them; a rule that names a pattern deals in files only.
        var foldersAsUnits = rule.SearchPattern is "*" or "";

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((rule.TargetPath, 0));

        var lastReport = Stopwatch.GetTimestamp();

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested || state.HitItemLimit)
            {
                return;
            }

            var (folder, depth) = pending.Pop();

            // The rule's own folder is exempt from the exclusion list, but anything below it
            // can be excluded by the user.
            if (depth > 0 && SafetyGuard.FindExclusion(folder, options.ExcludedPaths) is not null)
            {
                continue;
            }

            using var enumerator = EnumerateSafely(folder);

            if (enumerator is null)
            {
                continue;
            }

            while (true)
            {
                if (cancellationToken.IsCancellationRequested || state.HitItemLimit)
                {
                    return;
                }

                FileSystemInfo entry;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    entry = enumerator.Current;
                }
                catch (Exception ex)
                {
                    // One unreadable entry must not swallow the rest of the folder.
                    _logger?.LogDebug(ex, "Stopped listing {Folder}", folder);
                    break;
                }

                FileAttributes attributes;

                try
                {
                    attributes = entry.Attributes;
                }
                catch (Exception)
                {
                    continue;
                }

                // Links and junctions are never followed: they can point outside the allow
                // listed folder entirely, and removing one can remove its target.
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    HandleDirectory(entry, depth, rule, options, cutoff, foldersAsUnits, state, pending);
                }
                else
                {
                    HandleFile((FileInfo)entry, rule, options, cutoff, state);
                }

                if (progress is not null && HasIntervalElapsed(lastReport))
                {
                    lastReport = Stopwatch.GetTimestamp();
                    progress.Report(new JunkScanProgress(
                        rule.Category,
                        rule.Title,
                        state.Items.Count,
                        state.TotalBytes,
                        entry.FullName));
                }
            }
        }
    }

    private void HandleDirectory(
        FileSystemInfo entry,
        int depth,
        JunkRule rule,
        JunkScanOptions options,
        DateTime cutoff,
        bool foldersAsUnits,
        ScanState state,
        Stack<(string Path, int Depth)> pending)
    {
        var childDepth = depth + 1;
        var childFolder = entry.FullName;

        if (SafetyGuard.FindExclusion(childFolder, options.ExcludedPaths) is not null)
        {
            return;
        }

        if (foldersAsUnits && childDepth <= rule.MaximumDepth)
        {
            var measurement = MeasureFolder(childFolder, cutoff, options.MinimumSizeBytes);

            if (measurement is { IsEligible: true } eligible)
            {
                TryCreateItem(entry, childFolder, rule, options, eligible.SizeBytes, state);
                return;
            }
        }

        if (childDepth < rule.MaximumDepth)
        {
            pending.Push((childFolder, childDepth));
        }
    }

    private void HandleFile(
        FileInfo file,
        JunkRule rule,
        JunkScanOptions options,
        DateTime cutoff,
        ScanState state)
    {
        // The glob narrows the rule to the files it is for. Folders are still walked, so a
        // pattern can reach the same kind of file deeper inside the rule's folder.
        if (!MatchesPattern(file.Name, rule.SearchPattern))
        {
            return;
        }

        long length;

        try
        {
            length = file.Length;
        }
        catch (Exception)
        {
            return;
        }

        if (length < options.MinimumSizeBytes || file.LastWriteTimeUtc > cutoff)
        {
            return;
        }

        TryCreateItem(file, file.FullName, rule, options, length, state);
    }

    private bool TryCreateItem(
        FileSystemInfo entry,
        string path,
        JunkRule rule,
        JunkScanOptions options,
        long sizeBytes,
        ScanState state)
    {
        var check = SafetyGuard.Check(path, rule.TargetPath, options.ExcludedPaths);

        if (!check.IsAllowed)
        {
            _logger?.LogDebug("Refused {Path}: {Reason}", path, check.Reason);
            return false;
        }

        state.Items.Add(new JunkItem
        {
            Path = path,
            RootPath = rule.TargetPath,
            Category = rule.Category,
            RuleId = rule.Id,
            RuleTitle = rule.Title,
            SizeBytes = sizeBytes,
            IsDirectory = entry is DirectoryInfo,
            LastWriteTimeUtc = entry.LastWriteTimeUtc,
        });

        state.TotalBytes += sizeBytes;

        if (state.Items.Count >= options.MaximumItems)
        {
            state.HitItemLimit = true;
            _logger?.LogInformation("Scan reached the {Limit} item ceiling", options.MaximumItems);
        }

        return true;
    }

    /// <summary>
    /// Adds up a folder and decides whether it can be reported as one unit. Anything that is
    /// not clearly finished with, or that contains a link, makes the whole folder ineligible
    /// so the caller keeps walking instead.
    /// </summary>
    private static FolderMeasurement? MeasureFolder(string folder, DateTime cutoff, long minimumSizeBytes)
    {
        var total = 0L;
        var eligible = true;
        var pending = new Stack<string>();
        pending.Push(folder);

        try
        {
            while (pending.Count > 0)
            {
                var current = pending.Pop();

                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
                {
                    var attributes = entry.Attributes;

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return null;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry.FullName);
                        continue;
                    }

                    total += ((FileInfo)entry).Length;

                    if (entry.LastWriteTimeUtc > cutoff)
                    {
                        eligible = false;
                    }
                }
            }
        }
        catch (Exception)
        {
            // If any part of the folder cannot be read it is left alone entirely.
            return null;
        }

        if (total < minimumSizeBytes)
        {
            eligible = false;
        }

        return new FolderMeasurement(total, eligible);
    }

    private CleanupResult Clean(
        IReadOnlyCollection<JunkItem> items,
        CleanupOptions options,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<CleanupItemResult>(items.Count);
        var total = items.Count;
        var completed = 0;
        var reclaimed = 0L;
        var cancelled = false;

        _logger?.LogInformation(
            "Clean started for {Count} items ({Mode})",
            total,
            options.PermanentlyDelete ? "permanent delete" : "recycle bin");

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var outcome = CleanOne(item, options, out var message);

            if (outcome is CleanupOutcome.Recycled or CleanupOutcome.Deleted)
            {
                reclaimed += item.SizeBytes;
            }

            results.Add(new CleanupItemResult(item.Path, item.Category, item.SizeBytes, outcome, message));
            completed++;

            progress?.Report(new CleanupProgress(completed, total, item.Path, reclaimed));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        stopwatch.Stop();

        var result = new CleanupResult(results, cancelled, stopwatch.Elapsed);

        _logger?.LogInformation(
            "Clean finished: {Reclaimed} reclaimed, {Skipped} skipped, {Failed} failed in {Elapsed}",
            result.RecycledCount + result.DeletedCount,
            result.SkippedCount,
            result.FailedCount,
            stopwatch.Elapsed);

        return result;
    }

    private CleanupOutcome CleanOne(JunkItem item, CleanupOptions options, out string? message)
    {
        return ItemRemoval.Remove(item.Path, item.RootPath, item.IsDirectory, options, _remover, _logger, out message);
    }

    private static IEnumerator<FileSystemInfo>? EnumerateSafely(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFileSystemInfos().GetEnumerator();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Windows file glob, matched against the file name the way Explorer matches it.</summary>
    private static bool MatchesPattern(string fileName, string searchPattern) =>
        searchPattern is "*" or "" || FileSystemName.MatchesSimpleExpression(searchPattern, fileName, ignoreCase: true);

    private static bool HasIntervalElapsed(long timestamp) =>
        Stopwatch.GetElapsedTime(timestamp) >= ReportInterval;

    private readonly record struct FolderMeasurement(long SizeBytes, bool IsEligible);

    private sealed class ScanState
    {
        public List<JunkItem> Items { get; } = [];

        public long TotalBytes { get; set; }

        public bool HitItemLimit { get; set; }
    }
}
