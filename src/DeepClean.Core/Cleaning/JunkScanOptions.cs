namespace DeepClean.Core.Cleaning;

/// <summary>What a scan should look at and what it must leave alone.</summary>
public sealed class JunkScanOptions
{
    /// <summary>Categories the user asked for. Everything else is skipped.</summary>
    public IReadOnlyCollection<JunkCategory> Categories { get; init; } = Enum.GetValues<JunkCategory>();

    /// <summary>Folders, and everything below them, that must never be reported or removed.</summary>
    public IReadOnlyCollection<string> ExcludedPaths { get; init; } = [];

    /// <summary>Extra age floor applied on top of each rule's own minimum, in days.</summary>
    public int MinimumAgeDays { get; init; }

    /// <summary>
    /// True when the process has administrator rights. Rules that need them are skipped
    /// rather than attempted and failing.
    /// </summary>
    public bool IsElevated { get; init; }

    /// <summary>Files smaller than this are ignored. Zero keeps everything.</summary>
    public long MinimumSizeBytes { get; init; }

    /// <summary>Upper bound so a scan of a huge folder cannot run away or exhaust memory.</summary>
    public int MaximumItems { get; init; } = 100_000;
}
