using System.IO;
using Microsoft.Win32;

namespace Partition2MuseScore;

internal sealed record RegistryAppInfo(string? InstallLocation, string? DisplayVersion);

// Localise Audiveris/MuseScore 4 via le registre Windows (InstallLocation déclaré par le .msi),
// quel que soit le lecteur/dossier choisi à l'installation, avec un repli sur l'emplacement
// par défaut pour les installations qui n'apparaissent pas dans le registre.
internal static class ToolLocator
{
    public static string FindExecutable(string displayNameContains, string relativeExePath,
        string[] fallbackPaths, string toolName)
    {
        var installLocation = FindRegistryAppInfo(displayNameContains).InstallLocation;
        if (installLocation is not null)
        {
            var exePath = Path.Combine(installLocation, relativeExePath);
            if (File.Exists(exePath))
            {
                return exePath;
            }
        }

        return fallbackPaths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"{toolName} est introuvable. Vérifiez qu'il est installé.");
    }

    public static RegistryAppInfo FindRegistryAppInfo(string displayNameContains)
    {
        string[] uninstallKeyPaths =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];

        foreach (var keyPath in uninstallKeyPaths)
        {
            using var uninstallKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                var installLocation = subKey?.GetValue("InstallLocation") as string;

                if (!string.IsNullOrEmpty(installLocation)
                    && displayName?.Contains(displayNameContains, StringComparison.OrdinalIgnoreCase) == true)
                {
                    var displayVersion = subKey?.GetValue("DisplayVersion") as string;
                    return new RegistryAppInfo(installLocation, displayVersion);
                }
            }
        }

        return new RegistryAppInfo(null, null);
    }
}
