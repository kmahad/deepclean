using System.IO;
using DeepClean.Core.Cleaning;

namespace DeepClean.Tests;

/// <summary>
/// The catalog is what actually decides which folders are reachable, so its invariants are
/// checked against the machine the tests run on.
/// </summary>
public class JunkRuleCatalogTests
{
    private static readonly IReadOnlyList<JunkRule> Rules = JunkRuleCatalog.Create();

    [Fact]
    public void Create_GivesEveryRuleAUniqueId()
    {
        var duplicates = Rules
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Create_DescribesEveryRule()
    {
        foreach (var rule in Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Title));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
        }
    }

    [Fact]
    public void Create_OnlyReturnsFoldersThatExist()
    {
        foreach (var rule in Rules)
        {
            Assert.True(Directory.Exists(rule.TargetPath), $"{rule.Id} points at {rule.TargetPath}, which does not exist.");
        }
    }

    [Fact]
    public void Create_NeverPointsAtAProtectedFolder()
    {
        foreach (var rule in Rules)
        {
            Assert.Null(SafetyGuard.DescribeProtection(rule.TargetPath));
        }
    }

    [Fact]
    public void Create_AlwaysKeepsAFilesAgeFloorAboveZero()
    {
        foreach (var rule in Rules)
        {
            Assert.True(rule.MinimumAgeDays >= 1, $"{rule.Id} would delete files that were just written.");
        }
    }

    [Fact]
    public void Create_GivesWindowsUpdateLeftoversAWideMargin()
    {
        foreach (var rule in Rules.Where(rule => rule.Category == JunkCategory.WindowsUpdateLeftovers))
        {
            Assert.True(rule.MinimumAgeDays >= 14, $"{rule.Id} is less careful than two weeks.");
        }
    }

    [Fact]
    public void Create_OnlyAsksForRightsInsideTheWindowsFolder()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        foreach (var rule in Rules.Where(rule => rule.RequiresElevation))
        {
            Assert.True(
                SafetyGuard.IsSameOrInside(rule.TargetPath, windows),
                $"{rule.Id} asks for administrator rights but is not in a machine wide folder.");
        }
    }

    [Fact]
    public void GetActiveRules_DropsElevationOnlyRulesForAStandardUser()
    {
        var cleaner = new FileSystemCleaner(new FakeFileRemover(), Rules);

        var elevated = cleaner.GetActiveRules(null, isElevated: true);
        var standard = cleaner.GetActiveRules(null, isElevated: false);

        Assert.Contains(elevated, rule => rule.RequiresElevation);
        Assert.DoesNotContain(standard, rule => rule.RequiresElevation);
        Assert.All(standard, rule => Assert.False(rule.RequiresElevation));
    }

    [Fact]
    public void GetActiveRules_LimitsItselfToTheAskedForCategories()
    {
        var cleaner = new FileSystemCleaner(new FakeFileRemover(), Rules);
        var wanted = new[] { JunkCategory.CrashDumps };

        var active = cleaner.GetActiveRules(wanted, isElevated: true);

        Assert.All(active, rule => Assert.Equal(JunkCategory.CrashDumps, rule.Category));
    }
}
