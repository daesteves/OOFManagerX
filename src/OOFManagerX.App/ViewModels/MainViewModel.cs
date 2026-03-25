using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;

namespace OOFManagerX.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IScheduleService _scheduleService;
    private readonly IOOFService _oofService;
    private readonly IAuthenticationService _authService;
    private readonly ISettingsService _settingsService;
    private readonly IBackgroundOOFService _backgroundService;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private string _userDisplayName = "Not signed in";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isMonitoringEnabled = true;
    [ObservableProperty] private bool _isPrimaryOOFSelected = true;
    [ObservableProperty] private bool _isExtendedOOFSelected;
    [ObservableProperty] private DateTimeOffset _extendedOOFEndDate = DateTimeOffset.Now.AddDays(7);
    [ObservableProperty] private string _internalMessage = string.Empty;
    [ObservableProperty] private string _externalMessage = string.Empty;
    [ObservableProperty] private int _selectedExternalAudienceIndex = 2; // Default: All
    [ObservableProperty] private bool _isExternalExpanded;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private string _updateMessage = string.Empty;
    [ObservableProperty] private string _updateUrl = string.Empty;

    public ObservableCollection<WorkingDayViewModel> WorkingDays { get; } = new();
    public string[] ExternalAudienceOptions { get; } = ["None", "Contacts Only", "All"];

    public bool IsNotSignedIn => !IsSignedIn;
    public string MonitoringStatusText => IsMonitoringEnabled ? "Monitoring" : "Paused";
    public string MonitoringIcon => IsMonitoringEnabled ? "\uE768" : "\uE769";

    public MainViewModel(
        IScheduleService scheduleService,
        IOOFService oofService,
        IAuthenticationService authService,
        ISettingsService settingsService,
        IBackgroundOOFService backgroundService,
        ILogger<MainViewModel> logger)
    {
        _scheduleService = scheduleService;
        _oofService = oofService;
        _authService = authService;
        _settingsService = settingsService;
        _backgroundService = backgroundService;
        _logger = logger;

        _backgroundService.SyncStatusChanged += OnSyncStatusChanged;
    }

    public void OnUpdateAvailable(OOFManagerX.App.Services.UpdateInfo info)
    {
        var label = info.IsPreRelease ? "pre-release" : "release";
        UpdateMessage = $"🚀 v{info.Version} ({label}) is available!";
        UpdateUrl = info.ReleaseUrl;
        IsUpdateAvailable = true;
    }

    [RelayCommand]
    private void DismissUpdate() => IsUpdateAvailable = false;

    partial void OnIsSignedInChanged(bool value) => OnPropertyChanged(nameof(IsNotSignedIn));
    partial void OnIsMonitoringEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitoringStatusText));
        OnPropertyChanged(nameof(MonitoringIcon));
        if (value) _ = StartMonitoringAsync();
        else _ = _backgroundService.StopAsync();
    }

    partial void OnIsPrimaryOOFSelectedChanged(bool value)
    {
        if (value) LoadMessagesFromSchedule();
    }

    partial void OnIsExtendedOOFSelectedChanged(bool value)
    {
        if (value) LoadMessagesFromSchedule();
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Initializing...";

            await _scheduleService.InitializeAsync();
            InitializeWorkingDays();
            LoadMessagesFromSchedule();

            // Load external audience
            var schedule = _scheduleService.GetSchedule();
            SelectedExternalAudienceIndex = (int)schedule.ExternalAudience;
            if (schedule.IsExtendedOOFActive)
            {
                IsExtendedOOFSelected = true;
                IsPrimaryOOFSelected = false;
                if (schedule.ExtendedOOFEndDate.HasValue)
                    ExtendedOOFEndDate = new DateTimeOffset(schedule.ExtendedOOFEndDate.Value);
            }

            // Try silent sign-in
            var authResult = await _authService.TrySilentSignInAsync();
            if (authResult.Success)
            {
                IsSignedIn = true;
                UserDisplayName = authResult.UserPrincipalName ?? "Signed in";
                await StartMonitoringAsync();
            }

            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void InitializeWorkingDays()
    {
        WorkingDays.Clear();
        var schedule = _scheduleService.GetSchedule();
        foreach (var day in schedule.WorkingDays)
        {
            var vm = new WorkingDayViewModel(day);
            vm.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName is nameof(WorkingDayViewModel.StartTimeText)
                    or nameof(WorkingDayViewModel.EndTimeText)
                    or nameof(WorkingDayViewModel.IsOffWork))
                {
                    await _scheduleService.UpdateWorkingDayAsync(vm.ToModel());
                }
            };
            WorkingDays.Add(vm);
        }
    }

    private void LoadMessagesFromSchedule()
    {
        var schedule = _scheduleService.GetSchedule();
        if (IsPrimaryOOFSelected)
        {
            InternalMessage = schedule.PrimaryMessage.InternalMessage;
            ExternalMessage = schedule.PrimaryMessage.ExternalMessage;
        }
        else
        {
            InternalMessage = schedule.ExtendedMessage.InternalMessage;
            ExternalMessage = schedule.ExtendedMessage.ExternalMessage;
        }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Signing in...";

            var result = await _authService.SignInAsync();
            if (result.Success)
            {
                IsSignedIn = true;
                UserDisplayName = result.UserPrincipalName ?? "Signed in";
                await StartMonitoringAsync();
                StatusMessage = "Signed in successfully";
            }
            else
            {
                StatusMessage = $"Sign in failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign in failed");
            StatusMessage = $"Sign in error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _backgroundService.StopAsync();
        await _authService.SignOutAsync();
        IsSignedIn = false;
        UserDisplayName = "Not signed in";
        StatusMessage = "Signed out";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Saving...";

            var currentSchedule = _scheduleService.GetSchedule();

            var primaryMessage = IsPrimaryOOFSelected
                ? new OOFMessage(InternalMessage, ExternalMessage)
                : currentSchedule.PrimaryMessage;

            var extendedMessage = IsExtendedOOFSelected
                ? new OOFMessage(InternalMessage, ExternalMessage)
                : currentSchedule.ExtendedMessage;

            var updatedSchedule = currentSchedule with
            {
                PrimaryMessage = primaryMessage,
                ExtendedMessage = extendedMessage,
                IsExtendedOOFActive = IsExtendedOOFSelected,
                ExtendedOOFEndDate = IsExtendedOOFSelected ? ExtendedOOFEndDate.DateTime : null,
                ExternalAudience = (ExternalAudienceScope)SelectedExternalAudienceIndex
            };

            await _scheduleService.SaveScheduleAsync(updatedSchedule);

            // If signed in, immediately sync
            if (IsSignedIn)
            {
                var oofWindow = _scheduleService.GetOOFScheduleWindow(DateTime.Now);
                if (oofWindow.HasValue)
                {
                    var (start, end, msg) = oofWindow.Value;
                    await _oofService.SetScheduledOOFAsync(
                        msg, start, end, updatedSchedule.ExternalAudience);
                    StatusMessage = $"Saved • OOF scheduled {start:t} – {end:t}";
                }
                else
                {
                    StatusMessage = "Settings saved";
                }
            }
            else
            {
                StatusMessage = "Settings saved (sign in to sync)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task StartMonitoringAsync()
    {
        if (!_backgroundService.IsRunning && IsMonitoringEnabled)
            await _backgroundService.StartAsync();
    }

    private void OnSyncStatusChanged(object? sender, OOFSyncEventArgs e)
    {
        StatusMessage = e.Message;
    }
}

public partial class WorkingDayViewModel : ViewModelBase
{
    public DayOfWeek DayOfWeek { get; }
    public string DayName { get; }

    [ObservableProperty] private string _startTimeText;
    [ObservableProperty] private string _endTimeText;
    [ObservableProperty] private bool _isOffWork;

    public bool IsNotOffWork => !IsOffWork;

    partial void OnIsOffWorkChanged(bool value) => OnPropertyChanged(nameof(IsNotOffWork));

    public WorkingDayViewModel(WorkingDay model)
    {
        DayOfWeek = model.DayOfWeek;
        DayName = model.DayOfWeek.ToString();
        _startTimeText = model.StartTime.ToString("HH:mm");
        _endTimeText = model.EndTime.ToString("HH:mm");
        _isOffWork = model.IsOffWork;
    }

    public WorkingDay ToModel()
    {
        var start = TimeOnly.TryParse(StartTimeText, out var s) ? s : new TimeOnly(9, 0);
        var end = TimeOnly.TryParse(EndTimeText, out var e) ? e : new TimeOnly(17, 0);
        return new WorkingDay(DayOfWeek, start, end, IsOffWork);
    }
}
