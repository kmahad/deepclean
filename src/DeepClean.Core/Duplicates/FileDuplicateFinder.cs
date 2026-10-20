using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using DeepClean.Core.Cleaning;
using Microsoft.Extensions.Logging;

namespace DeepClean.Core.Duplicates;

/// <summary>
/// Finds files that hold exactly the same bytes. Three passes keep the expensive work small:
/// files are grouped by size first, then by the hash of their first few kilobytes, and only the
/// groups that survive both are hashed in full. Nothing is written, moved or deleted here.
/// </summary>
public sealed class FileDuplicateFinder : IDuplicateFinder
{
    private const int BufferSize = 1024 * 1024;

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(150);

    private readonly ILogger<FileDuplicateFinder>? _logger;

    public FileDuplicateFinder(ILogger<FileDuplicateFinder>? logger = null) => _logger = logger;

    public Task<DuplicateScanResult> FindAsync(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(() => Find(options, progress, cancellationToken), cancellationToken);
    }

    private DuplicateScanResult Find(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var state = new RunState();

        _logger?.LogInformation("Duplicate scan started over {RootCount} folders", options.Roots.Count);

        try
        {
            foreach (var root in NormalizeRoots(options))
            {
                Report(progress, state, Phase.Listing, root, force: true);
                Collect(root, options, state, progress, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    state.Cancelled = true;
                    break;
                }

                if (state.HitFileLimit)
                {
                    break;
                }
            }

            if (!state.Cancelled)
            {
                Group(options, state, progress, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            state.Cancelled = true;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            state.Cancelled = true;
        }

        stopwatch.Stop();

        _logger?.LogInformation(
            "Duplicate scan finished: {FileCount} files considered, {GroupCount} groups in {Elapsed}",
            state.Files.Count,
            state.Groups.Count,
            stopwatch.Elapsed);

        return new DuplicateScanResult(
            state.Groups,
            state.Files.Count,
            state.Cancelled,
            state.HitFileLimit,
            stopwatch.Elapsed);
    }

    /// <summary>Turns the requested folders into normalized paths that exist, dropping the rest.</summary>
    private static IEnumerable<string> NormalizeRoots(DuplicateScanOptions options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requested in options.Roots)
        {
            var root = SafetyGuard.TryNormalize(requested);

            if (root is not null && seen.Add(root) && Directory.Exists(root))
            {
                yield return root;
            }
        }
    }

    /// <summary>
    /// Walks one folder and remembers every file big enough to be worth comparing. Links and
    /// junctions are stepped over, so a scan cannot wander outside the folders that were chosen.
    /// </summary>
    private void Collect(
        string root,
        DuplicateScanOptions options,
        RunState state,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0 && !state.HitFileLimit && !cancellationToken.IsCancellationRequested)
        {
            var folder = pending.Pop();
            var enumerator = EnumerateSafely(folder);

            if (enumerator is null)
            {
                continue;
            }

            using (enumerator)
            {
                while (!state.HitFileLimit && !cancellationToken.IsCancellationRequested)
                {
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
                        // A folder that turned unreadable part way through; the rest still counts.
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

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (SafetyGuard.FindExclusion(entry.FullName, options.ExcludedPaths) is null)
                        {
                            pending.Push(entry.FullName);
                        }

                        continue;
                    }

                    Consider((FileInfo)entry, root, options, state, progress);
                }
            }
        }
    }

    private static IEnumerator<FileSystemInfo>? EnumerateSafely(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFileSystemInfos().GetEnumerator();
        }
        catch (Exception)
        {
            // A folder that cannot be listed is skipped rather than failing the whole scan.
            return null;
        }
    }

    /// <summary>Decides whether one file is worth comparing, and remembers it if it is.</summary>
    private void Consider(
        FileInfo file,
        string root,
        DuplicateScanOptions options,
        RunState state,
        IProgress<DuplicateScanProgress>? progress)
    {
        var path = file.FullName;

        // Overlapping scan folders would otherwise hand the same file in twice, and a file that
        // looks like a copy of itself would be offered up for removal.
        if (!state.Paths.Add(path))
        {
            return;
        }

        if (SafetyGuard.FindExclusion(path, options.ExcludedPaths) is not null)
        {
            return;
        }

        // Anything DeepClean would refuse to remove is never offered as a copy to remove. Only
        // system locations count here: the user's own folders are exactly where duplicates are
        // worth finding, and a copy taken from one of them is recycled rather than deleted.
        if (SafetyGuard.DescribeSystemProtection(path) is not null)
        {
            return;
        }

        long length;
        DateTime lastWriteUtc;

        try
        {
            length = file.Length;
            lastWriteUtc = file.LastWriteTimeUtc;
        }
        catch (Exception)
        {
            return;
        }

        if (length < options.MinimumSizeBytes || length <= 0)
        {
            return;
        }

        state.Files.Add(new Found(path, root, length, lastWriteUtc));

        if (options.MaximumFiles > 0 && state.Files.Count >= options.MaximumFiles)
        {
            state.HitFileLimit = true;
            _logger?.LogInformation("Duplicate scan stopped at the {Limit} file ceiling", options.MaximumFiles);
            return;
        }

        Report(progress, state, Phase.Listing, path, force: false);
    }

    /// <summary>
    /// Narrows the collected files down to the ones that really are identical: size first, then
    /// the hash of the first few kilobytes, and only then the hash of the whole file.
    /// </summary>
    private void Group(
        DuplicateScanOptions options,
        RunState state,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sampleBytes = Math.Max(1, options.SampleBytes);
        var byLength = state.Files.GroupBy(file => file.Length).Where(group => group.Count() > 1);

        foreach (var sameLength in byLength)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var prefixSize = Math.Min(sampleBytes, sameLength.Key);
            var byPrefix = new Dictionary<string, List<Found>>(StringComparer.Ordinal);

            foreach (var file in sameLength)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var prefix = Hash(file.Path, prefixSize, state, Phase.Sampling, progress);

                if (prefix is null)
                {
                    continue;
                }

                if (!byPrefix.TryGetValue(prefix, out var bucket))
                {
                    bucket = [];
                    byPrefix[prefix] = bucket;
                }

                bucket.Add(file);
            }

            foreach (var (prefix, bucket) in byPrefix)
            {
                if (bucket.Count < 2)
                {
                    continue;
                }

                // Every file in a bucket has the same length, so when that length fits inside the
                // sample the prefix hash already covered all of it and there is nothing left to read.
                var prefixIsWholeFile = bucket[0].Length <= sampleBytes;
                var byHash = new Dictionary<string, List<Found>>(StringComparer.Ordinal);

                foreach (var file in bucket)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var hash = prefixIsWholeFile
                        ? prefix
                        : Hash(file.Path, file.Length, state, Phase.Hashing, progress);

                    if (hash is null)
                    {
                        continue;
                    }

                    if (!byHash.TryGetValue(hash, out var copies))
                    {
                        copies = [];
                        byHash[hash] = copies;
                    }

                    copies.Add(file);
                }

                foreach (var (hash, copies) in byHash)
                {
                    if (copies.Count > 1)
                    {
                        state.Groups.Add(new DuplicateFileGroup(
                            hash,
                            copies[0].Length,
                            [.. copies.Select(copy => new DuplicateCandidate(copy.Path, copy.Root, copy.Length, copy.LastWriteUtc))]));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Hashes the first <paramref name="maxBytes"/> of a file, or the whole of it when that is
    /// smaller. A file that cannot be read counts as "not a duplicate" rather than stopping the
    /// scan, because one file another program is holding is no reason to give up on the rest.
    /// </summary>
    private string? Hash(
        string path,
        long maxBytes,
        RunState state,
        string phase,
        IProgress<DuplicateScanProgress>? progress)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.SequentialScan);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                var remaining = maxBytes;

                while (remaining > 0)
                {
                    var wanted = (int)Math.Min(buffer.Length, remaining);
                    var read = stream.Read(buffer, 0, wanted);

                    if (read <= 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);
                    remaining -= read;
                    state.BytesRead += read;

                    Report(progress, state, phase, path, force: false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            state.Hashed++;

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not read {Path}", path);
            return null;
        }
    }

    private static void Report(
        IProgress<DuplicateScanProgress>? progress,
        RunState state,
        string phase,
        string path,
        bool force)
    {
        if (progress is null)
        {
            return;
        }

        if (!force && Stopwatch.GetElapsedTime(state.LastReport) < ReportInterval)
        {
            return;
        }

        state.LastReport = Stopwatch.GetTimestamp();
        progress.Report(new DuplicateScanProgress(phase, state.Hashed, state.BytesRead, path));
    }

    private sealed class RunState
    {
        public List<Found> Files { get; } = [];

        /// <summary>Every path already taken in, so overlapping scan folders cannot double count.</summary>
        public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<DuplicateFileGroup> Groups { get; } = [];

        public int Hashed { get; set; }

        public long BytesRead { get; set; }

        public bool HitFileLimit { get; set; }

        public bool Cancelled { get; set; }

        public long LastReport { get; set; } = Stopwatch.GetTimestamp();
    }

    private readonly record struct Found(string Path, string Root, long Length, DateTime LastWriteUtc);

    private static class Phase
    {
        public const string Listing = "Listing files";
        public const string Sampling = "Comparing the start of each file";
        public const string Hashing = "Hashing the files that are still identical";
    }
}
