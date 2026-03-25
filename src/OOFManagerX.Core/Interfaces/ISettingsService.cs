using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Interfaces;

/// <summary>
/// Service for persisting and loading user settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads the OOF schedule from storage.
    /// </summary>
    Task<OOFSchedule> LoadScheduleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the OOF schedule to storage.
    /// </summary>
    Task SaveScheduleAsync(OOFSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads user settings from storage.
    /// </summary>
    Task<UserSettings> LoadUserSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves user settings to storage.
    /// </summary>
    Task SaveUserSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
