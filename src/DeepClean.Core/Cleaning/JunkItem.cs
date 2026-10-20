namespace DeepClean.Core.Cleaning;

/// <summary>
/// One thing the scan found. Carries the rule it came from so a deletion can be checked
/// against the allowlisted folder it was discovered in, even if the list has been sitting
/// in the interface for a while.
/// </summary>
public sealed record JunkItem
{
    public required string Path { get; init; }

    /// <summary>The rule's allowlisted folder. The item must stay inside it to be removable.</summary>
    public required string RootPath { get; init; }

    public required JunkCategory Category { get; init; }

    public required string RuleId { get; init; }

    public required string RuleTitle { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>True when the whole folder is removed in one step rather than a single file.</summary>
    public required bool IsDirectory { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }
}
