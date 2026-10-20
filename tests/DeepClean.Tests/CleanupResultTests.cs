using DeepClean.Core.Cleaning;

namespace DeepClean.Tests;

/// <summary>
/// The numbers on the post clean report come from here, so the way outcomes add up is
/// checked directly.
/// </summary>
public class CleanupResultTests
{
    [Fact]
    public void Empty_HasNothingInIt()
    {
        var result = CleanupResult.Empty;

        Assert.Empty(result.Items);
        Assert.Equal(0, result.ReclaimedBytes);
        Assert.Equal(0, result.SkippedBytes);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public void Counts_GroupItemsByOutcome()
    {
        var result = new CleanupResult(
            [
                Item("a", CleanupOutcome.Recycled, 100),
                Item("b", CleanupOutcome.Recycled, 200),
                Item("c", CleanupOutcome.Deleted, 400),
                Item("d", CleanupOutcome.Skipped, 800),
                Item("e", CleanupOutcome.Failed, 1600),
                Item("f", CleanupOutcome.Failed, 3200),
            ],
            wasCancelled: false,
            TimeSpan.FromSeconds(2));

        Assert.Equal(2, result.RecycledCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(2, result.FailedCount);
    }

    [Fact]
    public void ReclaimedBytes_OnlyCountsItemsThatActuallyLeftTheirFolder()
    {
        var result = new CleanupResult(
            [
                Item("a", CleanupOutcome.Recycled, 100),
                Item("b", CleanupOutcome.Deleted, 200),
                Item("c", CleanupOutcome.Skipped, 400),
                Item("d", CleanupOutcome.Failed, 800),
            ],
            wasCancelled: false,
            TimeSpan.Zero);

        Assert.Equal(300, result.ReclaimedBytes);
    }

    [Fact]
    public void SkippedBytes_CountsEverythingStillOnDisk()
    {
        var result = new CleanupResult(
            [
                Item("a", CleanupOutcome.Recycled, 100),
                Item("b", CleanupOutcome.Skipped, 400),
                Item("c", CleanupOutcome.Failed, 800),
            ],
            wasCancelled: false,
            TimeSpan.Zero);

        Assert.Equal(1200, result.SkippedBytes);
    }

    [Fact]
    public void Failures_And_Skips_KeepTheirExplanations()
    {
        var result = new CleanupResult(
            [
                Item("a", CleanupOutcome.Failed, 100, "Access is denied."),
                Item("b", CleanupOutcome.Skipped, 100, "It was already gone."),
                Item("c", CleanupOutcome.Recycled, 100),
            ],
            wasCancelled: false,
            TimeSpan.Zero);

        Assert.Equal("Access is denied.", Assert.Single(result.Failures).Message);
        Assert.Equal("It was already gone.", Assert.Single(result.Skips).Message);
    }

    [Fact]
    public void IsReclaimed_IsTrueOnlyForRecycledAndDeleted()
    {
        Assert.True(Item("a", CleanupOutcome.Recycled, 1).IsReclaimed);
        Assert.True(Item("b", CleanupOutcome.Deleted, 1).IsReclaimed);
        Assert.False(Item("c", CleanupOutcome.Skipped, 1).IsReclaimed);
        Assert.False(Item("d", CleanupOutcome.Failed, 1).IsReclaimed);
    }

    [Fact]
    public void Cancellation_And_Duration_AreCarriedThrough()
    {
        var result = new CleanupResult([], wasCancelled: true, TimeSpan.FromMilliseconds(150));

        Assert.True(result.WasCancelled);
        Assert.Equal(150, result.Duration.TotalMilliseconds);
    }

    private static CleanupItemResult Item(string name, CleanupOutcome outcome, long sizeBytes, string? message = null) =>
        new(
            @"C:\DeepClean.Tests.Synthetic\" + name + ".tmp",
            JunkCategory.UserTemporaryFiles,
            sizeBytes,
            outcome,
            message);
}
