using System.IO;
using DeepClean.Core.Cleaning;

namespace DeepClean.Tests;

/// <summary>
/// Exercises the walk and the removal pass against a real, throwaway folder tree with the
/// shell replaced by a fake. Everything the cleaner promises about what it touches and what
/// it leaves alone is checked here.
/// </summary>
public class FileSystemCleanerTests
{
    // ----- Scanning -------------------------------------------------------------------

    [Fact]
    public async Task ScanAsync_FindsFilesPastTheAgeFloorOnly()
    {
        using var tree = new TempTree();
        tree.CreateFile("old.tmp", 100, ageDays: 5);
        tree.CreateFile("new.tmp", 100, ageDays: 0);

        var result = await ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root)));

        Assert.Equal("old.tmp", Path.GetFileName(Assert.Single(result.Items).Path));
    }

    [Fact]
    public async Task ScanAsync_ReportsAFinishedFolderAsOneUnit()
    {
        using var tree = new TempTree();
        var cache = tree.CreateFolder("cache");
        tree.CreateFile(Path.Combine("cache", "one.tmp"), 100, ageDays: 5);
        tree.CreateFile(Path.Combine("cache", "two.tmp"), 200, ageDays: 6);

        var result = await ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root)));

        var item = Assert.Single(result.Items);

        Assert.Equal(cache, item.Path, ignoreCase: true);
        Assert.True(item.IsDirectory);
        Assert.Equal(300, item.SizeBytes);
        Assert.Equal(300, result.TotalBytes);
    }

    [Fact]
    public async Task ScanAsync_KeepsWalkingWhenAFolderHoldsSomethingRecent()
    {
        using var tree = new TempTree();
        tree.CreateFile(Path.Combine("cache", "old.tmp"), 100, ageDays: 5);
        tree.CreateFile(Path.Combine("cache", "recent.tmp"), 100, ageDays: 0);

        var result = await ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root)));

        var item = Assert.Single(result.Items);

        Assert.Equal("old.tmp", Path.GetFileName(item.Path));
        Assert.False(item.IsDirectory);
    }

    [Fact]
    public async Task ScanAsync_NeverReportsTheRuleFolderItself()
    {
        using var tree = new TempTree();
        tree.CreateFile("old.tmp", 100, ageDays: 5);

        var result = await ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root)));

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, item => Assert.False(
            string.Equals(tree.Root, item.Path, StringComparison.OrdinalIgnoreCase),
            $"A rule's own folder must never be reported, but {item.Path} was."));
    }

    [Fact]
    public async Task ScanAsync_HonoursTheExclusionList()
    {
        using var tree = new TempTree();
        var cache = tree.CreateFolder("cache");
        tree.CreateFile(Path.Combine("cache", "old.tmp"), 100, ageDays: 5);

        var result = await ScanAsync(
            Over(new FakeFileRemover(), Rule(tree.Root)),
            exclusions: [cache]);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ScanAsync_AppliesTheExtraAgeFloorFromTheOptions()
    {
        using var tree = new TempTree();
        tree.CreateFile("old.tmp", 100, ageDays: 3);

        var result = await ScanAsync(
            Over(new FakeFileRemover(), Rule(tree.Root, minimumAgeDays: 1)),
            minimumAgeDays: 7);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ScanAsync_HonoursTheSearchPattern()
    {
        using var tree = new TempTree();
        var thumbcache = tree.CreateFile("thumbcache_abc.db", 100, ageDays: 5);
        tree.CreateFile("iconcache_abc.db", 100, ageDays: 5);
        tree.CreateFile("notes.txt", 100, ageDays: 5);

        var result = await ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root, searchPattern: "thumbcache_*.db")));

        var item = Assert.Single(result.Items);

        Assert.Equal(thumbcache, item.Path);
    }

    [Fact]
    public async Task ScanAsync_StopsAtTheItemCeiling()
    {
        using var tree = new TempTree();

        for (var index = 0; index < 5; index++)
        {
            tree.CreateFile($"file{index}.tmp", 100, ageDays: 5);
        }

        var result = await ScanAsync(
            Over(new FakeFileRemover(), Rule(tree.Root)),
            maximumItems: 3);

        Assert.True(result.HitItemLimit);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ScanAsync_RespectsMaximumDepth()
    {
        using var tree = new TempTree();
        tree.CreateFile(Path.Combine("sub", "old.tmp"), 100, ageDays: 5);

        var shallow = await ScanAsync(
            Over(new FakeFileRemover(), Rule(tree.Root, searchPattern: "*.tmp", maximumDepth: 1)));

        var deep = await ScanAsync(
            Over(new FakeFileRemover(), Rule(tree.Root, searchPattern: "*.tmp", maximumDepth: 3)));

        Assert.Empty(shallow.Items);
        Assert.Equal(1, deep.Count);
    }

    [Fact]
    public async Task ScanAsync_LeavesTheFilesAlone()
    {
        using var tree = new TempTree();
        var files = new[]
        {
            tree.CreateFile("a.tmp", 10, ageDays: 5),
            tree.CreateFile("b.tmp", 20, ageDays: 5),
            tree.CreateFile("c.tmp", 30, ageDays: 5),
        };

        var result = await ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root)));

        Assert.Equal(3, result.Count);
        Assert.All(files, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        using var tree = new TempTree();
        tree.CreateFile("a.tmp", 10, ageDays: 5);
        tree.CreateFile("b.tmp", 20, ageDays: 5);

        var progress = new RecordingProgress<JunkScanProgress>();

        var result = await ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root)), progress: progress);

        Assert.NotEmpty(progress.Values);
        Assert.Equal(progress.Values.OrderBy(value => value.ItemCount), progress.Values);
        Assert.Equal(progress.Values.OrderBy(value => value.TotalBytes), progress.Values);
        Assert.True(progress.Values[^1].ItemCount <= result.Count);
    }

    [Fact]
    public async Task ScanAsync_ReportsCancellation()
    {
        using var tree = new TempTree();
        tree.CreateFile("old.tmp", 100, ageDays: 5);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScanAsync(Over(new FakeFileRemover(), Rule(tree.Root)), cancellationToken: cancellation.Token));
    }

    // ----- Cleaning -------------------------------------------------------------------

    [Fact]
    public async Task CleanAsync_RecyclesByDefault()
    {
        using var tree = new TempTree();
        var file = tree.CreateFile("old.tmp", 512, ageDays: 5);
        var remover = new FakeFileRemover();

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(file, tree.Root, 512)]);

        Assert.Equal(1, result.RecycledCount);
        Assert.Equal(512, result.ReclaimedBytes);

        var call = Assert.Single(remover.Calls);

        Assert.Equal(file, call.Path);
        Assert.False(call.IsDirectory);
        Assert.False(call.Permanent);
    }

    [Fact]
    public async Task CleanAsync_DeletesPermanentlyWhenAsked()
    {
        using var tree = new TempTree();
        var file = tree.CreateFile("old.tmp", 512, ageDays: 5);
        var remover = new FakeFileRemover();

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(file, tree.Root, 512)],
            permanentlyDelete: true);

        Assert.Equal(1, result.DeletedCount);
        Assert.True(Assert.Single(remover.Calls).Permanent);
    }

    [Fact]
    public async Task CleanAsync_SkipsItemsInAProtectedFolder()
    {
        using var tree = new TempTree();
        var remover = new FakeFileRemover();
        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(Path.Combine(system32, "kernel32.dll"), system32, 1)]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.SkippedBytes);
        Assert.Empty(remover.Calls);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(result.Skips).Message));
    }

    [Fact]
    public async Task CleanAsync_SkipsFilesThatAreAlreadyGone()
    {
        using var tree = new TempTree();
        var remover = new FakeFileRemover();

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(Path.Combine(tree.Root, "gone.tmp"), tree.Root, 100)]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("It was already gone.", Assert.Single(result.Skips).Message);
        Assert.Empty(remover.Calls);
    }

    [Fact]
    public async Task CleanAsync_SkipsFilesThatAreInUse()
    {
        using var tree = new TempTree();
        var file = tree.CreateFile("locked.tmp", 100, ageDays: 5);
        var remover = new FakeFileRemover
        {
            Throw = new IOException("The process cannot access the file because it is being used by another process."),
        };

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(file, tree.Root, 100)]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains("In use by another program", Assert.Single(result.Skips).Message);
    }

    [Fact]
    public async Task CleanAsync_ReportsWindowsRefusals()
    {
        using var tree = new TempTree();
        var file = tree.CreateFile("denied.tmp", 100, ageDays: 5);
        var remover = new FakeFileRemover { ResultFactory = _ => "Access is denied." };

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(file, tree.Root, 100)]);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.ReclaimedBytes);
        Assert.Equal("Access is denied.", Assert.Single(result.Failures).Message);
    }

    [Fact]
    public async Task CleanAsync_HonoursTheExclusionList()
    {
        using var tree = new TempTree();
        var keep = tree.CreateFolder("keep");
        var file = tree.CreateFile(Path.Combine("keep", "old.tmp"), 100, ageDays: 5);
        var remover = new FakeFileRemover();

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(file, tree.Root, 100)],
            exclusions: [keep]);

        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(remover.Calls);
    }

    [Fact]
    public async Task CleanAsync_KeepsGoingAfterAFailure()
    {
        using var tree = new TempTree();
        var first = tree.CreateFile("a.tmp", 10, ageDays: 5);
        var second = tree.CreateFile("b.tmp", 20, ageDays: 5);
        var remover = new FakeFileRemover { ResultFactory = path => path == first ? "Access is denied." : null };

        var result = await CleanAsync(
            Over(remover, Rule(tree.Root)),
            [Item(first, tree.Root, 10), Item(second, tree.Root, 20)]);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RecycledCount);
        Assert.Equal(20, result.ReclaimedBytes);
    }

    [Fact]
    public async Task CleanAsync_ReportsProgress()
    {
        using var tree = new TempTree();
        var items = new[]
        {
            Item(tree.CreateFile("a.tmp", 10, ageDays: 5), tree.Root, 10),
            Item(tree.CreateFile("b.tmp", 20, ageDays: 5), tree.Root, 20),
            Item(tree.CreateFile("c.tmp", 30, ageDays: 5), tree.Root, 30),
        };

        var progress = new RecordingProgress<CleanupProgress>();

        var result = await CleanAsync(Over(new FakeFileRemover(), Rule(tree.Root)), items, progress: progress);

        Assert.Equal(3, result.RecycledCount);
        Assert.Equal(new[] { 1, 2, 3 }, progress.Values.Select(value => value.Completed));
        Assert.All(progress.Values, value => Assert.Equal(3, value.Total));
        Assert.Equal(10, progress.Values[0].ReclaimedBytes);
        Assert.Equal(30, progress.Values[1].ReclaimedBytes);
        Assert.Equal(60, progress.Values[2].ReclaimedBytes);
    }

    [Fact]
    public async Task CleanAsync_ReportsCancellation()
    {
        using var tree = new TempTree();
        var file = tree.CreateFile("old.tmp", 100, ageDays: 5);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CleanAsync(
                Over(new FakeFileRemover(), Rule(tree.Root)),
                [Item(file, tree.Root, 100)],
                cancellationToken: cancellation.Token));
    }

    // ----- Helpers --------------------------------------------------------------------

    private static FileSystemCleaner Over(IFileRemover remover, JunkRule rule) => new(remover, [rule]);

    private static JunkRule Rule(
        string targetPath,
        int minimumAgeDays = 1,
        string searchPattern = "*",
        int maximumDepth = 3) =>
        new()
        {
            Id = "test-rule",
            Category = JunkCategory.UserTemporaryFiles,
            Title = "Test rule",
            Description = "A rule built by the tests.",
            TargetPath = SafetyGuard.TryNormalize(targetPath)!,
            SearchPattern = searchPattern,
            MinimumAgeDays = minimumAgeDays,
            MaximumDepth = maximumDepth,
        };

    private static JunkItem Item(string path, string rootPath, long sizeBytes, bool isDirectory = false) =>
        new()
        {
            Path = path,
            RootPath = rootPath,
            Category = JunkCategory.UserTemporaryFiles,
            RuleId = "test-rule",
            RuleTitle = "Test rule",
            SizeBytes = sizeBytes,
            IsDirectory = isDirectory,
            LastWriteTimeUtc = DateTime.UtcNow.AddDays(-5),
        };

    private static Task<JunkScanResult> ScanAsync(
        FileSystemCleaner cleaner,
        JunkCategory[]? categories = null,
        string[]? exclusions = null,
        int minimumAgeDays = 0,
        int maximumItems = 100_000,
        bool isElevated = true,
        IProgress<JunkScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        cleaner.ScanAsync(
            new JunkScanOptions
            {
                Categories = categories ?? [JunkCategory.UserTemporaryFiles],
                ExcludedPaths = exclusions ?? [],
                MinimumAgeDays = minimumAgeDays,
                MaximumItems = maximumItems,
                IsElevated = isElevated,
            },
            progress,
            cancellationToken);

    private static Task<CleanupResult> CleanAsync(
        FileSystemCleaner cleaner,
        IReadOnlyCollection<JunkItem> items,
        bool permanentlyDelete = false,
        string[]? exclusions = null,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        cleaner.CleanAsync(
            items,
            new CleanupOptions
            {
                PermanentlyDelete = permanentlyDelete,
                ExcludedPaths = exclusions ?? [],
            },
            progress,
            cancellationToken);
}
