namespace DeepClean.Core.Cleaning;

/// <summary>
/// Groups junk by what it is and how risky removing it is. The declaration order also
/// drives the order the categories appear in the interface.
/// </summary>
public enum JunkCategory
{
    UserTemporaryFiles,
    WindowsTemporaryFiles,
    BrowserCache,
    ThumbnailCache,
    CrashDumps,
    WindowsUpdateLeftovers,
    DeliveryOptimization,
}

/// <summary>Human readable text for <see cref="JunkCategory"/>.</summary>
public static class JunkCategories
{
    public static string DisplayName(JunkCategory category) => category switch
    {
        JunkCategory.UserTemporaryFiles => "Temporary files",
        JunkCategory.WindowsTemporaryFiles => "Windows temporary files",
        JunkCategory.BrowserCache => "Browser caches",
        JunkCategory.ThumbnailCache => "Thumbnail and icon cache",
        JunkCategory.CrashDumps => "Crash dumps and error reports",
        JunkCategory.WindowsUpdateLeftovers => "Windows Update leftovers",
        JunkCategory.DeliveryOptimization => "Delivery Optimization cache",
        _ => category.ToString(),
    };

    public static string Description(JunkCategory category) => category switch
    {
        JunkCategory.UserTemporaryFiles =>
            "Files left behind by installers and applications in your own temporary folders.",
        JunkCategory.WindowsTemporaryFiles =>
            "The machine wide temporary folder that Windows and background services write to.",
        JunkCategory.BrowserCache =>
            "Copies of web pages kept by Edge, Chrome and Firefox. Bookmarks, history, passwords, cookies and saved sessions are never included.",
        JunkCategory.ThumbnailCache =>
            "The picture and icon databases Explorer rebuilds by itself the next time you open a folder.",
        JunkCategory.CrashDumps =>
            "Memory dumps and error report queues written when a program stopped unexpectedly.",
        JunkCategory.WindowsUpdateLeftovers =>
            "Update packages Windows has already installed. Nothing newer than two weeks old is ever reported.",
        JunkCategory.DeliveryOptimization =>
            "Cached update content that Delivery Optimization has finished handing out to this and other computers.",
        _ => string.Empty,
    };
}
