using System.Timers;
using Microsoft.Extensions.Logging;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Services;

/// <summary>
/// Background service that polls Microsoft 365 and ensures OOF status matches the schedule.
/// </summary>
public class BackgroundOOFService : IBackgroundOOFService
{
    private readonly IOOFService _oofService;
    private readonly IScheduleService _scheduleService;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<BackgroundOOFService> _logger;
    
    private System.Timers.Timer? _timer;
    private bool _isDisposed;
    private bool _isSyncing;
    
    // Default polling interval: 5 minutes
    private const int DefaultPollingIntervalMs = 5 * 60 * 1000;
    
    private int _pollingIntervalMs = 
        Environment.GetEnvironmentVariable("OOFMANAGERX_FAST_POLL") == "1" 
            ? 30 * 1000 
            : DefaultPollingIntervalMs;

    public bool IsRunning { get; private set; }
    public DateTime? LastSyncTime { get; private set; }
    public string? LastSyncResult { get; private set; }

    public event EventHandler<OOFSyncEventArgs>? SyncStatusChanged;

    public BackgroundOOFService(
        IOOFService oofService,
        IScheduleService scheduleService,
        IAuthenticationService authService,
        ILogger<BackgroundOOFService> logger)
    {
        _oofService = oofService;
        _scheduleService = scheduleService;
        _authService = authService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Background OOF service is already running");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting background OOF service with {Interval}ms polling interval", _pollingIntervalMs);

        _timer = new System.Timers.Timer(_pollingIntervalMs);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();

        IsRunning = true;

        // Perform initial sync after a short delay to let the app initialize
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000, cancellationToken);
            await SyncNowAsync(cancellationToken);
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Stopping background OOF service");

        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;

        IsRunning = false;

        return Task.CompletedTask;
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (_isSyncing)
        {
            _logger.LogDebug("Sync already in progress, skipping");
            return;
        }

        try
        {
            _isSyncing = true;
            UpdateSyncStatus(true, LocalizedStrings.Syncing, false);
            await PerformSyncAsync(cancellationToken);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await SyncNowAsync();
    }

    private async Task PerformSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Performing OOF sync check");

        // Check if user is signed in
        if (!_authService.IsSignedIn)
        {
            _logger.LogDebug("Not signed in - skipping sync");
            UpdateSyncStatus(false, LocalizedStrings.NotSignedIn, false);
            return;
        }

        try
        {
            var now = DateTime.Now;
            var schedule = _scheduleService.GetSchedule();
            var shouldBeActive = _scheduleService.ShouldOOFBeActive(now);
            var oofWindow = _scheduleService.GetOOFScheduleWindow(now);

            _logger.LogDebug("Schedule check: OOF should be {Status} at {Time}",
                shouldBeActive ? "active" : "inactive", now);

            if (oofWindow == null)
            {
                _logger.LogWarning("Could not calculate OOF schedule window");
                UpdateSyncStatus(true, LocalizedStrings.OOFCorrectlyDisabled, false);
                return;
            }

            var (start, end, message) = oofWindow.Value;

            // Get current OOF status from Microsoft 365
            var currentStatus = await _oofService.GetCurrentOOFStatusAsync(cancellationToken);

            _logger.LogDebug("Current M365 OOF: status={Status}, scheduled={Start} to {End}",
                currentStatus.Status,
                currentStatus.ScheduledStartTime?.ToString("g") ?? "none",
                currentStatus.ScheduledEndTime?.ToString("g") ?? "none");

            // Check if M365 already has the correct scheduled window
            if (NeedsScheduleUpdate(currentStatus, start, end))
            {
                _logger.LogInformation("Setting OOF scheduled window: {Start} to {End}", start, end);
                await _oofService.SetScheduledOOFAsync(
                    message, start, end, schedule.ExternalAudience, cancellationToken);
                _logger.LogInformation("OOF schedule updated successfully");
            }
            else
            {
                _logger.LogDebug("OOF schedule already correct");
            }

            // Build status message
            var statusMessage = BuildStatusMessage(shouldBeActive, oofWindow, now);
            UpdateSyncStatus(true, statusMessage, shouldBeActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync OOF status");
            UpdateSyncStatus(false, $"{LocalizedStrings.SyncFailed}: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Determines if the M365 scheduled window needs to be updated.
    /// </summary>
    private static bool NeedsScheduleUpdate(OOFStatus currentStatus, DateTime expectedStart, DateTime expectedEnd)
    {
        // Must be in scheduled mode
        if (currentStatus.Status != "scheduled")
            return true;

        if (!currentStatus.ScheduledStartTime.HasValue || !currentStatus.ScheduledEndTime.HasValue)
            return true;

        // Allow 2-minute tolerance for time comparison
        var startMatch = Math.Abs((currentStatus.ScheduledStartTime.Value - expectedStart).TotalMinutes) < 2;
        var endMatch = Math.Abs((currentStatus.ScheduledEndTime.Value - expectedEnd).TotalMinutes) < 2;

        return !(startMatch && endMatch);
    }

    private string BuildStatusMessage(
        bool oofCurrentlyActive, 
        (DateTime Start, DateTime End, OOFMessage Message)? nextPeriod,
        DateTime now)
    {
        if (oofCurrentlyActive)
        {
            // OOF is currently active
            if (nextPeriod.HasValue && nextPeriod.Value.End > now)
            {
                // Show when it will end
                return LocalizedStrings.FormatOOFScheduled(nextPeriod.Value.Start, nextPeriod.Value.End);
            }
            return LocalizedStrings.OOFCorrectlyEnabled;
        }
        else
        {
            // OOF is currently inactive (working hours)
            if (nextPeriod.HasValue && nextPeriod.Value.Start > now)
            {
                // Show when next OOF period starts
                var relativeTime = LocalizedStrings.FormatRelativeTime(nextPeriod.Value.Start);
                return $"{LocalizedStrings.OOFCorrectlyDisabled} • {relativeTime}";
            }
            return LocalizedStrings.OOFCorrectlyDisabled;
        }
    }

    private void UpdateSyncStatus(bool success, string message, bool oofEnabled)
    {
        LastSyncTime = DateTime.Now;
        LastSyncResult = message;

        var args = new OOFSyncEventArgs
        {
            Success = success,
            Message = message,
            OOFIsEnabled = oofEnabled,
            SyncTime = LastSyncTime.Value
        };

        SyncStatusChanged?.Invoke(this, args);
    }

    public void SetPollingInterval(int intervalMs)
    {
        _pollingIntervalMs = intervalMs;
        _logger.LogInformation("Polling interval changed to {Interval}ms", intervalMs);

        if (_timer != null)
        {
            _timer.Interval = intervalMs;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;

        IsRunning = false;
    }
}
