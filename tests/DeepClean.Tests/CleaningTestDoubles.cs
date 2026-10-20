using System.Text;
using DeepClean.Core.Cleaning;

namespace DeepClean.Tests;

/// <summary>
/// Stands in for the Recycle Bin so the cleaning logic can be exercised without touching
/// a real disk or the Windows shell.
/// </summary>
internal sealed class FakeFileRemover : IFileRemover
{
    public List<(string Path, bool IsDirectory, bool Permanent)> Calls { get; } = [];

    /// <summary>What the remover should answer with; null means it worked.</summary>
    public Func<string, string?> ResultFactory { get; set; } = _ => null;

    /// <summary>When set, the remover throws instead of answering, as the shell does for a locked file.</summary>
    public Exception? Throw { get; set; }

    public string? MoveToRecycleBin(string path, bool isDirectory) => Record(path, isDirectory, permanent: false);

    public string? DeletePermanently(string path, bool isDirectory) => Record(path, isDirectory, permanent: true);

    private string? Record(string path, bool isDirectory, bool permanent)
    {
        Calls.Add((path, isDirectory, permanent));

        if (Throw is not null)
        {
            throw Throw;
        }

        return ResultFactory(path);
    }
}

/// <summary>A throwaway folder tree that deletes itself afterwards.</summary>
internal sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "DeepClean.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Normalized, so it looks exactly like a path the rule catalog would produce.</summary>
    public string Root { get; }

    public string CreateFile(string relativePath, int sizeBytes, int ageDays)
    {
        var full = Path.Combine(Root, relativePath);
        var folder = Path.GetDirectoryName(full)!;

        Directory.CreateDirectory(folder);

        File.WriteAllBytes(full, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddDays(-ageDays));

        return full;
    }

    /// <summary>Writes a file holding exactly these bytes, so two files built the same way match.</summary>
    public string CreateFile(string relativePath, byte[] content, int ageDays = 0)
    {
        var full = Path.Combine(Root, relativePath);
        var folder = Path.GetDirectoryName(full)!;

        Directory.CreateDirectory(folder);

        File.WriteAllBytes(full, content);
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddDays(-ageDays));

        return full;
    }

    /// <summary>Writes a text file, for the cases where the contents themselves matter.</summary>
    public string CreateFile(string relativePath, string content, int ageDays = 0) =>
        CreateFile(relativePath, Encoding.UTF8.GetBytes(content), ageDays);

    public string CreateFolder(string relativePath)
    {
        var full = Path.Combine(Root, relativePath);

        Directory.CreateDirectory(full);

        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception)
        {
            // A leftover test folder in the temporary directory is not worth failing a run over.
        }
    }
}

/// <summary>Collects progress reports on the calling thread, unlike <see cref="Progress{T}"/>.</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Values { get; } = [];

    public void Report(T value) => Values.Add(value);
}
