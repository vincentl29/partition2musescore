using System.Diagnostics;
using System.IO;

namespace Partition2MuseScore;

// Lance un process externe (Audiveris, MuseScore 4) et expose sa complétion comme une Task,
// tout en le gardant dans une liste partagée pour pouvoir le tuer si l'utilisateur ferme la
// fenêtre pendant qu'il tourne (voir MainWindow.OnClosing).
internal static class ProcessRunner
{
    public static Task RunAsync(string fileName, string arguments, List<Process> activeProcesses,
        Action<string>? onLine = null)
    {
        var tcs = new TaskCompletionSource();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        // Audiveris (un outil Java) répartit ses logs entre stdout et stderr selon le niveau ;
        // les deux flux doivent être lus pour ne rien manquer et pour éviter qu'un tampon plein
        // ne bloque le process si personne ne le draine.
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) onLine?.Invoke(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) onLine?.Invoke(args.Data); };

        process.Exited += (_, _) =>
        {
            lock (activeProcesses)
            {
                activeProcesses.Remove(process);
            }

            if (process.ExitCode != 0)
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"{Path.GetFileName(fileName)} a échoué (code {process.ExitCode})."));
            }
            else
            {
                tcs.TrySetResult();
            }

            process.Dispose();
        };

        // Enregistré avant Start() pour qu'il soit déjà suivi si le process se termine
        // immédiatement (évite une fenêtre de course avec le retrait dans Exited ci-dessus).
        lock (activeProcesses)
        {
            activeProcesses.Add(process);
        }

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return tcs.Task;
    }

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Le process s'est terminé entre la vérification et l'appel à Kill — rien à faire.
        }
    }
}
