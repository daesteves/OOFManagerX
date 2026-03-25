using System.Text.Json;
using Microsoft.Extensions.Logging;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Services;

/// <summary>
/// Service for persisting settings to local JSON files.
/// </summary>
public class SettingsService : ISettingsService
{
    private const string SettingsFileName = "usersettings.json";
    private const string ScheduleFileName = "schedule.json";
    
    private readonly ILogger<SettingsService> _logger;
    private readonly string _settingsFolder;
    private readonly JsonSerializerOptions _jsonOptions;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        _settingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OOFManagerX");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        EnsureSettingsFolderExists();
    }

    public async Task<OOFSchedule> LoadScheduleAsync(CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_settingsFolder, ScheduleFileName);
        
        if (!File.Exists(filePath))
        {
            _logger.LogInformation("No schedule file found, returning defaults");
            return new OOFSchedule();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var schedule = JsonSerializer.Deserialize<OOFSchedule>(json, _jsonOptions);
            _logger.LogInformation("Schedule loaded successfully");
            return schedule ?? new OOFSchedule();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load schedule, returning defaults");
            return new OOFSchedule();
        }
    }

    public async Task SaveScheduleAsync(OOFSchedule schedule, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_settingsFolder, ScheduleFileName);
        
        try
        {
            var json = JsonSerializer.Serialize(schedule, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
            _logger.LogInformation("Schedule saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save schedule");
            throw;
        }
    }

    public async Task<UserSettings> LoadUserSettingsAsync(CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_settingsFolder, SettingsFileName);
        
        if (!File.Exists(filePath))
        {
            _logger.LogInformation("No user settings file found, returning defaults");
            return new UserSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, _jsonOptions);
            _logger.LogInformation("User settings loaded successfully");
            return settings ?? new UserSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user settings, returning defaults");
            return new UserSettings();
        }
    }

    public async Task SaveUserSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_settingsFolder, SettingsFileName);
        
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
            _logger.LogInformation("User settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user settings");
            throw;
        }
    }

    private void EnsureSettingsFolderExists()
    {
        if (!Directory.Exists(_settingsFolder))
        {
            Directory.CreateDirectory(_settingsFolder);
            _logger.LogInformation("Created settings folder at {Path}", _settingsFolder);
        }
    }
}
