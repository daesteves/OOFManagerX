using System.Text.Json;
using Microsoft.Extensions.Logging;
using OOFManagerX.Core.Models;

namespace OOFManagerX.Core.Services;

/// <summary>
/// Service for managing OOF message templates.
/// </summary>
public class TemplatesService
{
    private static readonly string TemplatesFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OOFManagerX");
    
    private static readonly string TemplatesFile = Path.Combine(TemplatesFolder, "templates.json");
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<TemplatesService> _logger;
    private List<Template> _templates = new();

    public IReadOnlyList<Template> Templates => _templates.AsReadOnly();

    public TemplatesService(ILogger<TemplatesService> logger)
    {
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(TemplatesFile))
            {
                var json = await File.ReadAllTextAsync(TemplatesFile);
                _templates = JsonSerializer.Deserialize<List<Template>>(json, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load templates, starting with empty list");
            _templates = new();
        }
    }

    public async Task SaveTemplateAsync(Template template)
    {
        _templates.RemoveAll(t => t.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase));
        _templates.Add(template);
        await PersistAsync();
    }

    public async Task DeleteTemplateAsync(string templateId)
    {
        _templates.RemoveAll(t => t.Id == templateId);
        await PersistAsync();
    }

    public Template? GetTemplate(string templateId)
    {
        return _templates.FirstOrDefault(t => t.Id == templateId);
    }

    private async Task PersistAsync()
    {
        try
        {
            Directory.CreateDirectory(TemplatesFolder);
            var json = JsonSerializer.Serialize(_templates, JsonOptions);
            await File.WriteAllTextAsync(TemplatesFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save templates");
        }
    }
}
