using System.IO;

namespace DeepClean.Core.Cleaning;

/// <summary>Result of a safety inspection.</summary>
public readonly record struct SafetyCheck(bool IsAllowed, string Reason)
{
    public static SafetyCheck Allow() => new(true, string.Empty);

    public static SafetyCheck Block(string reason) => new(false, reason);
}

/// <summary>
/// Result of a check made for a duplicate copy. A copy that sits in one of the user's own folders
/// is allowed, but only when it can still be recovered, which is what <see cref="MustRecycle"/>
/// asks for. System locations are refused outright either way.
/// </summary>
public readonly record struct DuplicateSafetyCheck(bool IsAllowed, string Reason, bool MustRecycle)
{
    public static DuplicateSafetyCheck Allow(bool mustRecycle) => new(true, string.Empty, mustRecycle);

    public static DuplicateSafetyCheck Block(string reason) => new(false, reason, false);
}

/// <summary>
/// The second line of defence behind the allow list. Every path DeepClean reports or removes
/// passes through here, so a wrong rule, a stale item list or a hand edited settings file
/// still cannot reach anything that matters. Deliberately free of file system access so the
/// whole policy can be unit tested.
/// </summary>
public static class SafetyGuard
{
    private static readonly string[] ProtectedExact = BuildProtectedExact();
    private static readonly string[] SystemTrees = BuildSystemTrees();
    private static readonly string[] PersonalTrees = BuildPersonalTrees();

    /// <summary>Every path that is refused outright. Useful for logging and diagnostics.</summary>
    public static IReadOnlyList<string> ProtectedPaths { get; } = [.. ProtectedExact, .. SystemTrees, .. PersonalTrees];

    /// <summary>
    /// The folders that hold the user's own files. Deep clean never touches them, but the
    /// duplicate finder may look inside them, and a copy taken from one of them is always put in
    /// the Recycle Bin instead of being deleted outright.
    /// </summary>
    public static IReadOnlyList<string> PersonalFolders { get; } = [.. PersonalTrees];

    /// <summary>
    /// Checks that <paramref name="itemPath"/> may be removed, given that a scan found it
    /// inside <paramref name="rootPath"/>.
    /// </summary>
    public static SafetyCheck Check(string? itemPath, string? rootPath, IReadOnlyCollection<string>? exclusions = null)
    {
        var check = Inspect(itemPath, rootPath, exclusions, allowPersonalFolders: false);

        return check.IsAllowed ? SafetyCheck.Allow() : SafetyCheck.Block(check.Reason);
    }

    /// <summary>
    /// Checks a duplicate copy. Unlike <see cref="Check"/>, a copy that sits inside one of the
    /// user's own folders is allowed through, because finding duplicates there is the whole point
    /// of the feature; the check then reports that the copy must go to the Recycle Bin, so the
    /// user can always get it back. System locations stay refused.
    /// </summary>
    public static DuplicateSafetyCheck CheckDuplicate(
        string? itemPath,
        string? rootPath,
        IReadOnlyCollection<string>? exclusions = null,
        IReadOnlyCollection<string>? personalFolders = null) =>
        Inspect(itemPath, rootPath, exclusions, allowPersonalFolders: true, personalFolders);

    private static DuplicateSafetyCheck Inspect(
        string? itemPath,
        string? rootPath,
        IReadOnlyCollection<string>? exclusions,
        bool allowPersonalFolders,
        IReadOnlyCollection<string>? personalFolders = null)
    {
        if (HasTraversalSegment(itemPath))
        {
            return DuplicateSafetyCheck.Block("The item path contains a '..' segment and was refused.");
        }

        var item = TryNormalize(itemPath);

        if (item is null)
        {
            return DuplicateSafetyCheck.Block("The item path is empty or not a valid absolute path.");
        }

        if (HasTraversalSegment(rootPath))
        {
            return DuplicateSafetyCheck.Block("The folder the item came from contains a '..' segment and was refused.");
        }

        var root = TryNormalize(rootPath);

        if (root is null)
        {
            return DuplicateSafetyCheck.Block("The folder the item came from is not a valid absolute path.");
        }

        if (string.Equals(item, root, StringComparison.OrdinalIgnoreCase))
        {
            return DuplicateSafetyCheck.Block("A rule points at this folder, and a rule's own folder is never removed.");
        }

        if (!IsInside(item, root))
        {
            return DuplicateSafetyCheck.Block("The item is no longer inside the folder the scan found it in.");
        }

        // System locations are refused for every caller, duplicates included.
        var protection = DescribeSystemProtection(root);

        if (protection is not null)
        {
            return DuplicateSafetyCheck.Block($"The source folder is protected ({protection}).");
        }

        protection = DescribeSystemProtection(item);

        if (protection is not null)
        {
            return DuplicateSafetyCheck.Block($"The item is protected ({protection}).");
        }

        var exclusion = FindExclusion(item, exclusions);

        if (exclusion is not null)
        {
            return DuplicateSafetyCheck.Block($"The item is inside the excluded folder {exclusion}.");
        }

        var personal = FindPersonalFolder(item, personalFolders) ?? FindPersonalFolder(root, personalFolders);

        if (personal is null)
        {
            return DuplicateSafetyCheck.Allow(mustRecycle: false);
        }

        return allowPersonalFolders
            ? DuplicateSafetyCheck.Allow(mustRecycle: true)
            : DuplicateSafetyCheck.Block($"The item is inside your personal folder {personal}.");
    }

    private static string? FindPersonalFolder(string? path, IReadOnlyCollection<string>? personalFolders)
    {
        if (personalFolders is null)
        {
            return DescribePersonalFolder(path);
        }

        var full = TryNormalize(path);

        if (full is null)
        {
            return null;
        }

        foreach (var raw in personalFolders)
        {
            var folder = TryNormalize(raw);

            if (folder is not null && IsSameOrInside(full, folder))
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the location that covers <paramref name="path"/> and that DeepClean must never
    /// touch, or null when it is safe. Covers both system locations and the user's own folders.
    /// </summary>
    public static string? DescribeProtection(string? path) =>
        DescribeSystemProtection(path) ?? DescribePersonalFolder(path);

    /// <summary>
    /// Returns the system location that covers <paramref name="path"/>, or null. These are the
    /// paths that are refused outright no matter who asks, the duplicate finder included.
    /// </summary>
    public static string? DescribeSystemProtection(string? path)
    {
        var full = TryNormalize(path);

        if (full is null)
        {
            return "not a valid absolute path";
        }

        if (IsDriveRoot(full))
        {
            return "a drive root";
        }

        foreach (var candidate in ProtectedExact)
        {
            if (string.Equals(full, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        foreach (var candidate in SystemTrees)
        {
            if (IsSameOrInside(full, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Returns the user folder that covers <paramref name="path"/>, or null.</summary>
    public static string? DescribePersonalFolder(string? path)
    {
        var full = TryNormalize(path);

        if (full is null)
        {
            return null;
        }

        foreach (var candidate in PersonalTrees)
        {
            if (IsSameOrInside(full, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool IsProtected(string? path) => DescribeProtection(path) is not null;

    /// <summary>Returns the excluded folder that covers <paramref name="path"/>, or null.</summary>
    public static string? FindExclusion(string? path, IReadOnlyCollection<string>? exclusions)
    {
        if (exclusions is null || exclusions.Count == 0)
        {
            return null;
        }

        var full = TryNormalize(path);

        if (full is null)
        {
            return null;
        }

        foreach (var raw in exclusions)
        {
            var exclusion = TryNormalize(raw);

            if (exclusion is not null && IsSameOrInside(full, exclusion))
            {
                return exclusion;
            }
        }

        return null;
    }

    public static bool IsSameOrInside(string? path, string? folder)
    {
        var candidate = TryNormalize(path);
        var root = TryNormalize(folder);

        if (candidate is null || root is null)
        {
            return false;
        }

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) || IsInside(candidate, root);
    }

    /// <summary>True when <paramref name="path"/> sits strictly below <paramref name="folder"/>.</summary>
    public static bool IsInside(string? path, string? folder)
    {
        var candidate = TryNormalize(path);
        var root = TryNormalize(folder);

        if (candidate is null || root is null)
        {
            return false;
        }

        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasTraversalSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var segment in path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "..")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Canonical form of a path, or null when the input is not an absolute path that can be
    /// resolved. Resolving here means a later comparison can never be fooled by casing,
    /// trailing separators or an unresolved relative segment.
    /// </summary>
    public static string? TryNormalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            return null;
        }

        try
        {
            if (!Path.IsPathRooted(path))
            {
                return null;
            }

            var full = Path.GetFullPath(path);
            var trimmed = Path.TrimEndingDirectorySeparator(full);

            return string.IsNullOrEmpty(trimmed) ? full : trimmed;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool IsDriveRoot(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);

        return !string.IsNullOrEmpty(root) && string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] BuildProtectedExact()
    {
        var paths = new List<string>();

        void Add(string? candidate)
        {
            var normalized = TryNormalize(candidate);

            if (normalized is not null && !paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(normalized);
            }
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Add(AppPaths.RootDirectory);
        Add(AppPaths.LogDirectory);

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                Add(drive.RootDirectory.FullName);
            }
        }
        catch (Exception)
        {
            // Drive enumeration is best effort; the folders above already cover the system drive root.
        }

        return [.. paths];
    }

    /// <summary>
    /// Locations that are refused for every caller: Windows and its system folders, installed
    /// programs, machine wide data, roaming settings, credential folders and DeepClean's own
    /// files.
    /// </summary>
    private static string[] BuildSystemTrees()
    {
        var paths = new List<string>();

        void Add(string? candidate)
        {
            var normalized = TryNormalize(candidate);

            if (normalized is not null && !paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(normalized);
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var folder in new[]
                 {
                     "System32", "SysWOW64", "WinSxS", "servicing", "assembly", "Microsoft.NET",
                     "Boot", "Fonts", "SystemApps", "Installer", "Speech", "CSC", "Inf",
                 })
        {
            Add(Path.Combine(windows, folder));
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        // Favourites are browser bookmarks rather than bulk data, and the two dotted folders
        // hold keys, so they stay out of reach of both cleaning and duplicate removal.
        Add(Environment.GetFolderPath(Environment.SpecialFolder.Favorites));

        if (!string.IsNullOrWhiteSpace(profile))
        {
            Add(Path.Combine(profile, ".ssh"));
            Add(Path.Combine(profile, ".gnupg"));
        }

        Add(AppPaths.RootDirectory);

        // Built from the Windows folder's root rather than the SystemDrive variable: "C:" on
        // its own is a drive relative path, so combining it produced "C:$Recycle.Bin", which
        // resolved somewhere else entirely and protected nothing.
        var systemRoot = Path.GetPathRoot(windows);

        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            Add(Path.Combine(systemRoot, "$Recycle.Bin"));
        }

        return [.. paths];
    }

    /// <summary>
    /// The user's own folders. Deep clean leaves them alone, but the duplicate finder may look
    /// inside them, and anything removed from one of them is put in the Recycle Bin rather than
    /// deleted outright.
    /// </summary>
    private static string[] BuildPersonalTrees()
    {
        var paths = new List<string>();

        void Add(string? candidate)
        {
            var normalized = TryNormalize(candidate);

            if (normalized is not null && !paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(normalized);
            }
        }

        // The resolved location, not the profile plus the folder's name: these folders can be
        // redirected out of the profile, which is what OneDrive does when it moves them.
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.Desktop,
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyVideos,
                     Environment.SpecialFolder.MyMusic,
                 })
        {
            Add(Environment.GetFolderPath(folder));
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(profile))
        {
            Add(Path.Combine(profile, "Downloads"));
            Add(Path.Combine(profile, "OneDrive"));
        }

        Add(Environment.GetEnvironmentVariable("OneDrive"));

        return [.. paths];
    }
}
