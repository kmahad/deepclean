using System.IO;
using DeepClean.Core.Cleaning;
using DeepClean.Core.Duplicates;

namespace DeepClean.Tests;

/// <summary>
/// Removing a copy has to go through the same guards as the junk cleaner, and must keep going
/// when Windows refuses one. The Recycle Bin is faked, so nothing real is touched.
/// </summary>
public class DuplicateCleanerTests
{
    [Fact]
    public async Task DeleteAsync_RecyclesByDefault()
    {
        using var tree = new TempTree();
        var copy = tree.CreateFile("copy.jpg", Content(4096));
        var remover = new FakeFileRemover();

        var result = await DeleteAsync(new DuplicateCleaner(remover), [Copy(copy, tree.Root)]);

        Assert.Equal(1, result.RecycledCount);
        Assert.Equal(4096, result.ReclaimedBytes);

        var call = Assert.Single(remover.Calls);

        Assert.Equal(copy, call.Path);
        Assert.False(call.Permanent);

        // A copy has no junk category to report, and the report says so rather than guessing one.
        Assert.Null(Assert.Single(result.Items).Category);
    }

    [Fact]
    public async Task DeleteAsync_DeletesPermanentlyWhenAsked()
    {
        using var tree = new TempTree();
        var copy = tree.CreateFile("copy.jpg", Content(4096));
        var remover = new FakeFileRemover();

        var result = await DeleteAsync(new DuplicateCleaner(remover), [Copy(copy, tree.Root)], permanentlyDelete: true);

        Assert.Equal(1, result.DeletedCount);
        Assert.True(Assert.Single(remover.Calls).Permanent);
    }

    [Fact]
    public async Task DeleteAsync_SkipsCopiesInAProtectedFolder()
    {
        var remover = new FakeFileRemover();
        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");

        var result = await DeleteAsync(
            new DuplicateCleaner(remover),
            [Copy(Path.Combine(system32, "kernel32.dll"), system32, 1)]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.SkippedBytes);
        Assert.Empty(remover.Calls);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(result.Skips).Message));
    }

    [Fact]
    public async Task DeleteAsync_SkipsCopiesThatAreAlreadyGone()
    {
        using var tree = new TempTree();
        var remover = new FakeFileRemover();

        var result = await DeleteAsync(
            new DuplicateCleaner(remover),
            [Copy(Path.Combine(tree.Root, "gone.jpg"), tree.Root)]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("It was already gone.", Assert.Single(result.Skips).Message);
        Assert.Empty(remover.Calls);
    }

    [Fact]
    public async Task DeleteAsync_SkipsCopiesThatAreInUse()
    {
        using var tree = new TempTree();
        var copy = tree.CreateFile("copy.jpg", Content(4096));
        var remover = new FakeFileRemover
        {
            Throw = new IOException("The process cannot access the file because it is being used by another process."),
        };

        var result = await DeleteAsync(new DuplicateCleaner(remover), [Copy(copy, tree.Root)]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains("In use by another program", Assert.Single(result.Skips).Message);
    }

    [Fact]
    public async Task DeleteAsync_ReportsWindowsRefusals()
    {
        using var tree = new TempTree();
        var copy = tree.CreateFile("copy.jpg", Content(4096));
        var remover = new FakeFileRemover { ResultFactory = _ => "Access is denied." };

        var result = await DeleteAsync(new DuplicateCleaner(remover), [Copy(copy, tree.Root)]);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.ReclaimedBytes);
        Assert.Equal("Access is denied.", Assert.Single(result.Failures).Message);
    }

    [Fact]
    public async Task DeleteAsync_HonoursTheExclusionList()
    {
        using var tree = new TempTree();
        var excluded = tree.CreateFolder("keep");
        var copy = tree.CreateFile(Path.Combine("keep", "copy.jpg"), Content(4096));
        var remover = new FakeFileRemover();

        var result = await DeleteAsync(
            new DuplicateCleaner(remover),
            [Copy(copy, tree.Root)],
            exclusions: [excluded]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(remover.Calls);
    }

    [Fact]
    public async Task DeleteAsync_KeepsGoingAfterAFailure()
    {
        using var tree = new TempTree();
        var first = tree.CreateFile("a.jpg", Content(4096));
        var second = tree.CreateFile("b.jpg", Content(4096));
        var remover = new FakeFileRemover { ResultFactory = path => path == first ? "Access is denied." : null };

        var result = await DeleteAsync(
            new DuplicateCleaner(remover),
            [Copy(first, tree.Root), Copy(second, tree.Root)]);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RecycledCount);
        Assert.Equal(4096, result.ReclaimedBytes);
    }

    [Fact]
    public async Task DeleteAsync_ReportsProgress()
    {
        using var tree = new TempTree();
        var copies = new[]
        {
            Copy(tree.CreateFile("a.jpg", Content(4096)), tree.Root),
            Copy(tree.CreateFile("b.jpg", Content(4096)), tree.Root),
        };

        var progress = new RecordingProgress<CleanupProgress>();

        var result = await DeleteAsync(new DuplicateCleaner(new FakeFileRemover()), copies, progress: progress);

        Assert.Equal(2, result.RecycledCount);
        Assert.Equal(new[] { 1, 2 }, progress.Values.Select(value => value.Completed));
        Assert.All(progress.Values, value => Assert.Equal(2, value.Total));
        Assert.Equal(4096, progress.Values[0].ReclaimedBytes);
        Assert.Equal(8192, progress.Values[1].ReclaimedBytes);
    }

    [Fact]
    public async Task DeleteAsync_ReportsCancellation()
    {
        using var tree = new TempTree();
        var copy = tree.CreateFile("copy.jpg", Content(4096));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DeleteAsync(new DuplicateCleaner(new FakeFileRemover()), [Copy(copy, tree.Root)], cancellationToken: cancellation.Token));
    }

    // ----- Helpers --------------------------------------------------------------------

    private static byte[] Content(int sizeBytes) => new byte[sizeBytes];

    private static DuplicateCandidate Copy(string path, string rootPath, long sizeBytes = 4096) =>
        new(path, rootPath, sizeBytes, DateTime.UtcNow.AddDays(-5));

    private static Task<CleanupResult> DeleteAsync(
        DuplicateCleaner cleaner,
        IReadOnlyCollection<DuplicateCandidate> candidates,
        bool permanentlyDelete = false,
        string[]? exclusions = null,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        cleaner.DeleteAsync(
            candidates,
            new CleanupOptions
            {
                PermanentlyDelete = permanentlyDelete,
                ExcludedPaths = exclusions ?? [],
            },
            progress,
            cancellationToken);
}
