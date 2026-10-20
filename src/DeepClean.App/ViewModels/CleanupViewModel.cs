using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepClean.Core.Cleaning;
using DeepClean.Core.Configuration;
using DeepClean.Core.Utilities;
using DeepClean.Platform.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;

// Wpf.Ui ships its own MessageBox types; the confirmations on this page are plain
// Windows dialogs, so the framework types are pinned explicitly.
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace DeepClean.App.ViewModels;

/// <summary>One selectable category with the numbers a scan produced for it.</summary>
public sealed partial class CleanupCategoryRow : ObservableObject
{
    public CleanupCategoryRow(JunkCategory category)
    {
        Category = category;
        Name = JunkCategories.DisplayName(category);
        Description = JunkCategories.Description(category);
    }

    public JunkCategory Category { get; }

    public string Name { get; }

    public string Description { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private bool _isAvailable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private long _bytes;

    [ObservableProperty]
    private string _sizeText = "Not scanned yet";

    public bool HasItems => Count > 0 && Bytes > 0;

    /// <summary>Clears the numbers from an earlier scan.</summary>
    public void Reset()
    {
        Count = 0;
        Bytes = 0;
        SizeText = "Not scanned yet";
    }

    public void Apply(int count, long bytes)
    {
        Count = count;
        Bytes = bytes;
        SizeText = count == 0
            ? "Nothing to remove"
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0} · {1}",
                count == 1 ? "1 item" : string.Format(CultureInfo.CurrentCulture, "{0:N0} items", count),
                SizeFormatter.FormatBytes(bytes));
    }
}

/// <summary>One line of the preview list.</summary>
public sealed record JunkRow(string CategoryName, string Path, string SizeText, string AgeText);

/// <summary>
/// Drives the deep clean page. The scan is read only, nothing is removed until the user picks
/// categories and confirms, and every removal is re-checked by <see cref="SafetyGuard"/> first.
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private const int PreviewLimit = 120;
    private const int FailureListLimit = 12;

    private readonly ISystemCleaner _cleaner;
    private readonly IRecycleBinService _recycleBin;
    private readonly ISettingsStore _settings;
    private readonly IElevationService _elevation;
    private readonly ISnackbarService _snackbar;
    private readonly ILogger<CleanupViewModel> _logger;

    /// <summary>Everything the last scan produced, minus whatever has since been removed.</summary>
    private readonly List<JunkItem> _foundItems = [];

    private bool _loadSuspended;

    /// <summary>1 while a Recycle Bin query is in flight; stops overlapping blocking shell calls.</summary>
    private int _recycleBinQueryInFlight;

    public CleanupViewModel(
        ISystemCleaner cleaner,
        IRecycleBinService recycleBin,
        ISettingsStore settings,
        IElevationService elevation,
        ISnackbarService snackbar,
        ILogger<CleanupViewModel> logger)
    {
        _cleaner = cleaner;
        _recycleBin = recycleBin;
        _settings = settings;
        _elevation = elevation;
        _snackbar = snackbar;
        _logger = logger;

        Categories = [.. Enum.GetValues<JunkCategory>().Select(category => new CleanupCategoryRow(category))];

        AgeOptions =
        [
            new Option<int>("Use each rule's own age", 0),
            new Option<int>("At least 1 day old", 1),
            new Option<int>("At least 7 days old", 7),
            new Option<int>("At least 30 days old", 30),
        ];

        IsElevated = _elevation.IsElevated;
        _loadSuspended = true;
        DeletePermanently = _settings.Current.DeletePermanently;
        MinimumAge = Find(AgeOptions, _settings.Current.MinimumJunkAgeDays) ?? AgeOptions[0];
        ExcludedPaths = new ObservableCollection<string>(_settings.Current.ExcludedPaths);
        _loadSuspended = false;

        MarkUnavailableCategories();
        RefreshRecycleBin();
    }

    public ObservableCollection<CleanupCategoryRow> Categories { get; }

    public IReadOnlyList<Option<int>> AgeOptions { get; }

    public ObservableCollection<string> ExcludedPaths { get; }

    public bool IsElevated { get; }

    /// <summary>Shown when some categories are hidden because the process is not elevated.</summary>
    public bool ShowElevationNotice => !IsElevated;

    public string ElevationMessage =>
        "Windows temporary files, installed update packages and the Delivery Optimization cache live in folders that need administrator rights. They are left out of the scan until DeepClean is restarted as administrator.";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private string _statusText = "Run a scan to see what can be removed.";

    [ObservableProperty]
    private string _progressDetail = string.Empty;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 1;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _totalText = "Nothing scanned yet";

    [ObservableProperty]
    private IReadOnlyList<JunkRow> _largestItems = [];

    [ObservableProperty]
    private string _previewCaption = "The biggest items found, so you can see what a scan is actually looking at.";

    [ObservableProperty]
    private string? _selectedExclusion;

    [ObservableProperty]
    private bool _deletePermanently;

    [ObservableProperty]
    private Option<int>? _minimumAge;

    [ObservableProperty]
    private string _recycleBinText = "Checking the Recycle Bin…";

    [ObservableProperty]
    private bool _canEmptyRecycleBin;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private string _summaryTitle = string.Empty;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _summaryLines = [];

    public bool IsBusy => IsScanning || IsCleaning;

    public bool CanScan => !IsBusy;

    public bool CanCancelScan => IsScanning;

    public bool CanClean => !IsBusy && HasResults;

    public bool CanChangeSelection => !IsBusy;

    partial void OnIsScanningChanged(bool value) => RaiseCommandState();

    partial void OnIsCleaningChanged(bool value) => RaiseCommandState();

    partial void OnHasResultsChanged(bool value) => RaiseCommandState();

    /// <summary>
    /// Kicks off a Recycle Bin refresh. The shell query is blocking, so it runs off the UI
    /// thread; a second call while one is still in flight is ignored.
    /// </summary>
    public void RefreshRecycleBin()
    {
        if (Interlocked.CompareExchange(ref _recycleBinQueryInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = RefreshRecycleBinAsync();
    }

    private async Task RefreshRecycleBinAsync()
    {
        try
        {
            var (bytes, count) = await Task
                .Run(() => (_recycleBin.GetSizeBytes(), _recycleBin.GetItemCount()))
                .ConfigureAwait(true);

            if (bytes is null)
            {
                RecycleBinText = "The Recycle Bin could not be read.";
                CanEmptyRecycleBin = false;
                return;
            }

            RecycleBinText = string.Format(
                CultureInfo.CurrentCulture,
                "{0} in {1}",
                SizeFormatter.FormatBytes(bytes.Value),
                count is null or 0
                    ? "no items"
                    : string.Format(CultureInfo.CurrentCulture, "{0:N0} items", count.Value));

            CanEmptyRecycleBin = bytes > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the Recycle Bin failed");
            RecycleBinText = "The Recycle Bin could not be read.";
            CanEmptyRecycleBin = false;
        }
        finally
        {
            Interlocked.Exchange(ref _recycleBinQueryInFlight, 0);
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Categories
            .Where(row => row.IsAvailable && row.IsSelected)
            .Select(row => row.Category)
            .ToArray();

        if (selected.Length == 0)
        {
            Notify("Deep clean", "Select at least one category to scan.", ControlAppearance.Caution);
            return;
        }

        IsScanning = true;
        HasSummary = false;
        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressDetail = "Looking through the folders DeepClean is allowed to clean…";
        StatusText = "Scanning…";

        foreach (var row in Categories)
        {
            row.Reset();
        }

        try
        {
            var options = new JunkScanOptions
            {
                Categories = selected,
                ExcludedPaths = [.. ExcludedPaths],
                MinimumAgeDays = MinimumAge?.Value ?? 0,
                IsElevated = IsElevated,
            };

            var progress = new Progress<JunkScanProgress>(OnScanProgress);
            var result = await _cleaner.ScanAsync(options, progress, cancellationToken).ConfigureAwait(true);

            _foundItems.Clear();
            _foundItems.AddRange(result.Items);

            ApplyFoundItems();

            if (result.HitItemLimit)
            {
                StatusText = "The scan stopped at the item ceiling. Clean what was found and run it again for the rest.";
            }
            else if (result.WasCancelled)
            {
                StatusText = "The scan was cancelled. What was found before you stopped it is listed below.";
            }
            else
            {
                StatusText = _foundItems.Count == 0
                    ? "Nothing to remove in the categories you picked."
                    : "Scan finished " + FormatDuration(result.Duration) + ".";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "The scan was cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The scan failed");
            StatusText = "The scan could not be completed.";
            Notify("Deep clean", "The scan failed: " + ex.Message, ControlAppearance.Danger);
        }
        finally
        {
            IsScanning = false;
            IsProgressVisible = false;
            ProgressDetail = string.Empty;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var selectedCategories = Categories
            .Where(row => row.IsSelected)
            .Select(row => row.Category)
            .ToHashSet();

        var items = _foundItems.Where(item => selectedCategories.Contains(item.Category)).ToArray();

        if (items.Length == 0)
        {
            Notify("Deep clean", "There is nothing selected to clean.", ControlAppearance.Caution);
            return;
        }

        var totalBytes = items.Sum(item => item.SizeBytes);

        if (MessageBox.Show(
                Application.Current?.MainWindow,
                BuildCleanPrompt(items.Length, totalBytes),
                "Deep clean",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        IsCleaning = true;
        HasSummary = false;
        IsProgressVisible = true;
        IsProgressIndeterminate = false;
        ProgressMaximum = items.Length;
        ProgressValue = 0;
        ProgressDetail = string.Empty;
        StatusText = "Cleaning…";

        try
        {
            var options = new CleanupOptions
            {
                PermanentlyDelete = DeletePermanently,
                ExcludedPaths = [.. ExcludedPaths],
            };

            var progress = new Progress<CleanupProgress>(OnCleanProgress);
            var result = await _cleaner
                .CleanAsync(items, options, progress, cancellationToken)
                .ConfigureAwait(true);

            RemoveReclaimed(result);
            ApplyFoundItems();
            ShowSummary(result);
            RefreshRecycleBin();

            StatusText = result.WasCancelled
                ? "The clean was cancelled part way through."
                : "Clean finished " + FormatDuration(result.Duration) + ".";
        }
        catch (OperationCanceledException)
        {
            StatusText = "The clean was cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The clean failed");
            StatusText = "The clean could not be completed.";
            Notify("Deep clean", "The clean failed: " + ex.Message, ControlAppearance.Danger);
        }
        finally
        {
            IsCleaning = false;
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var confirmation =
            "This deletes everything currently in the Recycle Bin for good.\n\n" +
            "That includes items DeepClean did not put there, and once it is done those files " +
            "cannot be recovered. DeepClean cannot undo this.\n\n" +
            "Do you want to empty the Recycle Bin?";

        if (MessageBox.Show(
                Application.Current?.MainWindow,
                confirmation,
                "Empty the Recycle Bin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        IsCleaning = true;
        HasSummary = false;
        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressDetail = string.Empty;
        StatusText = "Emptying the Recycle Bin…";

        try
        {
            var error = await Task.Run(() => _recycleBin.Empty()).ConfigureAwait(true);

            if (error is null)
            {
                StatusText = "The Recycle Bin is now empty.";
                Notify("Recycle Bin", "The Recycle Bin is now empty.", ControlAppearance.Success);
            }
            else
            {
                StatusText = "The Recycle Bin could not be emptied.";
                Notify("Recycle Bin", "The Recycle Bin could not be emptied: " + error, ControlAppearance.Danger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Emptying the Recycle Bin failed");
            StatusText = "The Recycle Bin could not be emptied.";
            Notify("Recycle Bin", "The Recycle Bin could not be emptied: " + ex.Message, ControlAppearance.Danger);
        }
        finally
        {
            IsCleaning = false;
            IsProgressVisible = false;
            ProgressDetail = string.Empty;
            RefreshRecycleBin();
        }
    }

    [RelayCommand]
    private void RefreshRecycleBinInfo() => RefreshRecycleBin();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Categories.Where(row => row.IsAvailable))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Categories)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private void AddExclusion()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder DeepClean must never touch",
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = SafetyGuard.TryNormalize(dialog.FolderName);

        if (path is null)
        {
            Notify("Exclusions", "That folder could not be used as an exclusion.", ControlAppearance.Caution);
            return;
        }

        if (SafetyGuard.IsProtected(path))
        {
            Notify("Exclusions", "That folder is already protected by DeepClean and needs no exclusion.", ControlAppearance.Info);
            return;
        }

        if (ExcludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ExcludedPaths.Add(path);
        PersistExclusions();
        Notify("Exclusions", "DeepClean will leave " + path + " alone from now on.", ControlAppearance.Success);
    }

    [RelayCommand]
    private void RemoveExclusion()
    {
        if (SelectedExclusion is null)
        {
            return;
        }

        ExcludedPaths.Remove(SelectedExclusion);
        PersistExclusions();
    }

    [RelayCommand]
    private void RestartAsAdministrator()
    {
        if (IsElevated)
        {
            return;
        }

        if (_elevation.TryRestartElevated(out var error))
        {
            _logger.LogInformation("Restarting elevated at the request of the user");
            Application.Current?.Shutdown();
            return;
        }

        Notify("Administrator rights", error ?? "The request was cancelled.", ControlAppearance.Caution);
    }

    partial void OnDeletePermanentlyChanged(bool value)
    {
        if (_loadSuspended)
        {
            return;
        }

        Persist(settings => settings.DeletePermanently = value);
    }

    partial void OnMinimumAgeChanged(Option<int>? value)
    {
        if (_loadSuspended || value is null)
        {
            return;
        }

        Persist(settings => settings.MinimumJunkAgeDays = value.Value);
    }

    private void OnScanProgress(JunkScanProgress progress)
    {
        ProgressDetail = progress.CurrentPath;

        StatusText = string.Format(
            CultureInfo.CurrentCulture,
            "Scanning {0} — {1:N0} items, {2} so far…",
            progress.RuleTitle,
            progress.ItemCount,
            SizeFormatter.FormatBytes(progress.TotalBytes));
    }

    private void OnCleanProgress(CleanupProgress progress)
    {
        ProgressMaximum = progress.Total;
        ProgressValue = progress.Completed;
        ProgressDetail = progress.CurrentPath;

        StatusText = string.Format(
            CultureInfo.CurrentCulture,
            "Cleaning {0} of {1} — {2} so far…",
            progress.Completed,
            progress.Total,
            SizeFormatter.FormatBytes(progress.ReclaimedBytes));
    }

    /// <summary>Recomputes the per category numbers, the totals and the preview from <see cref="_foundItems"/>.</summary>
    private void ApplyFoundItems()
    {
        foreach (var row in Categories)
        {
            var mine = _foundItems.Where(item => item.Category == row.Category).ToArray();
            row.IsAvailable = row.IsAvailable && _cleaner
                .Rules.Any(rule => rule.Category == row.Category && (!rule.RequiresElevation || IsElevated));

            row.Apply(mine.Length, mine.Sum(item => item.SizeBytes));

            if (mine.Length == 0 && !row.IsAvailable)
            {
                row.SizeText = IsElevated
                    ? "Nothing to remove"
                    : "Needs administrator rights";
            }
        }

        var totalBytes = _foundItems.Sum(item => item.SizeBytes);

        HasResults = _foundItems.Count > 0;
        TotalText = _foundItems.Count == 0
            ? "Nothing to remove"
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0} across {1}",
                SizeFormatter.FormatBytes(totalBytes),
                _foundItems.Count == 1
                    ? "1 item"
                    : string.Format(CultureInfo.CurrentCulture, "{0:N0} items", _foundItems.Count));

        var ordered = _foundItems
            .OrderByDescending(item => item.SizeBytes)
            .Take(PreviewLimit)
            .Select(item => new JunkRow(
                JunkCategories.DisplayName(item.Category),
                item.Path,
                SizeFormatter.FormatBytes(item.SizeBytes),
                FormatAge(item.LastWriteTimeUtc)))
            .ToArray();

        LargestItems = ordered;

        PreviewCaption = _foundItems.Count <= PreviewLimit
            ? "Everything the scan found, biggest first."
            : string.Format(
                CultureInfo.CurrentCulture,
                "The {0} biggest of {1:N0} items. The rest are cleaned the same way.",
                PreviewLimit,
                _foundItems.Count);
    }

    private void RemoveReclaimed(CleanupResult result)
    {
        var removed = result.Items
            .Where(item => item.IsReclaimed)
            .Select(item => item.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (removed.Count == 0)
        {
            return;
        }

        _foundItems.RemoveAll(item => removed.Contains(item.Path));
    }

    private void ShowSummary(CleanupResult result)
    {
        var lines = new List<string>
        {
            string.Format(
                CultureInfo.CurrentCulture,
                "{0}, {1}",
                result.ReclaimedBytes == 0 ? "Nothing was removed" : SizeFormatter.FormatBytes(result.ReclaimedBytes) + " removed",
                string.Format(CultureInfo.CurrentCulture, "{0:N0} items", result.RecycledCount + result.DeletedCount)),
        };

        if (result.RecycledCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} items are in the Recycle Bin and can still be restored from there.",
                result.RecycledCount));
        }

        if (result.DeletedCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} items were deleted permanently. They cannot be recovered.",
                result.DeletedCount));
        }

        if (result.SkippedCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} items were left alone ({1}). This is normal for files that are open in another program.",
                result.SkippedCount,
                SizeFormatter.FormatBytes(result.SkippedBytes)));
        }

        if (result.FailedCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} items could not be removed.",
                result.FailedCount));
        }

        foreach (var failure in result.Failures.Take(FailureListLimit))
        {
            lines.Add("  • " + failure.Path + " — " + (failure.Message ?? "Windows refused the operation."));
        }

        if (result.FailedCount > FailureListLimit)
        {
            lines.Add("  • and " + (result.FailedCount - FailureListLimit) + " more.");
        }

        SummaryTitle = result.FailedCount > 0 ? "Finished with problems" : "Clean finished";
        SummaryMessage = result.ReclaimedBytes == 0
            ? "Nothing was removed. Files that are open elsewhere are always left in place."
            : "Windows will still hold the restorable copies until the Recycle Bin is emptied.";

        SummaryLines = lines;
        HasSummary = true;
    }

    private string BuildCleanPrompt(int count, long bytes)
    {
        var header = string.Format(
            CultureInfo.CurrentCulture,
            "Clean {0:N0} items ({1})?",
            count,
            SizeFormatter.FormatBytes(bytes));

        var body = DeletePermanently
            ? "\n\nDeepClean will delete these items outright. The Recycle Bin is not used, so they cannot be recovered."
            : "\n\nDeepClean will move these items to the Recycle Bin, so you can still restore them from there. " +
              "Anything too large for the Recycle Bin is removed permanently by Windows.";

        return header + body +
               "\n\nOnly the folders listed on this page are ever touched. Files that are open in another program are skipped.";
    }

    private void PersistExclusions() =>
        Persist(settings => settings.ExcludedPaths = [.. ExcludedPaths]);

    private void Persist(Action<AppSettings> mutate)
    {
        _ = PersistAsync(mutate);
    }

    private async Task PersistAsync(Action<AppSettings> mutate)
    {
        try
        {
            await _settings.UpdateAsync(mutate).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the cleaning settings failed");
            Notify("Deep clean", "The change could not be saved: " + ex.Message, ControlAppearance.Danger);
        }
    }

    /// <summary>
    /// Greys out the categories whose folders do not exist on this machine, or that need rights
    /// the process does not have.
    /// </summary>
    private void MarkUnavailableCategories()
    {
        foreach (var row in Categories)
        {
            var rules = _cleaner.Rules.Where(rule => rule.Category == row.Category).ToArray();

            if (rules.Length == 0)
            {
                row.IsAvailable = false;
                row.IsSelected = false;
                row.SizeText = "Not present on this computer";
                continue;
            }

            if (!IsElevated && rules.All(rule => rule.RequiresElevation))
            {
                row.IsSelected = false;
                row.SizeText = "Needs administrator rights";
            }
        }
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanCancelScan));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CanChangeSelection));
    }

    private static Option<T>? Find<T>(IReadOnlyList<Option<T>> options, T value)
    {
        foreach (var option in options)
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                return option;
            }
        }

        return null;
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1
            ? string.Format(CultureInfo.CurrentCulture, "in {0} ms", duration.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture))
            : string.Format(CultureInfo.CurrentCulture, "in {0:0.0} s", duration.TotalSeconds);

    private static string FormatAge(DateTime lastWriteUtc)
    {
        var age = DateTime.UtcNow - lastWriteUtc;

        if (age < TimeSpan.Zero)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return age.TotalMinutes < 1 ? "just now" : string.Format(CultureInfo.CurrentCulture, "{0:0} min old", age.TotalMinutes);
        }

        if (age.TotalDays < 1)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0:0} h old", age.TotalHours);
        }

        return age.TotalDays < 60
            ? string.Format(CultureInfo.CurrentCulture, "{0:0} days old", age.TotalDays)
            : string.Format(CultureInfo.CurrentCulture, "{0:0} months old", age.TotalDays / 30);
    }

    private void Notify(string title, string message, ControlAppearance appearance)
    {
        if (!_settings.Current.EnableNotifications)
        {
            return;
        }

        try
        {
            _snackbar.Show(title, message, appearance, null!, TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Showing a notification failed");
        }
    }
}
