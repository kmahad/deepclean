namespace DeepClean.Core.Cleaning;

/// <summary>
/// One allowlisted location that DeepClean may clean. A rule always names a single
/// folder and only that folder's contents are ever reported or removed, which keeps the
/// set of reachable paths something the user can read and reason about.
/// </summary>
public sealed record JunkRule
{
    /// <summary>Stable identifier, used for logging and for the item rows.</summary>
    public required string Id { get; init; }

    public required JunkCategory Category { get; init; }

    /// <summary>Short label shown in summaries, for example "Edge cache (Default)".</summary>
    public required string Title { get; init; }

    public required string Description { get; init; }

    /// <summary>Absolute folder this rule may clean. The folder itself is never removed.</summary>
    public required string TargetPath { get; init; }

    /// <summary>File glob applied inside <see cref="TargetPath"/>. Defaults to every file.</summary>
    public string SearchPattern { get; init; } = "*";

    /// <summary>
    /// Smallest age, in days, a file must reach before it is reported. Keeping this above
    /// zero is what stops DeepClean from touching files that are still being written.
    /// </summary>
    public int MinimumAgeDays { get; init; } = 1;

    /// <summary>True when reading and removing this location requires administrator rights.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>How far below <see cref="TargetPath"/> to look when a folder is too recent to remove whole.</summary>
    public int MaximumDepth { get; init; } = 3;
}
