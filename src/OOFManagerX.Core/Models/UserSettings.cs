namespace OOFManagerX.Core.Models;

/// <summary>
/// Application settings and user preferences.
/// </summary>
public record UserSettings
{
    public bool StartMinimized { get; init; }
    public bool StartWithWindows { get; init; }
    public bool MonitoringEnabled { get; init; } = true;

    /// <summary>
    /// Auto-import working hours from Outlook/Exchange. When enabled, manual schedule editing is disabled.
    /// </summary>
    public bool SyncWorkingHoursFromOutlook { get; init; }

    /// <summary>
    /// Schedule layout: true = horizontal (compact), false = vertical (classic).
    /// </summary>
    public bool HorizontalScheduleLayout { get; init; } = true;

    public string? UserPrincipalName { get; init; }
}
