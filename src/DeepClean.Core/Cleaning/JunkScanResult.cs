namespace DeepClean.Core.Cleaning;

/// <summary>Everything one scan found, plus how the scan itself went.</summary>
public sealed class JunkScanResult
{
    public JunkScanResult(
        IReadOnlyList<JunkItem> items,
        bool wasCancelled,
        bool hitItemLimit,
        TimeSpan duration)
    {
        Items = items;
        WasCancelled = wasCancelled;
        HitItemLimit = hitItemLimit;
        Duration = duration;

        var summary = new Dictionary<JunkCategory, CategorySummary>();

        foreach (var group in items.GroupBy(item => item.Category))
        {
            summary[group.Key] = new CategorySummary(group.Count(), group.Sum(item => item.SizeBytes));
        }

        ByCategory = summary;
    }

    public static JunkScanResult Empty { get; } = new([], false, false, TimeSpan.Zero);

    public IReadOnlyList<JunkItem> Items { get; }

    /// <summary>True when the user stopped the scan part way through.</summary>
    public bool WasCancelled { get; }

    /// <summary>True when the scan stopped because it reached <see cref="JunkScanOptions.MaximumItems"/>.</summary>
    public bool HitItemLimit { get; }

    public TimeSpan Duration { get; }

    public int Count => Items.Count;

    public long TotalBytes => Items.Sum(item => item.SizeBytes);

    public bool IsEmpty => Items.Count == 0;

    public IReadOnlyDictionary<JunkCategory, CategorySummary> ByCategory { get; }

    public long BytesFor(JunkCategory category) =>
        ByCategory.TryGetValue(category, out var summary) ? summary.Bytes : 0;

    public int CountFor(JunkCategory category) =>
        ByCategory.TryGetValue(category, out var summary) ? summary.Count : 0;
}

/// <summary>How much one category contributed to a scan.</summary>
public readonly record struct CategorySummary(int Count, long Bytes);
