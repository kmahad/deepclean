using System.IO;
using System.Text;

namespace DeepClean.Core.Cleaning;

/// <summary>
/// The complete list of folders DeepClean is allowed to look at. Each rule names exactly one
/// folder and only that folder's contents are ever reported. Apart from expanding the browser
/// profile folders found on this machine, the list is fixed, which keeps the reachable part
/// of the file system something a person can read and check.
/// </summary>
public static class JunkRuleCatalog
{
    /// <summary>Cache folders a Chromium based browser keeps inside each profile.</summary>
    private static readonly string[] ChromiumProfileCacheFolders =
    [
        "Cache",
        "Code Cache",
        "GPUCache",
        "DawnGraphiteCache",
        "DawnWebGPUCache",
        "GrShaderCache",
        "ShaderCache",
        "Media Cache",
    ];

    /// <summary>Cache folders shared by every profile of one Chromium based installation.</summary>
    private static readonly string[] ChromiumSharedCacheFolders =
    [
        "ShaderCache",
        "GrShaderCache",
        "GraphiteDawnCache",
    ];

    /// <summary>Cache folders a Firefox profile keeps.</summary>
    private static readonly string[] FirefoxProfileCacheFolders =
    [
        "cache2",
        "startupCache",
        "shader-cache",
        "thumbnails",
    ];

    /// <summary>Builds the list against the machine the application is currently running on.</summary>
    public static IReadOnlyList<JunkRule> Create()
    {
        var rules = new List<JunkRule>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        AddUserTemporaryFiles(rules, local);
        AddWindowsTemporaryFiles(rules, windows);
        AddBrowserCaches(rules, local);
        AddThumbnailCaches(rules, local);
        AddCrashDumps(rules, local);
        AddWindowsUpdateLeftovers(rules, windows);
        AddDeliveryOptimization(rules, local, windows);

        return rules;
    }

    private static void AddUserTemporaryFiles(List<JunkRule> rules, string local)
    {
        Add(
            rules,
            id: "user-temp",
            category: JunkCategory.UserTemporaryFiles,
            title: "Your temporary files",
            description: "Files that installers and applications left behind in your own temporary folder.",
            targetPath: Path.Combine(local, "Temp"),
            minimumAgeDays: 2);
    }

    private static void AddWindowsTemporaryFiles(List<JunkRule> rules, string windows)
    {
        Add(
            rules,
            id: "windows-temp",
            category: JunkCategory.WindowsTemporaryFiles,
            title: "Windows temporary files",
            description: "Files written by Windows and background services to the machine wide temporary folder.",
            targetPath: Path.Combine(windows, "Temp"),
            minimumAgeDays: 3,
            requiresElevation: true);

        Add(
            rules,
            id: "windows-system-temp",
            category: JunkCategory.WindowsTemporaryFiles,
            title: "Windows servicing temporary files",
            description: "Scratch space used while Windows installs updates. Files still in use are left alone.",
            targetPath: Path.Combine(windows, "SystemTemp"),
            minimumAgeDays: 3,
            requiresElevation: true);
    }

    private static void AddBrowserCaches(List<JunkRule> rules, string local)
    {
        var browsers = new (string Id, string Name, string Path)[]
        {
            ("edge", "Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("chrome", "Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("brave", "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("vivaldi", "Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            ("chromium", "Chromium", Path.Combine(local, "Chromium", "User Data")),
        };

        foreach (var browser in browsers)
        {
            AddChromiumBrowser(rules, browser.Id, browser.Name, browser.Path);
        }

        AddFirefox(rules, Path.Combine(local, "Mozilla", "Firefox", "Profiles"));
    }

    private static void AddChromiumBrowser(List<JunkRule> rules, string id, string name, string userDataPath)
    {
        if (!Directory.Exists(userDataPath))
        {
            return;
        }

        foreach (var shared in ChromiumSharedCacheFolders)
        {
            Add(
                rules,
                id: $"{id}-{Slug(shared)}",
                category: JunkCategory.BrowserCache,
                title: $"{name} shared cache",
                description: $"Shader and graphics caches {name} rebuilds by itself. Bookmarks, history, passwords, cookies and signed in sessions are never touched.",
                targetPath: Path.Combine(userDataPath, shared),
                minimumAgeDays: 2,
                maximumDepth: 2);
        }

        foreach (var profile in EnumerateDirectories(userDataPath))
        {
            // A real profile always holds a "Preferences" file, which skips leftover folders
            // such as "Crashpad" or a stale "BrowserMetrics".
            if (!File.Exists(Path.Combine(profile, "Preferences")))
            {
                continue;
            }

            var profileName = Path.GetFileName(profile);

            foreach (var cache in ChromiumProfileCacheFolders)
            {
                Add(
                    rules,
                    id: $"{id}-{Slug(profileName)}-{Slug(cache)}",
                    category: JunkCategory.BrowserCache,
                    title: $"{name} cache ({profileName})",
                    description: $"Web page, code and graphics caches for the {profileName} profile of {name}. Bookmarks, history, passwords, cookies and signed in sessions are never touched.",
                    targetPath: Path.Combine(profile, cache),
                    minimumAgeDays: 1,
                    maximumDepth: 3);
            }
        }
    }

    private static void AddFirefox(List<JunkRule> rules, string profilesPath)
    {
        if (!Directory.Exists(profilesPath))
        {
            return;
        }

        foreach (var profile in EnumerateDirectories(profilesPath))
        {
            // Firefox profiles are identified by their prefs.js file.
            if (!File.Exists(Path.Combine(profile, "prefs.js")))
            {
                continue;
            }

            var profileName = Path.GetFileName(profile);

            foreach (var cache in FirefoxProfileCacheFolders)
            {
                Add(
                    rules,
                    id: $"firefox-{Slug(profileName)}-{Slug(cache)}",
                    category: JunkCategory.BrowserCache,
                    title: $"Firefox cache ({profileName})",
                    description: $"Web page and thumbnail caches for the {profileName} profile of Firefox. Bookmarks, history, passwords, cookies and signed in sessions are never touched.",
                    targetPath: Path.Combine(profile, cache),
                    minimumAgeDays: 1,
                    maximumDepth: 3);
            }
        }
    }

    private static void AddThumbnailCaches(List<JunkRule> rules, string local)
    {
        var explorer = Path.Combine(local, "Microsoft", "Windows", "Explorer");

        Add(
            rules,
            id: "thumbnail-cache",
            category: JunkCategory.ThumbnailCache,
            title: "Thumbnail cache",
            description: "The picture preview database Explorer rebuilds the next time you open a folder.",
            targetPath: explorer,
            minimumAgeDays: 7,
            maximumDepth: 1,
            searchPattern: "thumbcache_*.db");

        Add(
            rules,
            id: "icon-cache",
            category: JunkCategory.ThumbnailCache,
            title: "Icon cache",
            description: "The icon database Explorer rebuilds by itself whenever it needs to.",
            targetPath: explorer,
            minimumAgeDays: 7,
            maximumDepth: 1,
            searchPattern: "iconcache_*.db");

        Add(
            rules,
            id: "thumbnail-cleanup-queue",
            category: JunkCategory.ThumbnailCache,
            title: "Pending thumbnail clean-up",
            description: "Thumbnail copies Explorer has already queued for removal.",
            targetPath: Path.Combine(explorer, "ThumbCacheToDelete"),
            minimumAgeDays: 1,
            maximumDepth: 2);
    }

    private static void AddCrashDumps(List<JunkRule> rules, string local)
    {
        Add(
            rules,
            id: "crash-dumps",
            category: JunkCategory.CrashDumps,
            title: "Application crash dumps",
            description: "Memory dumps Windows wrote when a program stopped unexpectedly.",
            targetPath: Path.Combine(local, "CrashDumps"),
            minimumAgeDays: 7,
            maximumDepth: 2);

        var wer = Path.Combine(local, "Microsoft", "Windows", "WER");

        Add(
            rules,
            id: "error-report-queue",
            category: JunkCategory.CrashDumps,
            title: "Queued error reports",
            description: "Error reports waiting to be sent to Microsoft.",
            targetPath: Path.Combine(wer, "ReportQueue"),
            minimumAgeDays: 7,
            maximumDepth: 2);

        Add(
            rules,
            id: "error-report-archive",
            category: JunkCategory.CrashDumps,
            title: "Archived error reports",
            description: "Error reports Windows has already finished with.",
            targetPath: Path.Combine(wer, "ReportArchive"),
            minimumAgeDays: 7,
            maximumDepth: 2);

        Add(
            rules,
            id: "error-report-temp",
            category: JunkCategory.CrashDumps,
            title: "Error reporting working files",
            description: "Scratch files left behind while Windows prepared an error report.",
            targetPath: Path.Combine(wer, "Temp"),
            minimumAgeDays: 3,
            maximumDepth: 2);
    }

    private static void AddWindowsUpdateLeftovers(List<JunkRule> rules, string windows)
    {
        Add(
            rules,
            id: "installed-update-packages",
            category: JunkCategory.WindowsUpdateLeftovers,
            title: "Installed update packages",
            description: "Update packages Windows has already installed and no longer needs. Nothing newer than two weeks old is ever reported, and packages still being written are skipped.",
            targetPath: Path.Combine(windows, "SoftwareDistribution", "Download"),
            minimumAgeDays: 14,
            requiresElevation: true,
            maximumDepth: 4);
    }

    private static void AddDeliveryOptimization(List<JunkRule> rules, string local, string windows)
    {
        Add(
            rules,
            id: "delivery-optimization-cache",
            category: JunkCategory.DeliveryOptimization,
            title: "Delivery Optimization cache",
            description: "Update content Delivery Optimization has finished handing out to this and other computers.",
            targetPath: Path.Combine(
                windows,
                "ServiceProfiles",
                "NetworkService",
                "AppData",
                "Local",
                "Microsoft",
                "Windows",
                "DeliveryOptimization",
                "Cache"),
            minimumAgeDays: 7,
            requiresElevation: true,
            maximumDepth: 3);

        Add(
            rules,
            id: "delivery-optimization-user-cache",
            category: JunkCategory.DeliveryOptimization,
            title: "Your Delivery Optimization cache",
            description: "The same cache for the account you are signed in with.",
            targetPath: Path.Combine(local, "Microsoft", "Windows", "DeliveryOptimization", "Cache"),
            minimumAgeDays: 7,
            maximumDepth: 3);
    }

    private static void Add(
        List<JunkRule> rules,
        string id,
        JunkCategory category,
        string title,
        string description,
        string targetPath,
        int minimumAgeDays,
        bool requiresElevation = false,
        int maximumDepth = 3,
        string searchPattern = "*")
    {
        var normalized = SafetyGuard.TryNormalize(targetPath);

        // A rule that does not exist on this machine, that repeats an existing identifier or
        // that points anywhere protected is dropped without a word of complaint.
        if (normalized is null || !Directory.Exists(normalized))
        {
            return;
        }

        if (SafetyGuard.DescribeProtection(normalized) is not null)
        {
            return;
        }

        if (rules.Any(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        rules.Add(new JunkRule
        {
            Id = id,
            Category = category,
            Title = title,
            Description = description,
            TargetPath = normalized,
            SearchPattern = searchPattern,
            MinimumAgeDays = minimumAgeDays,
            RequiresElevation = requiresElevation,
            MaximumDepth = maximumDepth,
        });
    }

    private static IEnumerable<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');

        return slug.Length == 0 ? "item" : slug;
    }
}
