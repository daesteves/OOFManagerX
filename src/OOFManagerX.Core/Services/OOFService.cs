using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Services;

/// <summary>
/// Service for managing OOF settings via Microsoft Graph API.
/// </summary>
public class OOFService : IOOFService
{
    private const string GraphEndpoint = "https://graph.microsoft.com/v1.0/me/mailboxSettings";
    
    private readonly IAuthenticationService _authService;
    private readonly ILogger<OOFService> _logger;
    private readonly HttpClient _httpClient;

    public OOFService(
        IAuthenticationService authService, 
        ILogger<OOFService> logger,
        HttpClient? httpClient = null)
    {
        _authService = authService;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<OOFStatus> GetCurrentOOFStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting current OOF status from Microsoft 365");

        var token = await _authService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Unable to get access token for OOF status check");
            throw new InvalidOperationException("Unable to get access token");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, GraphEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            // Handle 401 — token might be stale, force refresh and retry once
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Graph API returned 401, refreshing token and retrying");
                var freshToken = await ForceTokenRefreshAsync(cancellationToken);
                if (freshToken == null) throw new HttpRequestException("Authentication failed after token refresh");

                request = new HttpRequestMessage(HttpMethod.Get, GraphEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
                response = await _httpClient.SendAsync(request, cancellationToken);
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var mailboxSettings = JsonSerializer.Deserialize<MailboxSettingsResponse>(content, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var autoReply = mailboxSettings?.AutomaticRepliesSetting;
            if (autoReply == null)
            {
                return new OOFStatus();
            }

            return new OOFStatus
            {
                IsEnabled = autoReply.Status != "disabled",
                Status = autoReply.Status ?? "disabled",
                InternalMessage = autoReply.InternalReplyMessage ?? string.Empty,
                ExternalMessage = autoReply.ExternalReplyMessage ?? string.Empty,
                ExternalAudience = ParseExternalAudience(autoReply.ExternalAudience),
                ScheduledStartTime = autoReply.ScheduledStartDateTime?.DateTime,
                ScheduledEndTime = autoReply.ScheduledEndDateTime?.DateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OOF status");
            throw;
        }
    }

    public async Task SetOOFAsync(
        OOFMessage message, 
        bool isEnabled, 
        ExternalAudienceScope externalAudience,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting OOF status: Enabled={IsEnabled}, Audience={Audience}", isEnabled, externalAudience);

        var token = await _authService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Unable to get access token");
        }

        // Map enum to Graph API expected values
        var audienceValue = externalAudience switch
        {
            ExternalAudienceScope.None => "none",
            ExternalAudienceScope.ContactsOnly => "contactsOnly",
            ExternalAudienceScope.All => "all",
            _ => "all"
        };

        // Convert markdown to HTML for Outlook
        var internalHtml = MarkdownService.ConvertToHtml(message.InternalMessage);
        var externalHtml = MarkdownService.ConvertToHtml(message.ExternalMessage);

        var payload = new
        {
            automaticRepliesSetting = new
            {
                status = isEnabled ? "alwaysEnabled" : "disabled",
                externalAudience = audienceValue,
                internalReplyMessage = internalHtml,
                externalReplyMessage = externalHtml
            }
        };

        var json = JsonSerializer.Serialize(payload);
        _logger.LogDebug("OOF Payload: {Json}", json);
        
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Patch, GraphEndpoint)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to set OOF: {StatusCode} - {Error}", response.StatusCode, error);
            throw new HttpRequestException($"Failed to set OOF: {response.StatusCode}");
        }

        _logger.LogInformation("OOF status updated successfully");
    }

    public Task EnableOOFAsync(OOFMessage message, ExternalAudienceScope externalAudience, CancellationToken cancellationToken = default)
        => SetOOFAsync(message, true, externalAudience, cancellationToken);

    public Task DisableOOFAsync(CancellationToken cancellationToken = default)
        => SetOOFAsync(new OOFMessage(), false, ExternalAudienceScope.None, cancellationToken);

    public async Task SetScheduledOOFAsync(
        OOFMessage message,
        DateTime startTime,
        DateTime endTime,
        ExternalAudienceScope externalAudience,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting scheduled OOF: {Start} to {End}, Audience={Audience}", 
            startTime, endTime, externalAudience);

        var token = await _authService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Unable to get access token");
        }

        // Map enum to Graph API expected values
        var audienceValue = externalAudience switch
        {
            ExternalAudienceScope.None => "none",
            ExternalAudienceScope.ContactsOnly => "contactsOnly",
            ExternalAudienceScope.All => "all",
            _ => "all"
        };

        // Get local timezone for the scheduled times
        var timeZone = TimeZoneInfo.Local.Id;

        // Convert markdown to HTML for Outlook
        var internalHtml = MarkdownService.ConvertToHtml(message.InternalMessage);
        var externalHtml = MarkdownService.ConvertToHtml(message.ExternalMessage);

        var payload = new
        {
            automaticRepliesSetting = new
            {
                status = "scheduled",
                externalAudience = audienceValue,
                internalReplyMessage = internalHtml,
                externalReplyMessage = externalHtml,
                scheduledStartDateTime = new
                {
                    dateTime = startTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeZone = timeZone
                },
                scheduledEndDateTime = new
                {
                    dateTime = endTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeZone = timeZone
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        _logger.LogDebug("Scheduled OOF Payload: {Json}", json);
        
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Patch, GraphEndpoint)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to set scheduled OOF: {StatusCode} - {Error}", response.StatusCode, error);
            throw new HttpRequestException($"Failed to set scheduled OOF: {response.StatusCode}");
        }

        _logger.LogInformation("Scheduled OOF set successfully: {Start} to {End}", startTime, endTime);
    }

    private static ExternalAudienceScope ParseExternalAudience(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "all" => ExternalAudienceScope.All,
            "contactsonly" => ExternalAudienceScope.ContactsOnly,
            "none" => ExternalAudienceScope.None,
            _ => ExternalAudienceScope.All
        };
    }

    /// <summary>
    /// Forces a fresh token by calling TrySilentSignInAsync, then getting the new access token.
    /// </summary>
    private async Task<string?> ForceTokenRefreshAsync(CancellationToken cancellationToken)
    {
        var result = await _authService.TrySilentSignInAsync(cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("Token refresh via silent sign-in failed: {Error}", result.ErrorMessage);
            return null;
        }
        return await _authService.GetAccessTokenAsync(cancellationToken);
    }

    // Response DTOs for Graph API
    private class MailboxSettingsResponse
    {
        public AutomaticRepliesSettingResponse? AutomaticRepliesSetting { get; set; }
        public WorkingHoursResponse? WorkingHours { get; set; }
    }

    private class AutomaticRepliesSettingResponse
    {
        public string? Status { get; set; }
        public string? ExternalAudience { get; set; }
        public string? InternalReplyMessage { get; set; }
        public string? ExternalReplyMessage { get; set; }
        public DateTimeTimeZone? ScheduledStartDateTime { get; set; }
        public DateTimeTimeZone? ScheduledEndDateTime { get; set; }
    }

    private class DateTimeTimeZone
    {
        public DateTime DateTime { get; set; }
        public string? TimeZone { get; set; }
    }

    private class WorkingHoursResponse
    {
        public string[]? DaysOfWeek { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public TimeZoneBaseResponse? TimeZone { get; set; }
    }

    private class TimeZoneBaseResponse
    {
        public string? Name { get; set; }
    }

    public async Task<WorkingDay[]> GetOutlookWorkingHoursAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting working hours from Outlook");

        var token = await _authService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Unable to get access token");

        var request = new HttpRequestMessage(HttpMethod.Get, GraphEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var freshToken = await ForceTokenRefreshAsync(cancellationToken);
            if (freshToken == null) throw new HttpRequestException("Authentication failed");
            request = new HttpRequestMessage(HttpMethod.Get, GraphEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
            response = await _httpClient.SendAsync(request, cancellationToken);
        }

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var settings = JsonSerializer.Deserialize<MailboxSettingsResponse>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var wh = settings?.WorkingHours;
        if (wh?.DaysOfWeek == null || wh.StartTime == null || wh.EndTime == null)
        {
            _logger.LogWarning("No working hours configured in Outlook, using defaults");
            return OOFSchedule.CreateDefaultWeek().ToArray();
        }

        // Parse TimeOfDay strings like "08:00:00.0000000"
        var startTime = TimeOnly.Parse(wh.StartTime);
        var endTime = TimeOnly.Parse(wh.EndTime);

        var workDays = new HashSet<DayOfWeek>();
        foreach (var day in wh.DaysOfWeek)
        {
            if (Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var dow))
                workDays.Add(dow);
        }

        _logger.LogInformation("Outlook working hours: {Start}-{End}, days: {Days}",
            startTime, endTime, string.Join(", ", workDays));

        var result = new WorkingDay[7];
        for (var i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)i;
            result[i] = new WorkingDay(dow, startTime, endTime, !workDays.Contains(dow));
        }

        return result;
    }
}
