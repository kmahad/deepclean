namespace DeepClean.Core.Cleaning;

/// <summary>Reported periodically while a scan walks a rule's folder.</summary>
public sealed record JunkScanProgress(
    JunkCategory Category,
    string RuleTitle,
    int ItemCount,
    long TotalBytes,
    string CurrentPath);
