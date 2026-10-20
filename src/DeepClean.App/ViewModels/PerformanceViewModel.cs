using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepClean.Core.Configuration;
using DeepClean.Core.Performance;
using Microsoft.Extensions.Logging;

namespace DeepClean.App.ViewModels;

public sealed record PerformanceModeOption(string Name, PerformanceMode Value, string Description);

public sealed partial class ApprovedApplicationOption : ObservableObject
{
    private readonly Action<ApprovedApplicationOption> _changed;

    public ApprovedApplicationOption(BackgroundApplication application, bool isApproved, Action<ApprovedApplicationOption> changed)
    {
        ProcessName = application.ProcessName;
        DisplayName = application.DisplayName;
        ProcessCount = application.ProcessCount;
        IsBackground = application.IsBackground;
        _isApproved = isApproved;
        _changed = changed;
    }

    public string ProcessName { get; }

    public string DisplayName { get; }

    public int ProcessCount { get; }

    public bool IsBackground { get; }

    [ObservableProperty]
    private bool _isApproved;

    partial void OnIsApprovedChanged(bool value) => _changed(this);
}

/// <summary>Controls reversible power modes and explicit Gaming mode app approvals.</summary>
public sealed partial class PerformanceViewModel : ObservableObject
{
    private readonly IPerformanceModeService _performance;
    private readonly ISettingsStore _settings;
    private readonly ILogger<PerformanceViewModel> _logger;
    private bool _isApplyingSettings;

    public PerformanceViewModel(
        IPerformanceModeService performance,
        ISettingsStore settings,
        ILogger<PerformanceViewModel> logger)
    {
        _performance = performance;
        _settings = settings;
        _logger = logger;

        ModeOptions =
        [
            new PerformanceModeOption("Normal", PerformanceMode.Normal, "Restore original plan"),
            new PerformanceModeOption("Gaming", PerformanceMode.Gaming, "Max performance"),
            new PerformanceModeOption("Power saving", PerformanceMode.PowerSaving, "Reduce power use"),
        ];

        SelectedMode = ModeOptions[0];
        StatusText = "Choose a mode to get started.";
        _ = RefreshApplicationsAsync();
    }

    public IReadOnlyList<PerformanceModeOption> ModeOptions { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredApplications))]
    private IReadOnlyList<ApprovedApplicationOption> _applications = [];

    public IReadOnlyList<ApprovedApplicationOption> FilteredApplications
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return Applications;
            }

            return Applications
                .Where(app => app.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                              app.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredApplications));

    [ObservableProperty]
    private PerformanceModeOption? _selectedMode;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _lastResultText = string.Empty;

    [RelayCommand]
    private async Task RefreshApplicationsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var approvedNames = new HashSet<string>(_settings.Current.GamingModeApprovedApps, StringComparer.OrdinalIgnoreCase);
            var apps = await _performance.GetBackgroundApplicationsAsync();
            _isApplyingSettings = true;
            Applications = apps
                .Select(app => new ApprovedApplicationOption(app, approvedNames.Contains(app.ProcessName), OnApplicationApprovalChanged))
                .ToArray();
            OnPropertyChanged(nameof(Applications));
            StatusText = Applications.Count == 0
                ? "No open desktop apps were found. Refresh this list after opening an app you want to approve."
                : $"{Applications.Count} open desktop app{(Applications.Count == 1 ? "" : "s")} found. Only checked apps receive a close request in Gaming mode.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Listing desktop applications failed");
            StatusText = "The open applications list could not be loaded: " + ex.Message;
        }
        finally
        {
            _isApplyingSettings = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplySelectedModeAsync()
    {
        if (IsBusy || SelectedMode is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var approved = Applications.Where(app => app.IsApproved).Select(app => app.ProcessName).ToArray();
            var result = await _performance.ApplyAsync(SelectedMode.Value, approved);
            LastResultText = result.Message;
            StatusText = result.Succeeded
                ? $"{result.ActiveMode} mode is active. {result.Message}"
                : result.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying performance mode {Mode} failed", SelectedMode.Value);
            LastResultText = "The mode could not be applied: " + ex.Message;
            StatusText = LastResultText;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var app in Applications)
        {
            app.IsApproved = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var app in Applications)
        {
            app.IsApproved = false;
        }
    }

    private void OnApplicationApprovalChanged(ApprovedApplicationOption option)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var approved = new HashSet<string>(_settings.Current.GamingModeApprovedApps, StringComparer.OrdinalIgnoreCase);
        foreach (var app in Applications)
        {
            if (app.IsApproved)
            {
                approved.Add(app.ProcessName);
            }
            else
            {
                approved.Remove(app.ProcessName);
            }
        }

        _ = SaveApprovedAppsAsync(approved.ToList());
    }

    private async Task SaveApprovedAppsAsync(List<string> approvedNames)
    {
        try
        {
            await _settings.UpdateAsync(settings => settings.GamingModeApprovedApps = approvedNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving Gaming mode app approvals failed");
            StatusText = "App approval could not be saved: " + ex.Message;
        }
    }
}
