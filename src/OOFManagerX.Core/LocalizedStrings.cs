using System.Globalization;

namespace OOFManagerX.Core;

/// <summary>
/// Provides localized strings for the application.
/// Uses the current UI culture for formatting.
/// </summary>
public static class LocalizedStrings
{
    // Status messages
    public static string NotSignedIn => GetString("Not signed in");
    public static string SignInRequired => GetString("Please sign in to sync with Microsoft 365");
    public static string MonitoringPaused => GetString("Monitoring paused");
    public static string MonitoringStarted => GetString("Monitoring started");
    public static string Ready => GetString("Ready");
    public static string Initializing => GetString("Initializing...");
    public static string SigningIn => GetString("Signing in...");
    public static string SigningOut => GetString("Signing out...");
    public static string SignedInSuccessfully => GetString("Signed in successfully");
    public static string SignedOut => GetString("Signed out");
    public static string SavingSettings => GetString("Saving settings...");
    public static string SettingsSavedLocally => GetString("Settings saved locally (sign in to sync)");
    public static string NoOOFPeriodsInSchedule => GetString("Saved (no OOF periods in schedule)");
    
    // OOF Status messages
    public static string OOFEnabled => GetString("OOF enabled");
    public static string OOFDisabled => GetString("OOF disabled");
    public static string OOFCorrectlyEnabled => GetString("OOF active");
    public static string OOFCorrectlyDisabled => GetString("OOF inactive (working hours)");
    public static string SyncFailed => GetString("Sync failed");
    public static string Syncing => GetString("Syncing...");
    
    // Monitoring toggle
    public static string Monitoring => GetString("Monitoring");
    public static string Paused => GetString("Paused");
    public static string MonitoringTooltip => GetString(
        "When enabled, OOFManagerX automatically monitors your schedule and sets Out of Office replies in Microsoft 365. Disable to stop automatic OOF management.");
    
    // Tray menu
    public static string OpenOOFManagerX => GetString("Open OOFManagerX");
    public static string Exit => GetString("Exit");
    
    // Error messages
    public static string SignInFailed => GetString("Sign in failed");
    public static string ErrorSaving => GetString("Error saving");
    public static string Error => GetString("Error");

    /// <summary>
    /// Formats a scheduled OOF status message.
    /// Includes dates when the window spans more than one calendar day.
    /// </summary>
    public static string FormatOOFScheduled(DateTime start, DateTime end)
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var sameDay = start.Date == end.Date;
        var nextDay = end.Date == start.Date.AddDays(1);

        if (start <= now && end > now)
        {
            // Currently active — show end with date if it's far out
            var endFmt = (end.Date - now.Date).TotalDays > 1
                ? end.ToString("h:mm tt MM/dd", culture)
                : end.ToString("ddd h:mm tt", culture);
            return string.Format(culture, GetString("OOF active until {0}"), endFmt);
        }

        // Scheduled for future
        string startFmt, endFmt2;

        if (sameDay)
        {
            // Same day: "OOF scheduled: 5:00 PM → 9:00 PM"
            startFmt = start.ToString("h:mm tt", culture);
            endFmt2 = end.ToString("h:mm tt", culture);
        }
        else if (nextDay)
        {
            // Adjacent days: "OOF scheduled: 5:00 PM → 9:00 AM"
            startFmt = start.ToString("h:mm tt", culture);
            endFmt2 = end.ToString("h:mm tt", culture);
        }
        else
        {
            // Multi-day span: "OOF scheduled: 5:00 PM → 9:00 AM MM/dd"
            startFmt = start.ToString("h:mm tt", culture);
            endFmt2 = end.ToString("h:mm tt MM/dd", culture);
        }

        return string.Format(culture, GetString("OOF scheduled: {0} → {1}"), startFmt, endFmt2);
    }

    /// <summary>
    /// Formats a time for display using current culture.
    /// </summary>
    public static string FormatTime(DateTime time)
    {
        return time.ToString("t", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a date for display using current culture.
    /// </summary>
    public static string FormatDate(DateTime date)
    {
        return date.ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a date and time for display using current culture.
    /// </summary>
    public static string FormatDateTime(DateTime dateTime)
    {
        return dateTime.ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a relative time (e.g., "in 2 hours", "5 minutes ago").
    /// </summary>
    public static string FormatRelativeTime(DateTime dateTime)
    {
        var now = DateTime.Now;
        var diff = dateTime - now;
        var culture = CultureInfo.CurrentCulture;

        if (Math.Abs(diff.TotalMinutes) < 1)
            return GetString("now");

        if (diff.TotalMinutes > 0)
        {
            // Future
            if (diff.TotalMinutes < 60)
                return string.Format(culture, GetString("in {0} min"), (int)diff.TotalMinutes);
            if (diff.TotalHours < 24)
                return string.Format(culture, GetString("in {0} hr"), (int)diff.TotalHours);
            return string.Format(culture, GetString("in {0} days"), (int)diff.TotalDays);
        }
        else
        {
            // Past
            diff = -diff;
            if (diff.TotalMinutes < 60)
                return string.Format(culture, GetString("{0} min ago"), (int)diff.TotalMinutes);
            if (diff.TotalHours < 24)
                return string.Format(culture, GetString("{0} hr ago"), (int)diff.TotalHours);
            return string.Format(culture, GetString("{0} days ago"), (int)diff.TotalDays);
        }
    }

    /// <summary>
    /// Gets the localized day name.
    /// </summary>
    public static string GetDayName(DayOfWeek dayOfWeek)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(dayOfWeek);
    }

    /// <summary>
    /// Gets the abbreviated day name.
    /// </summary>
    public static string GetShortDayName(DayOfWeek dayOfWeek)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek);
    }

    private static string GetString(string key)
    {
        return key;
    }
}
