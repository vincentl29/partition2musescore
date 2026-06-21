using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Partition2MuseScore;

// Installe (si absent) ou met à jour (si périmé) Audiveris/MuseScore via winget --silent, le
// gestionnaire de paquets officiel de Windows. Contrairement à la tentative de build portable
// auto-téléchargée déjà essayée et explicitement rejetée (voir
// memory/feedback_no_auto_download.md), l'appli ne télécharge ni n'extrait jamais rien
// elle-même ici : elle délègue entièrement le téléchargement et l'installation à winget. Cette
// logique vit côté appli (premier lancement) plutôt que dans une custom action du Setup.msi,
// car winget n'est pas garanti de fonctionner sous le compte SYSTEM utilisé par les actions
// différées d'un .msi par-machine élevé.
internal static class ToolUpdater
{
    public enum ToolAction
    {
        Install,
        Upgrade,
    }

    private static readonly Dictionary<string, string> WingetIds = new()
    {
        ["Audiveris"] = "audiveris.org.Audiveris",
        ["MuseScore"] = "Musescore.Musescore",
    };

    // Lance un seul processus PowerShell élevé qui enchaîne un `winget install`/`winget upgrade`
    // --silent par outil concerné (selon l'action demandée pour chacun), puis écrit le résultat
    // de chacun dans un fichier temporaire que le processus appelant (non élevé) relit ensuite.
    // Une seule invite UAC apparaît, même si Audiveris et MuseScore sont tous les deux concernés ;
    // la fenêtre du processus lui-même reste masquée, conformément à la demande d'une
    // installation/mise à jour "transparente".
    public static async Task<IReadOnlyDictionary<string, bool>> TryApplyAsync(IReadOnlyDictionary<string, ToolAction> actions)
    {
        var failure = actions.Keys.ToDictionary(name => name, _ => false);
        if (actions.Count == 0)
        {
            return failure;
        }

        var resultFilePath = Path.Combine(Path.GetTempPath(), $"Partition2MuseScore_winget_{Guid.NewGuid():N}.json");
        var script = BuildScript(actions, resultFilePath);

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-Command", script },
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return failure;
            }

            await process.WaitForExitAsync();

            if (!File.Exists(resultFilePath))
            {
                return failure;
            }

            var json = await File.ReadAllTextAsync(resultFilePath);
            File.Delete(resultFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? failure;
        }
        catch (Win32Exception)
        {
            // L'utilisateur a refusé l'invite UAC, ou l'élévation est impossible sur cette
            // machine : on continue avec la version actuellement installée plutôt que de
            // bloquer l'appli pour une mise à jour qui n'est pas critique.
            return failure;
        }
    }

    private static string BuildScript(IReadOnlyDictionary<string, ToolAction> actions, string resultFilePath)
    {
        var statements = new List<string> { "$results = @{}" };
        foreach (var (name, action) in actions)
        {
            var verb = action == ToolAction.Install ? "install" : "upgrade";
            statements.Add($"winget {verb} --id {WingetIds[name]} --silent " +
                "--accept-package-agreements --accept-source-agreements --disable-interactivity");
            statements.Add($"$results['{name}'] = ($LASTEXITCODE -eq 0)");
        }
        statements.Add($"$results | ConvertTo-Json | Out-File -FilePath '{resultFilePath}' -Encoding utf8");

        return string.Join(" ; ", statements);
    }
}
