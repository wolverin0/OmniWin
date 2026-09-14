using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; } = false;
    public string CurrentVersion { get; set; } = "1.2.0";
    public string LatestVersion { get; set; } = "1.2.0";
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = "https://github.com/pauol/OmniWin/releases/latest";
    public DateTime? PublishedAt { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

public class UpdateCheckService
{
    public const string CURRENT_VERSION = "1.2.0";
    private static readonly Lazy<UpdateCheckService> _instance = new(() => new UpdateCheckService());
    public static UpdateCheckService Instance => _instance.Value;

    private readonly HttpClient _httpClient;

    public UpdateCheckService(HttpClient? client = null)
    {
        _httpClient = client ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OmniWin-Updater/1.2.0");
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string repoOwner = "pauol", string repoName = "OmniWin")
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = CURRENT_VERSION,
            LatestVersion = CURRENT_VERSION
        };

        try
        {
            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                result.StatusMessage = $"No se pudo verificar: HTTP {(int)response.StatusCode}";
                return result;
            }

            string json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                result.StatusMessage = "Respuesta de actualización inválida.";
                return result;
            }

            string tagName = node["tag_name"]?.GetValue<string>()?.TrimStart('v', 'V') ?? "1.2.0";
            result.LatestVersion = tagName;
            result.ReleaseTitle = node["name"]?.GetValue<string>() ?? $"Versión {tagName}";
            result.ReleaseNotes = node["body"]?.GetValue<string>() ?? string.Empty;
            result.DownloadUrl = node["html_url"]?.GetValue<string>() ?? $"https://github.com/{repoOwner}/{repoName}/releases/latest";

            if (node["published_at"] != null && DateTime.TryParse(node["published_at"]!.GetValue<string>(), out var dt))
            {
                result.PublishedAt = dt;
            }

            if (Version.TryParse(CURRENT_VERSION, out var cur) && Version.TryParse(tagName, out var latest))
            {
                result.IsUpdateAvailable = latest > cur;
                result.StatusMessage = result.IsUpdateAvailable
                    ? $"¡Nueva versión disponible: v{tagName}!"
                    : "OmniWin está actualizado a la última versión.";
            }
            else
            {
                result.IsUpdateAvailable = !string.Equals(CURRENT_VERSION, tagName, StringComparison.OrdinalIgnoreCase);
                result.StatusMessage = result.IsUpdateAvailable
                    ? $"¡Nueva versión disponible: v{tagName}!"
                    : "OmniWin está actualizado a la última versión.";
            }
        }
        catch (Exception ex)
        {
            result.StatusMessage = $"Error al buscar actualizaciones: {ex.Message}";
        }

        return result;
    }
}
