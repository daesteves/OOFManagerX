using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OOFManagerX.Core;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;
using System.Collections.ObjectModel;

namespace OOFSponder.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IScheduleService _scheduleService;
    private readonly IOOFService _oofService;
    private readonly IAuthenticationService _authService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _isSignedIn;

    public bool IsNotSignedIn => !IsSignedIn;

    [ObservableProperty]
    private string _userDisplayName = LocalizedStrings.NotSignedIn;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = LocalizedStrings.Ready;

    [ObservableProperty]
    private bool _isMonitoringEnabled = true;

    public string MonitoringStatusText => IsMonitoringEnabled ? LocalizedStrings.Monitoring : LocalizedStrings.Paused;
    public string MonitoringIcon => IsMonitoringEnabled ? "\uE768" : "\uE769";  // Play/Pause icons

    [ObservableProperty]
    private bool _isPrimaryOOFSelected = true;

    [ObservableProperty]
    private bool _isExtendedOOFSelected;

    [ObservableProperty]
    private DateTimeOffset _extendedOOFEndDate = DateTimeOffset.Now.AddDays(7);

    [ObservableProperty]
    private string _internalMessage = string.Empty;

    [ObservableProperty]
    private string _externalMessage = string.Empty;

    [ObservableProperty]
    private int _selectedExternalAudienceIndex;

    public ObservableCollection<WorkingDayViewModel> WorkingDays { get; } = new();

    public string[] ExternalAudienceOptions { get; } = ["None", "Contacts Only", "All"];

    public MainViewModel(
        IScheduleService scheduleService,
        IOOFService oofService,
        IAuthenticationService authService,
        ISettingsService settingsService)
    {
        _scheduleService = scheduleService;
        _oofService = oofService;
        _authService = authService;
        _settingsService = settingsService;

        // Don't load settings here - wait for InitializeAsync
    }

    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotSignedIn));
    }

    partial void OnIsMonitoringEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitoringStatusText));
        OnPropertyChanged(nameof(MonitoringIcon));
    }

    private void InitializeWorkingDays()
    {
        var schedule = _scheduleService.GetSchedule();
        WorkingDays.Clear();
        
        foreach (var day in schedule.WorkingDays)
        {
            WorkingDays.Add(new WorkingDayViewModel(day, OnWorkingDayChanged));
        }

        // Load messages
        LoadMessagesFromSchedule(schedule);
        
        SelectedExternalAudienceIndex = (int)schedule.ExternalAudience;
        IsExtendedOOFSelected = schedule.IsExtendedOOFActive;
        IsPrimaryOOFSelected = !schedule.IsExtendedOOFActive;
        
        if (schedule.ExtendedOOFEndDate.HasValue)
        {
            ExtendedOOFEndDate = new DateTimeOffset(schedule.ExtendedOOFEndDate.Value);
        }
    }

    private void LoadMessagesFromSchedule(OOFSchedule schedule)
    {
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

    private async void OnWorkingDayChanged(WorkingDayViewModel vm)
    {
        var workingDay = new WorkingDay(
            vm.DayOfWeek,
            vm.StartTime,
            vm.EndTime,
            vm.IsOffWork);

        await _scheduleService.UpdateWorkingDayAsync(workingDay);
        StatusMessage = $"Updated {vm.DayName}";
    }

    partial void OnIsPrimaryOOFSelectedChanged(bool value)
    {
        if (value)
        {
            IsExtendedOOFSelected = false;
            var schedule = _scheduleService.GetSchedule();
            InternalMessage = schedule.PrimaryMessage.InternalMessage;
            ExternalMessage = schedule.PrimaryMessage.ExternalMessage;
        }
    }

    partial void OnIsExtendedOOFSelectedChanged(bool value)
    {
        if (value)
        {
            IsPrimaryOOFSelected = false;
            var schedule = _scheduleService.GetSchedule();
            InternalMessage = schedule.ExtendedMessage.InternalMessage;
            ExternalMessage = schedule.ExtendedMessage.ExternalMessage;
        }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        IsLoading = true;
        StatusMessage = LocalizedStrings.SigningIn;

        try
        {
            var result = await _authService.SignInAsync();
            if (result.Success)
            {
                IsSignedIn = true;
                UserDisplayName = result.UserPrincipalName ?? LocalizedStrings.SignedInSuccessfully;
                StatusMessage = LocalizedStrings.SignedInSuccessfully;
            }
            else
            {
                StatusMessage = $"{LocalizedStrings.SignInFailed}: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"{LocalizedStrings.Error}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        IsLoading = true;
        StatusMessage = LocalizedStrings.SigningOut;
        
        try
        {
            await _authService.SignOutAsync();
            IsSignedIn = false;
            UserDisplayName = LocalizedStrings.NotSignedIn;
            StatusMessage = LocalizedStrings.SignedOut;
        }
        catch (Exception ex)
        {
            StatusMessage = $"{LocalizedStrings.Error}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        IsLoading = true;
        StatusMessage = LocalizedStrings.SavingSettings;

        try
        {
            var schedule = _scheduleService.GetSchedule();
            
            // Build updated working days from view models
            var workingDays = WorkingDays.Select(vm => new WorkingDay(
                vm.DayOfWeek, vm.StartTime, vm.EndTime, vm.IsOffWork)).ToList();

            // Update messages based on which tab is selected
            OOFMessage primaryMessage, extendedMessage;
            
            if (IsPrimaryOOFSelected)
            {
                primaryMessage = new OOFMessage(InternalMessage, ExternalMessage);
                extendedMessage = schedule.ExtendedMessage;
            }
            else
            {
                primaryMessage = schedule.PrimaryMessage;
                extendedMessage = new OOFMessage(InternalMessage, ExternalMessage);
            }

            var updatedSchedule = schedule with
            {
                WorkingDays = workingDays.AsReadOnly(),
                PrimaryMessage = primaryMessage,
                ExtendedMessage = extendedMessage,
                ExternalAudience = (ExternalAudienceScope)SelectedExternalAudienceIndex,
                IsExtendedOOFActive = IsExtendedOOFSelected,
                ExtendedOOFEndDate = IsExtendedOOFSelected ? ExtendedOOFEndDate.DateTime : null
            };

            await _scheduleService.SaveScheduleAsync(updatedSchedule);

            // If signed in, use scheduled mode to set OOF in advance
            if (IsSignedIn)
            {
                var messageToSend = IsPrimaryOOFSelected ? primaryMessage : extendedMessage;
                var audience = (ExternalAudienceScope)SelectedExternalAudienceIndex;
                
                // Get the next OOF period - this allows us to set it in advance
                var nextPeriod = _scheduleService.GetOOFScheduleWindow(DateTime.Now);
                
                if (nextPeriod.HasValue)
                {
                    var (start, end, _) = nextPeriod.Value;
                    StatusMessage = LocalizedStrings.Syncing;

                    await _oofService.SetScheduledOOFAsync(
                        messageToSend,
                        start,
                        end,
                        audience);

                    // Format user-friendly status using localized formatting
                    StatusMessage = "✓ " + LocalizedStrings.FormatOOFScheduled(start, end);
                }
                else
                {
                    // No OOF period found (e.g., always working) - disable OOF
                    await _oofService.DisableOOFAsync();
                    StatusMessage = "✓ " + LocalizedStrings.NoOOFPeriodsInSchedule;
                }
            }
            else
            {
                StatusMessage = LocalizedStrings.SettingsSavedLocally;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"{LocalizedStrings.ErrorSaving}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Initializing...";
        
        try
        {
            // Load saved schedule from disk first
            await _scheduleService.InitializeAsync();
            InitializeWorkingDays();
            
            // Try silent sign-in
            var result = await _authService.TrySilentSignInAsync();
            if (result.Success)
            {
                IsSignedIn = true;
                UserDisplayName = result.UserPrincipalName ?? LocalizedStrings.SignedInSuccessfully;
                StatusMessage = LocalizedStrings.Ready;
            }
            else
            {
                StatusMessage = LocalizedStrings.SignInRequired;
            }
        }
        catch
        {
            StatusMessage = LocalizedStrings.Ready;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public partial class WorkingDayViewModel : ObservableObject
{
    private readonly Action<WorkingDayViewModel> _onChanged;

    public DayOfWeek DayOfWeek { get; }
    public string DayName => LocalizedStrings.GetDayName(DayOfWeek);
    public string ShortDayName => LocalizedStrings.GetShortDayName(DayOfWeek);

    [ObservableProperty]
    private TimeOnly _startTime;

    [ObservableProperty]
    private TimeOnly _endTime;

    [ObservableProperty]
    private bool _isOffWork;

    public bool IsNotOffWork => !IsOffWork;

    // Text-based time entry (HH:mm format)
    public string StartTimeText
    {
        get => StartTime.ToString("HH:mm");
        set
        {
            if (TryParseTime(value, out var time))
            {
                StartTime = time;
                OnPropertyChanged();
            }
        }
    }

    public string EndTimeText
    {
        get => EndTime.ToString("HH:mm");
        set
        {
            if (TryParseTime(value, out var time))
            {
                EndTime = time;
                OnPropertyChanged();
            }
        }
    }

    public TimeSpan StartTimeSpan
    {
        get => StartTime.ToTimeSpan();
        set
        {
            StartTime = TimeOnly.FromTimeSpan(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(StartTimeText));
        }
    }

    public TimeSpan EndTimeSpan
    {
        get => EndTime.ToTimeSpan();
        set
        {
            EndTime = TimeOnly.FromTimeSpan(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EndTimeText));
        }
    }

    public WorkingDayViewModel(WorkingDay workingDay, Action<WorkingDayViewModel> onChanged)
    {
        DayOfWeek = workingDay.DayOfWeek;
        _startTime = workingDay.StartTime;
        _endTime = workingDay.EndTime;
        _isOffWork = workingDay.IsOffWork;
        _onChanged = onChanged;
    }

    private static bool TryParseTime(string value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Try various formats
        if (TimeOnly.TryParse(value, out time)) return true;
        if (TimeOnly.TryParseExact(value, "HH:mm", null, System.Globalization.DateTimeStyles.None, out time)) return true;
        if (TimeOnly.TryParseExact(value, "H:mm", null, System.Globalization.DateTimeStyles.None, out time)) return true;
        if (TimeOnly.TryParseExact(value, "HHmm", null, System.Globalization.DateTimeStyles.None, out time)) return true;
        
        return false;
    }

    partial void OnStartTimeChanged(TimeOnly value)
    {
        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(StartTimeSpan));
        _onChanged(this);
    }

    partial void OnEndTimeChanged(TimeOnly value)
    {
        OnPropertyChanged(nameof(EndTimeText));
        OnPropertyChanged(nameof(EndTimeSpan));
        _onChanged(this);
    }

    partial void OnIsOffWorkChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotOffWork));
        _onChanged(this);
    }
}
