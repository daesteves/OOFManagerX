using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OOFManagerX.App.Services;

public record UpdateInfo(string Version, string ReleaseUrl, bool IsPreRelease, string Body);

public class UpdateCheckService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/daesteves/OOFManagerX/releases";
    private const int CheckIntervalHours = 4;

    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly string _currentVersion;
    private readonly bool _isPreRelease;
    private Timer? _timer;

    public event Action<UpdateInfo>? UpdateAvailable;

    public UpdateCheckService(ILogger<UpdateCheckService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OOFManagerX", "2.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var assemblyVersion = typeof(UpdateCheckService).Assembly.GetName().Version;
        var infoVersion = typeof(UpdateCheckService).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? assemblyVersion?.ToString() ?? "0.0.0";

        // Parse version - strip build metadata (e.g., "2.0.0-dev1+abc123" -> "2.0.0-dev1")
        var plusIndex = infoVersion.IndexOf('+');
        _currentVersion = plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
        
        // Consider pre-release if: has pre-release tag OR version is below 1.0 (early adopter)
        TryParseVersion(_currentVersion, out var parsed);
        _isPreRelease = _currentVersion.Contains("-dev") || _currentVersion.Contains("-pre")
                        || _currentVersion.Contains("-alpha") || _currentVersion.Contains("-beta")
                        || _currentVersion.Contains("-rc")
                        || (parsed.Major == 0);

        _logger.LogInformation("Update checker initialized. Current version: {Version}, PreRelease: {IsPreRelease}",
            _currentVersion, _isPreRelease);
    }

    public void Start()
    {
        // Check after 5 seconds, then every N hours
        _timer = new Timer(async _ => await CheckForUpdatesAsync(),
            null, TimeSpan.FromSeconds(5), TimeSpan.FromHours(CheckIntervalHours));
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            _logger.LogDebug("Checking for updates...");

            var response = await _httpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API returned {StatusCode}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize<JsonElement[]>(json);
            if (releases == null || releases.Length == 0) return;

            // Parse current version for comparison
            if (!TryParseVersion(_currentVersion, out var currentParsed)) return;

            foreach (var release in releases)
            {
                var tagName = release.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tagName)) continue;

                var isReleasePreRelease = release.GetProperty("prerelease").GetBoolean()
                    || release.TryGetProperty("draft", out var draft) && draft.GetBoolean();
                var releaseVersion = tagName.TrimStart('v');

                // Filter: production users only see production releases
                if (!_isPreRelease && isReleasePreRelease) continue;

                if (!TryParseVersion(releaseVersion, out var releaseParsed)) continue;

                // Compare: is this release newer?
                if (CompareVersions(releaseParsed, currentParsed) > 0)
                {
                    var htmlUrl = release.GetProperty("html_url").GetString() ?? "";
                    var body = release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

                    _logger.LogInformation("Update available: {Version} (current: {Current})", releaseVersion, _currentVersion);

                    UpdateAvailable?.Invoke(new UpdateInfo(releaseVersion, htmlUrl, isReleasePreRelease, body));
                    return; // Only notify for the latest applicable release
                }
            }

            _logger.LogDebug("No updates available");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
        }
    }

    private static bool TryParseVersion(string version, out (int Major, int Minor, int Patch, string Pre) parsed)
    {
        parsed = default;
        var pre = "";
        var dashIdx = version.IndexOf('-');
        if (dashIdx >= 0)
        {
            pre = version[(dashIdx + 1)..];
            version = version[..dashIdx];
        }

        var parts = version.Split('.');
        if (parts.Length < 2) return false;

        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;

        parsed = (major, minor, patch, pre);
        return true;
    }

    private static int CompareVersions((int Major, int Minor, int Patch, string Pre) a,
                                        (int Major, int Minor, int Patch, string Pre) b)
    {
        var cmp = a.Major.CompareTo(b.Major);
        if (cmp != 0) return cmp;
        cmp = a.Minor.CompareTo(b.Minor);
        if (cmp != 0) return cmp;
        cmp = a.Patch.CompareTo(b.Patch);
        if (cmp != 0) return cmp;

        var aIsPre = !string.IsNullOrEmpty(a.Pre);
        var bIsPre = !string.IsNullOrEmpty(b.Pre);
        if (!aIsPre && bIsPre) return 1;
        if (aIsPre && !bIsPre) return -1;

        return string.Compare(a.Pre, b.Pre, StringComparison.OrdinalIgnoreCase);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
