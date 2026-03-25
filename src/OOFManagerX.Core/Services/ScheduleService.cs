using Microsoft.Extensions.Logging;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Services;

/// <summary>
/// Service for managing OOF schedule logic and determining when OOF should be active.
/// </summary>
public class ScheduleService : IScheduleService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ScheduleService> _logger;
    
    private OOFSchedule _currentSchedule;

    public ScheduleService(ISettingsService settingsService, ILogger<ScheduleService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _currentSchedule = new OOFSchedule();
    }

    public OOFSchedule GetSchedule() => _currentSchedule;

    public async Task SaveScheduleAsync(OOFSchedule schedule, CancellationToken cancellationToken = default)
    {
        _currentSchedule = schedule;
        await _settingsService.SaveScheduleAsync(schedule, cancellationToken);
        _logger.LogInformation("Schedule saved");
    }

    public async Task UpdateWorkingDayAsync(WorkingDay workingDay, CancellationToken cancellationToken = default)
    {
        var workingDays = _currentSchedule.WorkingDays.ToList();
        var index = workingDays.FindIndex(d => d.DayOfWeek == workingDay.DayOfWeek);
        
        if (index >= 0)
        {
            workingDays[index] = workingDay;
        }
        else
        {
            workingDays.Add(workingDay);
        }

        _currentSchedule = _currentSchedule with { WorkingDays = workingDays.AsReadOnly() };
        await _settingsService.SaveScheduleAsync(_currentSchedule, cancellationToken);
        
        _logger.LogInformation("Updated working day for {Day}", workingDay.DayOfWeek);
    }

    public bool ShouldOOFBeActive(DateTime currentDateTime)
    {
        _logger.LogDebug("Checking if OOF should be active at {DateTime}", currentDateTime);

        // If extended OOF is active, check if it should still be on
        if (_currentSchedule.IsExtendedOOFActive)
        {
            if (_currentSchedule.ExtendedOOFEndDate.HasValue && 
                currentDateTime >= _currentSchedule.ExtendedOOFEndDate.Value)
            {
                _logger.LogInformation("Extended OOF period has ended");
                // Extended OOF has ended, fall through to normal schedule check
            }
            else
            {
                _logger.LogDebug("Extended OOF is active");
                return true;
            }
        }

        // Check normal working schedule
        var currentDay = _currentSchedule.WorkingDays
            .FirstOrDefault(d => d.DayOfWeek == currentDateTime.DayOfWeek);

        if (currentDay == null)
        {
            _logger.LogWarning("No schedule found for {Day}, defaulting to OOF active", currentDateTime.DayOfWeek);
            return true;
        }

        // If it's a day off, OOF should be active
        if (currentDay.IsOffWork)
        {
            _logger.LogDebug("{Day} is marked as off work, OOF active", currentDateTime.DayOfWeek);
            return true;
        }

        // Check if current time is outside working hours
        var currentTime = TimeOnly.FromDateTime(currentDateTime);
        var isWorkingHours = currentDay.IsCurrentlyWorking(currentTime);
        
        _logger.LogDebug(
            "Current time {Time} is {Status} working hours ({Start}-{End})",
            currentTime,
            isWorkingHours ? "within" : "outside",
            currentDay.StartTime,
            currentDay.EndTime);

        return !isWorkingHours;
    }

    public OOFMessage GetCurrentMessage()
    {
        if (_currentSchedule.IsExtendedOOFActive && 
            (!_currentSchedule.ExtendedOOFEndDate.HasValue || 
             DateTime.Now < _currentSchedule.ExtendedOOFEndDate.Value))
        {
            return _currentSchedule.ExtendedMessage;
        }

        return _currentSchedule.PrimaryMessage;
    }

    /// <summary>
    /// Calculates the OOF schedule window to set in M365.
    /// During work hours: returns (workEnd today, next workStart) — future OOF.
    /// Outside work hours: returns (OOF period start, next workStart) — active OOF.
    /// </summary>
    public (DateTime Start, DateTime End, OOFMessage Message)? GetOOFScheduleWindow(DateTime currentDateTime)
    {
        _logger.LogDebug("Calculating OOF schedule window at {DateTime}", currentDateTime);

        // Extended OOF (vacation mode) takes priority
        if (_currentSchedule.IsExtendedOOFActive)
        {
            if (_currentSchedule.ExtendedOOFEndDate.HasValue)
            {
                var endTime = GetWorkStartTimeForDate(_currentSchedule.ExtendedOOFEndDate.Value.Date);
                if (endTime > currentDateTime)
                {
                    // Use start of today or the current OOF period start, whichever is earlier
                    var start = FindCurrentOOFPeriodStart(currentDateTime);
                    _logger.LogInformation("Extended OOF window: {Start} to {End}", start, endTime);
                    return (start, endTime, _currentSchedule.ExtendedMessage);
                }
            }
            else
            {
                // No end date — indefinite extended OOF, cap at 14 days
                var start = FindCurrentOOFPeriodStart(currentDateTime);
                var end = currentDateTime.Date.AddDays(14);
                _logger.LogInformation("Extended OOF (indefinite) window: {Start} to {End}", start, end);
                return (start, end, _currentSchedule.ExtendedMessage);
            }
        }

        var shouldBeActive = ShouldOOFBeActive(currentDateTime);

        if (!shouldBeActive)
        {
            // During working hours — schedule the next OOF period
            var todaySchedule = _currentSchedule.WorkingDays
                .FirstOrDefault(d => d.DayOfWeek == currentDateTime.DayOfWeek);

            if (todaySchedule == null || todaySchedule.IsOffWork)
            {
                _logger.LogWarning("Expected working day but schedule says otherwise");
                return null;
            }

            var workEnd = currentDateTime.Date.Add(todaySchedule.EndTime.ToTimeSpan());
            var nextWorkStart = FindNextWorkStart(workEnd);

            _logger.LogInformation("Work hours — scheduling next OOF: {Start} to {End}", workEnd, nextWorkStart);
            return (workEnd, nextWorkStart, GetCurrentMessage());
        }
        else
        {
            // Outside working hours — OOF should be active now
            var periodStart = FindCurrentOOFPeriodStart(currentDateTime);
            var periodEnd = FindNextWorkStart(currentDateTime);

            _logger.LogInformation("Off hours — OOF window: {Start} to {End}", periodStart, periodEnd);
            return (periodStart, periodEnd, GetCurrentMessage());
        }
    }

    /// <summary>
    /// Finds the next time work starts after the given time.
    /// Scans up to 14 days ahead.
    /// </summary>
    private DateTime FindNextWorkStart(DateTime after)
    {
        for (int dayOffset = 0; dayOffset < 14; dayOffset++)
        {
            var checkDate = after.Date.AddDays(dayOffset);
            var daySchedule = _currentSchedule.WorkingDays
                .FirstOrDefault(d => d.DayOfWeek == checkDate.DayOfWeek);

            if (daySchedule != null && !daySchedule.IsOffWork)
            {
                var workStart = checkDate.Add(daySchedule.StartTime.ToTimeSpan());
                if (workStart > after)
                {
                    _logger.LogDebug("Next work start: {Day} at {Time}", checkDate.DayOfWeek, daySchedule.StartTime);
                    return workStart;
                }
            }
        }

        _logger.LogWarning("No working day found in next 14 days after {After}", after);
        return after.AddDays(14);
    }

    /// <summary>
    /// Finds when the current OOF period started by looking backwards
    /// for the most recent work-end time before now.
    /// </summary>
    private DateTime FindCurrentOOFPeriodStart(DateTime now)
    {
        for (int dayOffset = 0; dayOffset >= -7; dayOffset--)
        {
            var checkDate = now.Date.AddDays(dayOffset);
            var daySchedule = _currentSchedule.WorkingDays
                .FirstOrDefault(d => d.DayOfWeek == checkDate.DayOfWeek);

            if (daySchedule == null || daySchedule.IsOffWork)
                continue;

            var workEnd = checkDate.Add(daySchedule.EndTime.ToTimeSpan());
            if (workEnd <= now)
            {
                _logger.LogDebug("OOF period started at {WorkEnd} ({Day} end of work)", workEnd, checkDate.DayOfWeek);
                return workEnd;
            }
        }

        // Fallback: use start of today
        _logger.LogDebug("Could not find OOF period start, using start of today");
        return now.Date;
    }

    /// <summary>
    /// Gets the work start time for a given date based on the schedule.
    /// If the date is not a working day, finds the next working day's start time.
    /// </summary>
    private DateTime GetWorkStartTimeForDate(DateTime date)
    {
        // Look up to 7 days ahead to find a working day
        for (int dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            var checkDate = date.AddDays(dayOffset);
            var daySchedule = _currentSchedule.WorkingDays
                .FirstOrDefault(d => d.DayOfWeek == checkDate.DayOfWeek);

            if (daySchedule != null && !daySchedule.IsOffWork)
            {
                var workStart = checkDate.Add(daySchedule.StartTime.ToTimeSpan());
                _logger.LogDebug("Work starts on {Date} at {Time}", checkDate.DayOfWeek, daySchedule.StartTime);
                return workStart;
            }
        }

        // Fallback: if no working day found in next 7 days, use midnight of the original date
        _logger.LogWarning("No working day found within 7 days of {Date}, using midnight", date);
        return date;
    }

    /// <summary>
    /// Initializes the schedule from storage.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _currentSchedule = await _settingsService.LoadScheduleAsync(cancellationToken);
        _logger.LogInformation("Schedule initialized with {Count} working days", _currentSchedule.WorkingDays.Count);
    }
}
