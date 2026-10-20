using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepClean.Core.Cleaning;
using DeepClean.Core.Configuration;
using DeepClean.Core.Duplicates;
using DeepClean.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;

// Wpf.Ui ships its own MessageBox types; the confirmations on this page are plain
// Windows dialogs, so the framework types are pinned explicitly.
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace DeepClean.App.ViewModels;

/// <summary>One copy of a file that shares its contents with the others in the group.</summary>
public sealed partial class DuplicateCandidateViewModel : ObservableObject
{
    private readonly Action<DuplicateCandidateViewModel> _selectionChanged;

    public DuplicateCandidateViewModel(
        DuplicateCandidate candidate,
        Action<DuplicateCandidateViewModel> selectionChanged)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        _selectionChanged = selectionChanged;

        // Qualified: this type has its own Path property, which would otherwise shadow the type.
        FileName = System.IO.Path.GetFileName(candidate.Path);

        var folder = System.IO.Path.GetDirectoryName(candidate.Path) ?? candidate.Path;
        FolderPath = folder;

        SizeText = SizeFormatter.FormatBytes(candidate.SizeBytes);
        AgeText = FormatAge(candidate.LastWriteTimeUtc);

        // A copy taken from one of the user's own folders can only ever be recycled, so it is
        // labelled here rather than surprising anyone at the confirmation.
        RecycleOnly = SafetyGuard.CheckDuplicate(candidate.Path, candidate.RootPath).MustRecycle;
    }

    public DuplicateCandidate Candidate { get; }

    public string FileName { get; }

    public string FolderPath { get; }

    public string Path => Candidate.Path;

    public string SizeText { get; }

    public string AgeText { get; }

    /// <summary>True when this copy lives in one of the user's own folders and may only be recycled.</summary>
    public bool RecycleOnly { get; }

    public string LocationNote =>
        "In your personal folder — this copy is put in the Recycle Bin, never deleted outright.";

    /// <summary>The copy suggested as the one to keep: the oldest, which is usually the original.</summary>
    [ObservableProperty]
    private bool _isSuggestedKeep;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged(this);

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
}

/// <summary>One set of files that hold exactly the same bytes.</summary>
public sealed partial class DuplicateGroupViewModel : ObservableObject
{
    private readonly Action<DuplicateGroupViewModel> _selectionChanged;

    public DuplicateGroupViewModel(
        DuplicateFileGroup group,
        Action<DuplicateGroupViewModel> selectionChanged)
    {
        ArgumentNullException.ThrowIfNull(group);

        _selectionChanged = selectionChanged;
        Hash = group.Hash;
        SizeBytes = group.SizeBytes;

        var candidates = group.Candidates
            .Select(candidate => new DuplicateCandidateViewModel(candidate, OnCandidateSelectionChanged))
            .ToArray();

        // The oldest copy is suggested as the one to keep, which is usually the original.
        var keep = candidates
            .OrderBy(candidate => candidate.Candidate.LastWriteTimeUtc)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (keep is not null)
        {
            keep.IsSuggestedKeep = true;
        }

        Candidates = [.. candidates];

        UpdateSelectionText();
    }

    public string Hash { get; }

    public long SizeBytes { get; }

    public ObservableCollection<DuplicateCandidateViewModel> Candidates { get; }

    /// <summary>Bytes that come back if all but one copy of this group go.</summary>
    public long WastedBytes => SizeBytes * (Candidates.Count - 1);

    public string HeaderText => string.Format(
        CultureInfo.CurrentCulture,
        "{0} · {1} each · {2} can come back",
        Candidates.Count == 1 ? "1 copy" : string.Format(CultureInfo.CurrentCulture, "{0:N0} copies", Candidates.Count),
        SizeFormatter.FormatBytes(SizeBytes),
        SizeFormatter.FormatBytes(WastedBytes));

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    public int SelectedCount => Candidates.Count(candidate => candidate.IsSelected);

    public IReadOnlyList<DuplicateCandidateViewModel> SelectedCandidates =>
        [.. Candidates.Where(candidate => candidate.IsSelected)];

    /// <summary>Picks every copy except the one suggested to keep.</summary>
    public void SelectAllButSuggested()
    {
        foreach (var candidate in Candidates)
        {
            candidate.IsSelected = !candidate.IsSuggestedKeep;
        }
    }

    public void ClearSelection()
    {
        foreach (var candidate in Candidates)
        {
            candidate.IsSelected = false;
        }
    }

    private void OnCandidateSelectionChanged(DuplicateCandidateViewModel candidate)
    {
        UpdateSelectionText();
        _selectionChanged(this);
    }

    private void UpdateSelectionText() =>
        SelectionText = SelectedCount == 0
            ? "Nothing selected"
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0} of {1} selected",
                SelectedCount,
                Candidates.Count);
}

/// <summary>
/// Drives the duplicate finder. Nothing is picked for the user, the removal is confirmed before it
/// happens, and every copy is re-checked by <see cref="SafetyGuard"/> first. Copies in the user's own
/// folders are always moved to the Recycle Bin rather than deleted outright.
/// </summary>
public sealed partial class DuplicatesViewModel : ObservableObject
{
    private const int FailureListLimit = 12;

    private readonly IDuplicateFinder _finder;
    private readonly IDuplicateCleaner _cleaner;
    private readonly ISettingsStore _settings;
    private readonly ISnackbarService _snackbar;
    private readonly ILogger<DuplicatesViewModel> _logger;

    private bool _loadSuspended;

    public DuplicatesViewModel(
        IDuplicateFinder finder,
        IDuplicateCleaner cleaner,
        ISettingsStore settings,
        ISnackbarService snackbar,
        ILogger<DuplicatesViewModel> logger)
    {
        _finder = finder;
        _cleaner = cleaner;
        _settings = settings;
        _snackbar = snackbar;
        _logger = logger;

        SizeOptions =
        [
            new Option<long>("Small files too (100 KB and up)", 100 * 1024),
            new Option<long>("At least 1 MB", 1024 * 1024),
            new Option<long>("At least 10 MB", 10 * 1024 * 1024),
            new Option<long>("At least 100 MB", 100 * 1024 * 1024),
        ];

        _loadSuspended = true;
        DeletePermanently = _settings.Current.DeletePermanently;
        MinimumSize = SizeOptions[1];
        ExcludedPaths = new ObservableCollection<string>(_settings.Current.ExcludedPaths);
        _loadSuspended = false;

        AddPersonalFolders();
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public ObservableCollection<string> Roots { get; } = [];

    public ObservableCollection<string> ExcludedPaths { get; }

    public IReadOnlyList<Option<long>> SizeOptions { get; }

    [ObservableProperty]
    private string? _selectedRoot;

    [ObservableProperty]
    private string? _selectedExclusion;

    [ObservableProperty]
    private Option<long>? _minimumSize;

    [ObservableProperty]
    private bool _deletePermanently;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private string _statusText = "Add the folders you want searched, then run a scan.";

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
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _resultsCaption = string.Empty;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectedSizeText = "Nothing selected";

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private string _summaryTitle = string.Empty;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _summaryLines = [];

    public bool IsBusy => IsScanning || IsDeleting;

    public bool CanScan => !IsBusy && Roots.Count > 0;

    public bool CanCancelScan => IsScanning;

    public bool CanDelete => !IsBusy && HasResults && SelectedCount > 0;

    public bool CanChangeSelection => !IsBusy;

    partial void OnIsScanningChanged(bool value) => RaiseCommandState();

    partial void OnIsDeletingChanged(bool value) => RaiseCommandState();

    partial void OnHasResultsChanged(bool value) => RaiseCommandState();

    partial void OnSelectedCountChanged(int value) => RaiseCommandState();

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to search for duplicates",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var folder in dialog.FolderNames)
        {
            AddRoot(folder);
        }
    }

    [RelayCommand]
    private void AddPersonalFolders()
    {
        var added = SafetyGuard.PersonalFolders.Count(AddRoot);

        if (added == 0)
        {
            Notify("Duplicates", "Your personal folders are already on the list.", ControlAppearance.Info);
        }
    }

    [RelayCommand]
    private void RemoveRoot()
    {
        if (SelectedRoot is null)
        {
            return;
        }

        Roots.Remove(SelectedRoot);
        SelectedRoot = null;
        RaiseCommandState();
    }

    [RelayCommand]
    private void ClearRoots()
    {
        Roots.Clear();
        SelectedRoot = null;
        RaiseCommandState();
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (Roots.Count == 0)
        {
            Notify("Duplicates", "Add at least one folder to search.", ControlAppearance.Caution);
            return;
        }

        foreach (var group in Groups)
        {
            group.ClearSelection();
        }

        Groups.Clear();
        SelectedCount = 0;
        SelectedSizeText = "Nothing selected";
        HasSummary = false;
        IsScanning = true;
        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressDetail = string.Empty;
        StatusText = "Looking for files with matching contents…";

        try
        {
            var options = new DuplicateScanOptions
            {
                Roots = [.. Roots],
                ExcludedPaths = [.. ExcludedPaths],
                MinimumSizeBytes = MinimumSize?.Value ?? 0,
            };

            var progress = new Progress<DuplicateScanProgress>(OnScanProgress);
            var result = await _finder.FindAsync(options, progress, cancellationToken).ConfigureAwait(true);

            BuildGroups(result);

            if (result.HitFileLimit)
            {
                StatusText = "The scan stopped at the file ceiling. Remove what was found and run it again for the rest.";
            }
            else if (result.WasCancelled)
            {
                StatusText = "The scan was cancelled. What was found before you stopped it is listed below.";
            }
            else
            {
                StatusText = result.IsEmpty
                    ? "No duplicates found in the folders you picked."
                    : "Scan finished " + FormatDuration(result.Duration) + ".";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "The scan was cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The duplicate scan failed");
            StatusText = "The scan could not be completed.";
            Notify("Duplicates", "The scan failed: " + ex.Message, ControlAppearance.Danger);
        }
        finally
        {
            IsScanning = false;
            IsProgressVisible = false;
            ProgressDetail = string.Empty;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Groups.SelectMany(group => group.SelectedCandidates).ToArray();

        if (selected.Length == 0)
        {
            Notify("Duplicates", "There is nothing selected to remove.", ControlAppearance.Caution);
            return;
        }

        var bytes = selected.Sum(candidate => candidate.Candidate.SizeBytes);
        var recycledOnly = selected.Count(candidate => candidate.RecycleOnly);

        if (MessageBox.Show(
                BuildDeletePrompt(selected.Length, bytes, recycledOnly),
                "Remove duplicate copies",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        IsDeleting = true;
        HasSummary = false;
        IsProgressVisible = true;
        IsProgressIndeterminate = false;
        ProgressMaximum = selected.Length;
        ProgressValue = 0;
        ProgressDetail = string.Empty;
        StatusText = "Removing copies…";

        try
        {
            var options = new CleanupOptions
            {
                PermanentlyDelete = DeletePermanently,
                ExcludedPaths = [.. ExcludedPaths],
            };

            var candidates = selected.Select(candidate => candidate.Candidate).ToArray();

            var progress = new Progress<CleanupProgress>(OnDeleteProgress);
            var result = await _cleaner
                .DeleteAsync(candidates, options, progress, cancellationToken)
                .ConfigureAwait(true);

            RemoveReclaimed(result);
            ApplyGroups();
            ShowSummary(result);

            StatusText = result.WasCancelled
                ? "The removal was cancelled part way through."
                : "Removal finished " + FormatDuration(result.Duration) + ".";
        }
        catch (OperationCanceledException)
        {
            StatusText = "The removal was cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Removing the duplicate copies failed");
            StatusText = "The removal could not be completed.";
            Notify("Duplicates", "The removal failed: " + ex.Message, ControlAppearance.Danger);
        }
        finally
        {
            IsDeleting = false;
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    private void SelectAllCopies()
    {
        if (!CanChangeSelection)
        {
            return;
        }

        foreach (var group in Groups)
        {
            group.SelectAllButSuggested();
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var group in Groups)
        {
            group.ClearSelection();
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = false;
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

        if (SafetyGuard.DescribeSystemProtection(path) is not null || SafetyGuard.DescribePersonalFolder(path) is not null)
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

    /// <summary>Adds a folder as a scan root, unless it is already there or DeepClean must not search it.</summary>
    private bool AddRoot(string? folder)
    {
        var path = SafetyGuard.TryNormalize(folder);

        if (path is null)
        {
            return false;
        }

        if (SafetyGuard.DescribeSystemProtection(path) is not null)
        {
            Notify("Duplicates", path + " is a Windows location DeepClean never searches.", ControlAppearance.Caution);
            return false;
        }

        if (Roots.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        Roots.Add(path);
        RaiseCommandState();
        return true;
    }

    partial void OnDeletePermanentlyChanged(bool value)
    {
        if (_loadSuspended)
        {
            return;
        }

        Persist(settings => settings.DeletePermanently = value);
    }

    private void OnScanProgress(DuplicateScanProgress progress)
    {
        ProgressDetail = progress.CurrentPath;

        StatusText = string.Format(
            CultureInfo.CurrentCulture,
            "{0} — {1:N0} files read, {2} so far…",
            progress.Phase,
            progress.FilesHashed,
            SizeFormatter.FormatBytes(progress.BytesRead));
    }

    private void OnDeleteProgress(CleanupProgress progress)
    {
        ProgressMaximum = progress.Total;
        ProgressValue = progress.Completed;
        ProgressDetail = progress.CurrentPath;

        StatusText = string.Format(
            CultureInfo.CurrentCulture,
            "Removing copy {0} of {1} — {2} so far…",
            progress.Completed,
            progress.Total,
            SizeFormatter.FormatBytes(progress.ReclaimedBytes));
    }

    private void BuildGroups(DuplicateScanResult result)
    {
        Groups.Clear();

        foreach (var group in result.Groups.OrderByDescending(group => group.WastedBytes))
        {
            Groups.Add(new DuplicateGroupViewModel(group, OnGroupSelectionChanged));
        }

        if (Groups.Count > 0)
        {
            Groups[0].IsExpanded = true;
        }

        ApplyGroups();
    }

    private void ApplyGroups()
    {
        var wasted = Groups.Sum(group => group.WastedBytes);
        var copies = Groups.Sum(group => group.Candidates.Count);

        HasResults = Groups.Count > 0;
        TotalText = HasResults
            ? SizeFormatter.FormatBytes(wasted) + " can come back"
            : "No duplicates found";
        ResultsCaption = HasResults
            ? string.Format(
                CultureInfo.CurrentCulture,
                "{0} across {1}. Nothing is picked until you choose, and the oldest copy of each set is suggested to keep.",
                copies == 1 ? "1 file" : string.Format(CultureInfo.CurrentCulture, "{0:N0} files", copies),
                Groups.Count == 1
                    ? "1 set of copies"
                    : string.Format(CultureInfo.CurrentCulture, "{0:N0} sets of copies", Groups.Count))
            : string.Empty;

        if (Groups.Count == 0)
        {
            SelectedCount = 0;
            SelectedSizeText = "Nothing selected";
        }
    }

    private void OnGroupSelectionChanged(DuplicateGroupViewModel group) => RecomputeSelection();

    /// <summary>Works out what is ticked across every set, for the running total and the button state.</summary>
    private void RecomputeSelection()
    {
        SelectedCount = Groups.Sum(item => item.SelectedCount);
        var bytes = Groups.Sum(item => item.SelectedCandidates.Sum(candidate => candidate.Candidate.SizeBytes));
        SelectedSizeText = SelectedCount == 0
            ? "Nothing selected"
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0} selected ({1})",
                SelectedCount,
                SizeFormatter.FormatBytes(bytes));
    }

    /// <summary>Drops the copies that were really removed and any set that is no longer a duplicate.</summary>
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

        foreach (var group in Groups.ToArray())
        {
            foreach (var candidate in group.Candidates.Where(candidate => removed.Contains(candidate.Path)).ToArray())
            {
                group.Candidates.Remove(candidate);
            }

            if (group.Candidates.Count < 2)
            {
                Groups.Remove(group);
            }
            else if (group.Candidates.All(candidate => candidate.IsSuggestedKeep == false))
            {
                group.Candidates[0].IsSuggestedKeep = true;
            }
        }

        RecomputeSelection();
    }

    private void ShowSummary(CleanupResult result)
    {
        var lines = new List<string>
        {
            string.Format(
                CultureInfo.CurrentCulture,
                "{0}, {1}",
                result.ReclaimedBytes == 0 ? "Nothing was removed" : SizeFormatter.FormatBytes(result.ReclaimedBytes) + " came back",
                string.Format(CultureInfo.CurrentCulture, "{0:N0} copies", result.RecycledCount + result.DeletedCount)),
        };

        if (result.RecycledCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} copies are in the Recycle Bin, including everything taken from your personal folders, and can still be restored from there.",
                result.RecycledCount));
        }

        if (result.DeletedCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} copies were deleted permanently. They cannot be recovered.",
                result.DeletedCount));
        }

        if (result.SkippedCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} copies were left alone ({1}). This is normal for files that are open in another program.",
                result.SkippedCount,
                SizeFormatter.FormatBytes(result.SkippedBytes)));
        }

        if (result.FailedCount > 0)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} copies could not be removed.",
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

        SummaryTitle = result.FailedCount > 0 ? "Finished with problems" : "Removal finished";
        SummaryMessage = result.ReclaimedBytes == 0
            ? "Nothing was removed. A copy that is open elsewhere is always left in place."
            : "Windows will still hold the restorable copies until the Recycle Bin is emptied.";

        SummaryLines = lines;
        HasSummary = true;
    }

    private string BuildDeletePrompt(int count, long bytes, int recycledOnly)
    {
        var header = string.Format(
            CultureInfo.CurrentCulture,
            "Remove {0:N0} duplicate copies ({1})?",
            count,
            SizeFormatter.FormatBytes(bytes));

        var body = DeletePermanently
            ? "\n\nDeepClean will delete these copies outright. The Recycle Bin is not used, so they cannot be recovered."
            : "\n\nDeepClean will move these copies to the Recycle Bin, so you can still restore them from there.";

        if (recycledOnly > 0)
        {
            body += string.Format(
                CultureInfo.CurrentCulture,
                "\n\n{0:N0} of them live in your personal folders, so those go to the Recycle Bin either way. The copy suggested to keep in each set is never selected for you.",
                recycledOnly);
        }

        return header + body +
               "\n\nCheck the list first: only the copies you ticked are touched, and one copy of each set should stay so nothing is lost.";
    }

    private void PersistExclusions() =>
        Persist(settings => settings.ExcludedPaths = [.. ExcludedPaths]);

    private void Persist(Action<AppSettings> mutate) => _ = PersistAsync(mutate);

    private async Task PersistAsync(Action<AppSettings> mutate)
    {
        try
        {
            await _settings.UpdateAsync(mutate).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the duplicate settings failed");
            Notify("Duplicates", "The change could not be saved: " + ex.Message, ControlAppearance.Danger);
        }
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanCancelScan));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanChangeSelection));
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1
            ? string.Format(CultureInfo.CurrentCulture, "in {0} ms", duration.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture))
            : string.Format(CultureInfo.CurrentCulture, "in {0:0.0} s", duration.TotalSeconds);

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
