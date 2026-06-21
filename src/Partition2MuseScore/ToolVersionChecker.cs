using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Partition2MuseScore;

// Compare la version installée (registre Windows, via ToolLocator) à la dernière version
// publiée sur GitHub. Sert à la fois à l'affichage informatif et, depuis l'ajout de ToolUpdater,
// à décider si une mise à jour winget doit être déclenchée — l'appli elle-même ne télécharge ni
// n'installe jamais rien directement.
internal static class ToolVersionChecker
{
    public readonly record struct VersionInfo(string? Installed, string? Latest, bool LatestFromCache);

    private static readonly Dictionary<string, string> GitHubRepos = new()
    {
        ["Audiveris"] = "Audiveris/audiveris",
        ["MuseScore"] = "musescore/MuseScore",
    };

    public static async Task<VersionInfo> GetVersionInfoAsync(string toolName)
    {
        var installedVersion = ToolLocator.FindRegistryAppInfo(toolName).DisplayVersion;
        var latest = await FetchLatestVersionAsync(toolName);
        return new VersionInfo(installedVersion, latest?.Version, latest?.FromCache ?? false);
    }

    public static string Describe(string toolName, VersionInfo info)
    {
        var installedText = info.Installed is null ? "non installé" : $"installé {info.Installed}";
        var latestText = info.Latest is null
            ? "dernière version disponible : inconnue (pas de connexion et pas de cache)"
            : $"dernière version disponible {info.Latest}{(info.LatestFromCache ? " (cache, hors-ligne)" : "")}";

        return $"{toolName} : {installedText} — {latestText}";
    }

    // Détecte si la version installée a du retard sur la dernière publiée, pour déclencher une
    // mise à jour winget (voir ToolUpdater). Volontairement conservateur : si l'un ou l'autre
    // numéro de version ne se parse pas en System.Version (suffixe inattendu, format inconnu),
    // on considère qu'il n'y a pas de mise à jour plutôt que de risquer une invite UAC superflue
    // sur un simple écart de formatage entre le tag GitHub et le DisplayVersion du registre.
    public static bool IsUpgradeAvailable(VersionInfo info)
    {
        if (info.Installed is null || info.Latest is null)
        {
            return false;
        }

        var installed = ParseLeadingVersion(info.Installed);
        var latest = ParseLeadingVersion(info.Latest);

        return installed is not null && latest is not null && latest > installed;
    }

    private static Version? ParseLeadingVersion(string raw)
    {
        var match = Regex.Match(raw.TrimStart('v', 'V'), @"^\d+(\.\d+){0,3}");
        return match.Success && Version.TryParse(match.Value, out var version) ? version : null;
    }

    // Un seul HttpClient pour toute la durée de vie de l'appli : en créer un par appel épuise
    // les sockets disponibles à la longue (piège classique avec HttpClient en .NET).
    private static readonly HttpClient GitHubApiClient = CreateGitHubApiClient();

    private static HttpClient CreateGitHubApiClient()
    {
        var client = new HttpClient();
        // L'API GitHub rejette toute requête sans User-Agent (HTTP 403).
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Partition2MuseScore");
        return client;
    }

    // Demande la dernière version publiée (tag de release GitHub) d'un outil. En cas d'échec
    // réseau, retombe sur la dernière valeur connue mise en cache localement plutôt que de ne
    // rien afficher — utile en l'absence de connexion internet.
    private static async Task<(string Version, bool FromCache)?> FetchLatestVersionAsync(string toolName)
    {
        var repo = GitHubRepos[toolName];

        try
        {
            using var response = await GitHubApiClient.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            var version = json.RootElement.GetProperty("tag_name").GetString()!;

            WriteVersionCache(toolName, version);
            return (version, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            var cachedVersion = ReadVersionCache(toolName);
            return cachedVersion is null ? null : (cachedVersion, true);
        }
    }

    private static readonly string VersionCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Partition2MuseScore", "version_cache.json");

    private static string? ReadVersionCache(string toolName) =>
        ReadAllVersionCache().GetValueOrDefault(toolName);

    private static void WriteVersionCache(string toolName, string version)
    {
        var cache = ReadAllVersionCache();
        cache[toolName] = version;

        Directory.CreateDirectory(Path.GetDirectoryName(VersionCachePath)!);
        File.WriteAllText(VersionCachePath, JsonSerializer.Serialize(cache));
    }

    private static Dictionary<string, string> ReadAllVersionCache()
    {
        if (!File.Exists(VersionCachePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(VersionCachePath)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
