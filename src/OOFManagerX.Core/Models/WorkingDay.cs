namespace OOFManagerX.Core.Models;

/// <summary>
/// Represents the working schedule for a single day of the week.
/// </summary>
public record WorkingDay
{
    public DayOfWeek DayOfWeek { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public bool IsOffWork { get; init; }

    public WorkingDay()
    {
        StartTime = new TimeOnly(9, 0);
        EndTime = new TimeOnly(17, 0);
    }

    public WorkingDay(DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, bool isOffWork = false)
    {
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        IsOffWork = isOffWork;
    }

    /// <summary>
    /// Determines if the current time falls within working hours for this day.
    /// </summary>
    public bool IsCurrentlyWorking(TimeOnly currentTime)
    {
        if (IsOffWork) return false;
        return currentTime >= StartTime && currentTime <= EndTime;
    }

    /// <summary>
    /// Creates a default working day (9 AM - 5 PM, not off work).
    /// </summary>
    public static WorkingDay CreateDefault(DayOfWeek dayOfWeek)
    {
        var isWeekend = dayOfWeek == System.DayOfWeek.Saturday || dayOfWeek == System.DayOfWeek.Sunday;
        return new WorkingDay(
            dayOfWeek,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            isWeekend
        );
    }
}
