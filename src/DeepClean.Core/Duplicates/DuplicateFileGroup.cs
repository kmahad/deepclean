namespace DeepClean.Core.Duplicates;

/// <summary>One file that holds exactly the same bytes as the others in its group.</summary>
/// <param name="Path">Where the copy is.</param>
/// <param name="RootPath">The folder that was searched for, which lets a removal re-check the copy.</param>
/// <param name="SizeBytes">The size of the file.</param>
/// <param name="LastWriteTimeUtc">Used to suggest which copy is most likely worth keeping.</param>
public sealed record DuplicateCandidate(
    string Path,
    string RootPath,
    long SizeBytes,
    DateTime LastWriteTimeUtc);

/// <summary>Files that hold exactly the same bytes, found under one or more of the searched folders.</summary>
/// <param name="Hash">The SHA-256 of the contents, as hex.</param>
/// <param name="SizeBytes">The size every copy in the group has.</param>
/// <param name="Candidates">Every copy found, in the order the scan met them.</param>
public sealed record DuplicateFileGroup(
    string Hash,
    long SizeBytes,
    IReadOnlyList<DuplicateCandidate> Candidates)
{
    /// <summary>Bytes that would come back if one copy was kept and the rest were removed.</summary>
    public long WastedBytes => SizeBytes * (Candidates.Count - 1);
}
