using System.IO;
using DeepClean.Core;
using DeepClean.Core.Cleaning;

namespace DeepClean.Tests;

/// <summary>
/// The safety policy is the last thing between a wrong rule and a user's files, so it is
/// checked directly rather than only through the cleaner.
/// </summary>
public class SafetyGuardTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>A folder that no protection rule covers, used for the neutral cases.</summary>
    private static string SafeRoot => Path.Combine(Path.GetPathRoot(Windows)!, "DeepClean.Tests.Synthetic");

    private static string SafeItem => Path.Combine(SafeRoot, "some-file.tmp");

    [Theory]
    [InlineData("System32")]
    [InlineData("SysWOW64")]
    [InlineData("WinSxS")]
    [InlineData("servicing")]
    [InlineData("assembly")]
    [InlineData("Fonts")]
    [InlineData("Installer")]
    public void DescribeProtection_RefusesWindowsSubtrees(string folderName)
    {
        var path = Path.Combine(Windows, folderName, "some-file.dll");

        Assert.NotNull(SafetyGuard.DescribeProtection(path));
    }

    [Fact]
    public void DescribeProtection_RefusesTheWindowsFolderItself() =>
        Assert.NotNull(SafetyGuard.DescribeProtection(Windows));

    [Fact]
    public void DescribeProtection_RefusesProgramFilesAndProgramData()
    {
        Assert.NotNull(SafetyGuard.DescribeProtection(ProgramFiles));
        Assert.NotNull(SafetyGuard.DescribeProtection(Path.Combine(ProgramFiles, "AnApp", "app.exe")));
        Assert.NotNull(SafetyGuard.DescribeProtection(ProgramData));
        Assert.NotNull(SafetyGuard.DescribeProtection(Path.Combine(ProgramData, "AnApp", "data.bin")));
    }

    [Fact]
    public void DescribeProtection_RefusesRoamingAppDataButNotTheLocalTempFolder()
    {
        Assert.NotNull(SafetyGuard.DescribeProtection(Path.Combine(AppData, "SomeApp", "settings.json")));

        // The local temporary folder is exactly what DeepClean is allowed to clean.
        Assert.Null(SafetyGuard.DescribeProtection(Path.Combine(LocalAppData, "Temp")));
        Assert.Null(SafetyGuard.DescribeProtection(Path.Combine(LocalAppData, "Temp", "leftover.tmp")));
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.Desktop)]
    [InlineData(Environment.SpecialFolder.MyDocuments)]
    [InlineData(Environment.SpecialFolder.MyPictures)]
    [InlineData(Environment.SpecialFolder.MyVideos)]
    [InlineData(Environment.SpecialFolder.MyMusic)]
    public void DescribeProtection_RefusesPersonalFolders(Environment.SpecialFolder folder)
    {
        // The resolved location, not the profile plus the folder's name: these folders can be
        // redirected out of the profile, which is what OneDrive does when it moves them.
        var path = Path.Combine(Environment.GetFolderPath(folder), "something.txt");

        Assert.NotNull(SafetyGuard.DescribeProtection(path));
    }

    [Theory]
    [InlineData("Downloads")]
    [InlineData(".ssh")]
    [InlineData(".gnupg")]
    public void DescribeProtection_RefusesProfileFolders(string folderName)
    {
        var path = Path.Combine(Profile, folderName, "something.txt");

        Assert.NotNull(SafetyGuard.DescribeProtection(path));
    }

    [Fact]
    public void DescribeProtection_RefusesDeepCleanOwnFolders()
    {
        Assert.NotNull(SafetyGuard.DescribeProtection(AppPaths.RootDirectory));
        Assert.NotNull(SafetyGuard.DescribeProtection(Path.Combine(AppPaths.RootDirectory, "settings.json")));
        Assert.NotNull(SafetyGuard.DescribeProtection(AppPaths.LogDirectory));
    }

    [Fact]
    public void DescribeProtection_RefusesTheRecycleBin()
    {
        var recycleBin = Path.Combine(Path.GetPathRoot(Windows)!, "$Recycle.Bin");

        Assert.NotNull(SafetyGuard.DescribeProtection(Path.Combine(recycleBin, "S-1-5-21", "$R123.txt")));
    }

    [Fact]
    public void DescribeProtection_RefusesDriveRoots()
    {
        var root = Path.GetPathRoot(Windows)!;

        Assert.NotNull(SafetyGuard.DescribeProtection(root));
        Assert.True(SafetyGuard.IsProtected(root));
    }

    [Fact]
    public void DescribeProtection_AllowsAnOrdinaryFolder() =>
        Assert.Null(SafetyGuard.DescribeProtection(SafeRoot));

    [Fact]
    public void Check_AllowsAnItemInsideItsOwnRuleFolder()
    {
        var check = SafetyGuard.Check(SafeItem, SafeRoot);

        Assert.True(check.IsAllowed);
        Assert.Equal(string.Empty, check.Reason);
    }

    [Fact]
    public void Check_RefusesTheRuleFolderItself()
    {
        var check = SafetyGuard.Check(SafeRoot, SafeRoot);

        Assert.False(check.IsAllowed);
    }

    [Fact]
    public void Check_RefusesAnItemThatIsNoLongerInsideTheRuleFolder()
    {
        var check = SafetyGuard.Check(Path.Combine(SafeRoot + ".Other", "some-file.tmp"), SafeRoot);

        Assert.False(check.IsAllowed);
    }

    [Fact]
    public void Check_RefusesAnItemBelowTheRuleFolderByAnotherName()
    {
        // "DeepClean.Tests.Synthetic2" starts with the rule folder's text but is a different folder.
        var sibling = SafeRoot + "2";

        Assert.False(SafetyGuard.Check(Path.Combine(sibling, "some-file.tmp"), SafeRoot).IsAllowed);
    }

    [Fact]
    public void Check_RefusesTraversalInTheItemPath()
    {
        var check = SafetyGuard.Check(Path.Combine(SafeRoot, "..", "..", "Windows", "notepad.exe"), SafeRoot);

        Assert.False(check.IsAllowed);
        Assert.Contains("..", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_RefusesTraversalInTheRootPath() =>
        Assert.False(SafetyGuard.Check(SafeItem, Path.Combine(SafeRoot, "..", "SomewhereElse")).IsAllowed);

    [Fact]
    public void Check_RefusesAProtectedRuleFolder() =>
        Assert.False(SafetyGuard.Check(Path.Combine(Windows, "System32", "kernel32.dll"), Path.Combine(Windows, "System32")).IsAllowed);

    [Fact]
    public void Check_RefusesAnItemInsideAProtectedFolderEvenWhenTheRuleIsFine()
    {
        var check = SafetyGuard.Check(Path.Combine(Windows, "System32", "kernel32.dll"), SafeRoot);

        // The item is not inside the rule folder at all, which is already refused.
        Assert.False(check.IsAllowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\file.tmp")]
    public void Check_RefusesPathsThatAreNotAbsolute(string itemPath) =>
        Assert.False(SafetyGuard.Check(itemPath, SafeRoot).IsAllowed);

    [Fact]
    public void Check_RefusesAnItemInsideAnExcludedFolder()
    {
        var excluded = Path.Combine(SafeRoot, "KeepMe");
        var check = SafetyGuard.Check(Path.Combine(excluded, "some-file.tmp"), SafeRoot, [excluded]);

        Assert.False(check.IsAllowed);
        Assert.Contains("excluded", check.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_AllowsAnItemOutsideTheExcludedFolder()
    {
        var excluded = Path.Combine(SafeRoot, "KeepMe");

        Assert.True(SafetyGuard.Check(SafeItem, SafeRoot, [excluded]).IsAllowed);
    }

    [Fact]
    public void Check_IsNotFooledByCasingOrATrailingSeparator()
    {
        var check = SafetyGuard.Check(SafeItem.ToUpperInvariant(), SafeRoot + Path.DirectorySeparatorChar);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void FindExclusion_ReturnsTheFolderThatCoversThePath()
    {
        var excluded = Path.Combine(SafeRoot, "KeepMe");
        var found = SafetyGuard.FindExclusion(Path.Combine(excluded, "deeper", "some-file.tmp"), [excluded]);

        Assert.Equal(excluded, found);
    }

    [Fact]
    public void FindExclusion_ReturnsNullForAnEmptyList() =>
        Assert.Null(SafetyGuard.FindExclusion(SafeItem, []));

    [Fact]
    public void IsInside_IsStrictAndCaseInsensitive()
    {
        Assert.True(SafetyGuard.IsInside(SafeItem, SafeRoot));
        Assert.False(SafetyGuard.IsInside(SafeRoot, SafeRoot));
        Assert.False(SafetyGuard.IsInside(SafeRoot, SafeItem));
        Assert.True(SafetyGuard.IsInside(SafeItem.ToUpperInvariant(), SafeRoot));
    }

    [Fact]
    public void IsSameOrInside_AcceptsTheFolderItself()
    {
        Assert.True(SafetyGuard.IsSameOrInside(SafeRoot, SafeRoot));
        Assert.True(SafetyGuard.IsSameOrInside(SafeItem, SafeRoot));
        Assert.False(SafetyGuard.IsSameOrInside(SafeRoot, SafeItem));
    }

    [Theory]
    [InlineData(@"C:\Temp\..\Windows", true)]
    [InlineData("..", true)]
    [InlineData("/tmp/../etc", true)]
    [InlineData(@"C:\Temp\file.tmp", false)]
    [InlineData("", false)]
    public void HasTraversalSegment_FindsParentSegments(string path, bool expected) =>
        Assert.Equal(expected, SafetyGuard.HasTraversalSegment(path));

    [Fact]
    public void TryNormalize_RejectsRelativeAndUnusablePaths()
    {
        Assert.Null(SafetyGuard.TryNormalize(null));
        Assert.Null(SafetyGuard.TryNormalize(string.Empty));
        Assert.Null(SafetyGuard.TryNormalize("relative\\path"));
        Assert.Null(SafetyGuard.TryNormalize("C:\\bad\0path"));
    }

    [Fact]
    public void TryNormalize_TrimsATrailingSeparator() =>
        Assert.Equal(SafeRoot, SafetyGuard.TryNormalize(SafeRoot + Path.DirectorySeparatorChar));

    [Fact]
    public void ProtectedPaths_IsNotEmpty() => Assert.NotEmpty(SafetyGuard.ProtectedPaths);

    [Fact]
    public void PersonalFolders_IsNotEmpty() => Assert.NotEmpty(SafetyGuard.PersonalFolders);

    [Theory]
    [InlineData(Environment.SpecialFolder.Desktop)]
    [InlineData(Environment.SpecialFolder.MyDocuments)]
    [InlineData(Environment.SpecialFolder.MyPictures)]
    [InlineData(Environment.SpecialFolder.MyVideos)]
    [InlineData(Environment.SpecialFolder.MyMusic)]
    public void DescribeSystemProtection_AllowsTheUsersOwnFolders(Environment.SpecialFolder folder)
    {
        var resolved = Environment.GetFolderPath(folder);

        // Guards against the check passing only because this machine has no such folder.
        Assert.False(string.IsNullOrWhiteSpace(resolved), $"This machine has no {folder} folder to test with.");

        var path = Path.Combine(resolved, "something.jpg");

        Assert.Null(SafetyGuard.DescribeSystemProtection(path));
        Assert.NotNull(SafetyGuard.DescribePersonalFolder(path));

        // Deep clean still leaves them alone, so the two views have to agree.
        Assert.NotNull(SafetyGuard.DescribeProtection(path));
        Assert.True(SafetyGuard.IsProtected(path));
    }

    [Fact]
    public void DescribePersonalFolder_FindsDownloadsAndOneDrive()
    {
        Assert.NotNull(SafetyGuard.DescribePersonalFolder(Path.Combine(Profile, "Downloads", "installer.zip")));
        Assert.NotNull(SafetyGuard.DescribePersonalFolder(Path.Combine(Profile, "OneDrive", "notes.txt")));
    }

    [Fact]
    public void DescribeSystemProtection_StillRefusesProgramAndCredentialFolders()
    {
        Assert.NotNull(SafetyGuard.DescribeSystemProtection(Path.Combine(Windows, "System32", "kernel32.dll")));
        Assert.NotNull(SafetyGuard.DescribeSystemProtection(Path.Combine(ProgramFiles, "AnApp", "app.exe")));
        Assert.NotNull(SafetyGuard.DescribeSystemProtection(Path.Combine(Profile, ".ssh", "id_rsa")));
        Assert.NotNull(SafetyGuard.DescribeSystemProtection(Path.Combine(Profile, ".gnupg", "pubring.kbx")));

        // Keys are never treated as ordinary user data, so duplicates never look at them.
        Assert.Null(SafetyGuard.DescribePersonalFolder(Path.Combine(Profile, ".ssh", "id_rsa")));
    }

    [Fact]
    public void Check_StillRefusesAPersonalFolderForDeepClean()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var check = SafetyGuard.Check(Path.Combine(folder, "report.docx"), folder);

        Assert.False(check.IsAllowed);
        Assert.Contains("personal", check.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckDuplicate_AllowsACopyInAPersonalFolderButAsksForTheRecycleBin()
    {
        var check = SafetyGuard.CheckDuplicate(Path.Combine(SafeRoot, "photo.jpg"), SafeRoot, personalFolders: [SafeRoot]);

        Assert.True(check.IsAllowed);
        Assert.True(check.MustRecycle);
        Assert.Equal(string.Empty, check.Reason);
    }

    [Fact]
    public void CheckDuplicate_AllowsACopyOutsideAnyProtectedFolder()
    {
        var check = SafetyGuard.CheckDuplicate(SafeItem, SafeRoot);

        Assert.True(check.IsAllowed);
        Assert.False(check.MustRecycle);
    }

    [Fact]
    public void CheckDuplicate_RefusesACopyInASystemFolder()
    {
        var system32 = Path.Combine(Windows, "System32");
        var check = SafetyGuard.CheckDuplicate(Path.Combine(system32, "kernel32.dll"), system32);

        Assert.False(check.IsAllowed);
        Assert.False(check.MustRecycle);
        Assert.False(string.IsNullOrWhiteSpace(check.Reason));
    }

    [Fact]
    public void CheckDuplicate_HonoursTheExclusionList()
    {
        var excluded = Path.Combine(SafeRoot, "KeepMe");
        var check = SafetyGuard.CheckDuplicate(Path.Combine(excluded, "some-file.tmp"), SafeRoot, [excluded]);

        Assert.False(check.IsAllowed);
    }

    [Fact]
    public void CheckDuplicate_RefusesAnItemThatIsNoLongerInsideItsFolder() =>
        Assert.False(SafetyGuard.CheckDuplicate(Path.Combine(SafeRoot + ".Other", "some-file.tmp"), SafeRoot).IsAllowed);

    [Fact]
    public void CheckDuplicate_RefusesTheFolderItself() =>
        Assert.False(SafetyGuard.CheckDuplicate(SafeRoot, SafeRoot).IsAllowed);
}
