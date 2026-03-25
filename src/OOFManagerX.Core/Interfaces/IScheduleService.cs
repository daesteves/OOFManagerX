using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Interfaces;

/// <summary>
/// Service for managing OOF schedule and working hours.
/// </summary>
public interface IScheduleService
{
    /// <summary>
    /// Initializes the schedule from storage.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current working schedule.
    /// </summary>
    OOFSchedule GetSchedule();

    /// <summary>
    /// Updates the working schedule.
    /// </summary>
    Task SaveScheduleAsync(OOFSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a single working day in the schedule.
    /// </summary>
    Task UpdateWorkingDayAsync(WorkingDay workingDay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if OOF should currently be active based on schedule.
    /// </summary>
    bool ShouldOOFBeActive(DateTime currentDateTime);

    /// <summary>
    /// Gets the appropriate OOF message (primary or extended) based on current state.
    /// </summary>
    OOFMessage GetCurrentMessage();

    /// <summary>
    /// Calculates the OOF schedule window to set in M365.
    /// During work hours: returns the next OOF period (workEnd → next workStart).
    /// Outside work hours: returns the current OOF period (periodStart → next workStart).
    /// </summary>
    (DateTime Start, DateTime End, OOFMessage Message)? GetOOFScheduleWindow(DateTime currentDateTime);
}
