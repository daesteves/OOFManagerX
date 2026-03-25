namespace OOFManagerX.Core.Models;

/// <summary>
/// Application settings and user preferences.
/// </summary>
public record UserSettings
{
    /// <summary>
    /// Start the application minimized to system tray.
    /// </summary>
    public bool StartMinimized { get; init; }

    /// <summary>
    /// Start the application automatically on Windows login.
    /// </summary>
    public bool StartWithWindows { get; init; }

    /// <summary>
    /// Enable or disable OOF monitoring and synchronization.
    /// </summary>
    public bool MonitoringEnabled { get; init; } = true;

    /// <summary>
    /// The currently logged-in user's email/UPN.
    /// </summary>
    public string? UserPrincipalName { get; init; }
}
