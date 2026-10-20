using System.IO;
using DeepClean.Core.Duplicates;

namespace DeepClean.Tests;

/// <summary>
/// The duplicate scan runs against a real throwaway folder tree, because what it has to get right
/// is which bytes it compares rather than how it talks to a fake.
/// </summary>
public class FileDuplicateFinderTests
{
    [Fact]
    public async Task FindAsync_GroupsFilesThatHoldTheSameBytes()
    {
        using var tree = new TempTree();
        var original = tree.CreateFile(Path.Combine("one", "photo.jpg"), Content(4096), ageDays: 5);
        var copy = tree.CreateFile(Path.Combine("two", "photo copy.jpg"), Content(4096), ageDays: 1);

        var result = await FindAsync(Options(tree.Root));

        var group = Assert.Single(result.Groups);

        Assert.Equal(2, group.Candidates.Count);
        Assert.Equal(4096, group.SizeBytes);
        Assert.Equal(4096, group.WastedBytes);
        Assert.Equal(1, result.DuplicateFileCount);
        Assert.Equal(4096, result.WastedBytes);
        Assert.Contains(group.Candidates, candidate => candidate.Path == original);
        Assert.Contains(group.Candidates, candidate => candidate.Path == copy);
    }

    [Fact]
    public async Task FindAsync_KeepsFilesOfTheSameLengthWithDifferentBytesApart()
    {
        using var tree = new TempTree();
        tree.CreateFile("a.jpg", Content(4096, seed: 1));
        tree.CreateFile("b.jpg", Content(4096, seed: 2));

        var result = await FindAsync(Options(tree.Root));

        Assert.Empty(result.Groups);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public async Task FindAsync_ReportsEveryCopyInAGroup()
    {
        using var tree = new TempTree();

        for (var index = 0; index < 3; index++)
        {
            tree.CreateFile($"copy{index}.jpg", Content(8192));
        }

        var result = await FindAsync(Options(tree.Root));

        var group = Assert.Single(result.Groups);

        Assert.Equal(3, group.Candidates.Count);
        Assert.Equal(16384, group.WastedBytes);
        Assert.Equal(2, result.DuplicateFileCount);
    }

    [Fact]
    public async Task FindAsync_MatchesFilesLargerThanTheSample()
    {
        using var tree = new TempTree();
        tree.CreateFile("a.bin", Content(200_000));
        tree.CreateFile("b.bin", Content(200_000));

        var result = await FindAsync(Options(tree.Root));

        var group = Assert.Single(result.Groups);

        Assert.Equal(2, group.Candidates.Count);
        Assert.Equal(200_000, group.SizeBytes);
    }

    [Fact]
    public async Task FindAsync_KeepsFilesThatDifferOnlyPastTheSampleApart()
    {
        using var tree = new TempTree();
        var first = Content(200_000);
        var second = (byte[])first.Clone();

        second[^1] ^= 0xFF;

        tree.CreateFile("a.bin", first);
        tree.CreateFile("b.bin", second);

        var result = await FindAsync(Options(tree.Root));

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FindAsync_IgnoresFilesSmallerThanTheFloor()
    {
        using var tree = new TempTree();
        tree.CreateFile("a.tmp", Content(512));
        tree.CreateFile("b.tmp", Content(512));

        var result = await FindAsync(Options(tree.Root, minimumSizeBytes: 1024));

        Assert.Empty(result.Groups);
        Assert.Equal(0, result.FileCount);
    }

    [Fact]
    public async Task FindAsync_IgnoresEmptyFiles()
    {
        using var tree = new TempTree();
        tree.CreateFile("a.tmp", []);
        tree.CreateFile("b.tmp", []);

        var result = await FindAsync(Options(tree.Root));

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FindAsync_HonoursTheExclusionList()
    {
        using var tree = new TempTree();
        var excluded = tree.CreateFolder("keep");
        tree.CreateFile(Path.Combine("keep", "a.jpg"), Content(4096));
        tree.CreateFile("b.jpg", Content(4096));

        var result = await FindAsync(Options(tree.Root, exclusions: [excluded]));

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FindAsync_FindsCopiesAcrossTheSearchedFolders()
    {
        using var first = new TempTree();
        using var second = new TempTree();
        first.CreateFile("a.jpg", Content(4096));
        second.CreateFile("b.jpg", Content(4096));

        var result = await FindAsync(new DuplicateScanOptions
        {
            Roots = [first.Root, second.Root],
            MinimumSizeBytes = 0,
        });

        var group = Assert.Single(result.Groups);

        Assert.Equal(2, group.Candidates.Count);
        Assert.Contains(group.Candidates, candidate => candidate.RootPath == first.Root);
        Assert.Contains(group.Candidates, candidate => candidate.RootPath == second.Root);
    }

    [Fact]
    public async Task FindAsync_StopsAtTheFileCeiling()
    {
        using var tree = new TempTree();

        for (var index = 0; index < 3; index++)
        {
            tree.CreateFile($"copy{index}.jpg", Content(4096));
        }

        var result = await FindAsync(Options(tree.Root, maximumFiles: 2));

        Assert.True(result.HitFileLimit);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public async Task FindAsync_IgnoresFoldersThatAreNotThere()
    {
        using var tree = new TempTree();

        var result = await FindAsync(new DuplicateScanOptions
        {
            Roots = [Path.Combine(tree.Root, "missing")],
            MinimumSizeBytes = 0,
        });

        Assert.Empty(result.Groups);
        Assert.Equal(0, result.FileCount);
    }

    [Fact]
    public async Task FindAsync_LeavesTheFilesAlone()
    {
        using var tree = new TempTree();
        var first = tree.CreateFile("a.jpg", Content(4096));
        var second = tree.CreateFile("b.jpg", Content(4096));

        await FindAsync(Options(tree.Root));

        Assert.Equal(Content(4096), File.ReadAllBytes(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task FindAsync_ReportsProgress()
    {
        using var tree = new TempTree();
        tree.CreateFile("a.jpg", Content(4096));
        tree.CreateFile("b.jpg", Content(4096));

        var progress = new RecordingProgress<DuplicateScanProgress>();

        var result = await FindAsync(Options(tree.Root), progress);

        Assert.NotEmpty(progress.Values);
        Assert.Equal(progress.Values.OrderBy(value => value.BytesRead), progress.Values);
        Assert.All(progress.Values, value => Assert.False(string.IsNullOrWhiteSpace(value.Phase)));
        Assert.True(progress.Values[^1].BytesRead <= 4096 * 2);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public async Task FindAsync_ReportsCancellation()
    {
        using var tree = new TempTree();
        tree.CreateFile("a.jpg", Content(4096));
        tree.CreateFile("b.jpg", Content(4096));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FindAsync(Options(tree.Root), cancellationToken: cancellation.Token));
    }

    // ----- Helpers --------------------------------------------------------------------

    /// <summary>Deterministic bytes, so two files built with the same seed are identical.</summary>
    private static byte[] Content(int sizeBytes, int seed = 7)
    {
        var bytes = new byte[sizeBytes];

        new Random(seed).NextBytes(bytes);

        return bytes;
    }

    private static DuplicateScanOptions Options(
        string root,
        long minimumSizeBytes = 0,
        int maximumFiles = 100_000,
        string[]? exclusions = null) =>
        new()
        {
            Roots = [root],
            ExcludedPaths = exclusions ?? [],
            MinimumSizeBytes = minimumSizeBytes,
            MaximumFiles = maximumFiles,
        };

    private static Task<DuplicateScanResult> FindAsync(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        new FileDuplicateFinder().FindAsync(options, progress, cancellationToken);
}
