using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Interfaces;

/// <summary>
/// Service for communicating with Microsoft 365 Graph API for OOF settings.
/// </summary>
public interface IOOFService
{
    /// <summary>
    /// Gets the current OOF settings from Microsoft 365.
    /// </summary>
    Task<OOFStatus> GetCurrentOOFStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the OOF status and message in Microsoft 365 (always enabled or disabled).
    /// </summary>
    Task SetOOFAsync(OOFMessage message, bool isEnabled, ExternalAudienceScope externalAudience, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a scheduled OOF period in Microsoft 365.
    /// The OOF will automatically activate at startTime and deactivate at endTime.
    /// </summary>
    Task SetScheduledOOFAsync(OOFMessage message, DateTime startTime, DateTime endTime, ExternalAudienceScope externalAudience, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables OOF with the specified message.
    /// </summary>
    Task EnableOOFAsync(OOFMessage message, ExternalAudienceScope externalAudience, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables OOF.
    /// </summary>
    Task DisableOOFAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the user's working hours from Outlook/Exchange.
    /// </summary>
    Task<WorkingDay[]> GetOutlookWorkingHoursAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the current OOF status from Microsoft 365.
/// </summary>
public record OOFStatus
{
    public bool IsEnabled { get; init; }
    /// <summary>
    /// Raw M365 status: "disabled", "alwaysEnabled", or "scheduled".
    /// </summary>
    public string Status { get; init; } = "disabled";
    public string InternalMessage { get; init; } = string.Empty;
    public string ExternalMessage { get; init; } = string.Empty;
    public ExternalAudienceScope ExternalAudience { get; init; }
    public DateTime? ScheduledStartTime { get; init; }
    public DateTime? ScheduledEndTime { get; init; }
}
