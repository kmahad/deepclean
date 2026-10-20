namespace DeepClean.Core.Duplicates;

/// <summary>What a duplicate scan should look at, and how much work it is allowed to do.</summary>
public sealed class DuplicateScanOptions
{
    /// <summary>Folders to search. Everything below them is included, links excepted.</summary>
    public IReadOnlyCollection<string> Roots { get; init; } = [];

    /// <summary>Folders, and everything below them, that must never be listed.</summary>
    public IReadOnlyCollection<string> ExcludedPaths { get; init; } = [];

    /// <summary>Files smaller than this are ignored. Zero means every file counts.</summary>
    public long MinimumSizeBytes { get; init; } = 1024 * 1024;

    /// <summary>Upper bound on the number of files considered, so a huge folder cannot run away.</summary>
    public int MaximumFiles { get; init; } = 200_000;

    /// <summary>How many bytes at the start of a file are compared before the whole file is hashed.</summary>
    public int SampleBytes { get; init; } = 64 * 1024;
}
