namespace OOFManagerX.Core.Interfaces;

/// <summary>
/// Background service that monitors and synchronizes OOF status with Microsoft 365.
/// </summary>
public interface IBackgroundOOFService : IDisposable
{
    /// <summary>
    /// Starts the background monitoring service.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the background monitoring service.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the service is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the last time the OOF status was synchronized.
    /// </summary>
    DateTime? LastSyncTime { get; }

    /// <summary>
    /// Gets the last sync result message.
    /// </summary>
    string? LastSyncResult { get; }

    /// <summary>
    /// Forces an immediate sync check.
    /// </summary>
    Task SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the polling interval in milliseconds.
    /// </summary>
    void SetPollingInterval(int intervalMs);

    /// <summary>
    /// Event raised when the sync status changes.
    /// </summary>
    event EventHandler<OOFSyncEventArgs>? SyncStatusChanged;
}

/// <summary>
/// Event arguments for OOF sync status changes.
/// </summary>
public class OOFSyncEventArgs : EventArgs
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool OOFIsEnabled { get; init; }
    public DateTime SyncTime { get; init; }
}
