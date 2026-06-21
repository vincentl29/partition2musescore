using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Partition2MuseScore;

// Catalogue des langues OCR Tesseract pour Audiveris : langues déjà installées localement
// (dossier tessdata) + langues disponibles au téléchargement depuis le dépôt GitHub
// tesseract-ocr/tessdata -- celui qu'Audiveris utilise lui-même via son propre Tools -> Languages
// (confirmé en inspectant son bytecode : il utilise la lib github-api contre cet exact dépôt).
// Seul ce dépôt "main" (legacy+LSTM) est compatible avec le moteur Tesseract embarqué par
// Audiveris : les variantes _fast/_best sont LSTM seul et font échouer l'OCR complètement
// ("Could not initialize TessBaseAPI languages: ... in legacy mode" -> aucun texte reconnu),
// testé et corrigé le 2026-06-20 -- voir CLAUDE.md.
internal static class OcrLanguageCatalog
{
    private const string RepoContentsUrl = "https://api.github.com/repos/tesseract-ocr/tessdata/contents/";
    private const string RawBaseUrl = "https://raw.githubusercontent.com/tesseract-ocr/tessdata/main/";

    public static readonly string TessdataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudiverisLtd", "audiveris", "config", "tessdata");

    // Un seul HttpClient partagé (cf. ToolVersionChecker) : en créer un par appel épuise les
    // sockets disponibles à la longue.
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Partition2MuseScore");
        return client;
    }

    public static List<string> GetInstalledLanguageCodes()
    {
        if (!Directory.Exists(TessdataDirectory))
        {
            return [];
        }

        return Directory.GetFiles(TessdataDirectory, "*.traineddata")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(code => !string.IsNullOrEmpty(code) && code != "osd")
            .Select(code => code!)
            .ToList();
    }

    public static bool IsInstalled(string code) =>
        File.Exists(Path.Combine(TessdataDirectory, $"{code}.traineddata"));

    // Récupère la liste des langues disponibles sur le dépôt GitHub, pour les proposer dans le
    // menu déroulant même si elles ne sont pas encore installées. Mise en cache localement
    // (même principe que ToolVersionChecker) pour rester utilisable hors-ligne après un premier
    // succès, plutôt que de ne proposer que les langues déjà installées en cas de coupure réseau.
    public static async Task<List<string>> GetAvailableLanguageCodesAsync()
    {
        try
        {
            using var response = await Client.GetAsync(RepoContentsUrl);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            var codes = json.RootElement.EnumerateArray()
                .Select(entry => entry.GetProperty("name").GetString()!)
                .Where(name => name.EndsWith(".traineddata") && name != "osd.traineddata")
                .Select(name => name[..^".traineddata".Length])
                .ToList();

            WriteCache(codes);
            return codes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ReadCache();
        }
    }

    // Télécharge un fichier de langue manquant directement depuis le dépôt "main" dans le
    // dossier tessdata d'Audiveris -- jamais _fast/_best (cf. note en tête de fichier).
    private static async Task DownloadLanguageAsync(string code)
    {
        Directory.CreateDirectory(TessdataDirectory);

        using var response = await Client.GetAsync($"{RawBaseUrl}{code}.traineddata");
        response.EnsureSuccessStatusCode();

        var destination = Path.Combine(TessdataDirectory, $"{code}.traineddata");
        await using var fileStream = File.Create(destination);
        await response.Content.CopyToAsync(fileStream);
    }

    // Une spécification de langue Audiveris peut combiner plusieurs codes ("fra+eng") : on
    // s'assure que chacun est installé avant de lancer la conversion, en téléchargeant ceux qui
    // manquent. `progress` ne reçoit un message que pour les langues réellement téléchargées --
    // une langue déjà installée ne génère aucun message (vérification quasi instantanée).
    public static async Task EnsureInstalledAsync(string specification, IProgress<ConversionProgress> progress)
    {
        foreach (var code in specification.Split('+'))
        {
            if (!IsInstalled(code))
            {
                progress.Report(new ConversionProgress(0, $"Téléchargement de la langue OCR ({code})"));
                await DownloadLanguageAsync(code);
            }
        }
    }

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Partition2MuseScore", "ocr_languages_cache.json");

    private static List<string> ReadCache()
    {
        if (!File.Exists(CachePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CachePath)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteCache(List<string> codes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(CachePath, JsonSerializer.Serialize(codes));
    }
}
