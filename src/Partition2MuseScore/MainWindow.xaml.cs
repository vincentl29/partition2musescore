using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Partition2MuseScore;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir la partition source",
            Filter = "Images et PDF (*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff|Tous les fichiers (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            InputPathTextBox.Text = dialog.FileName;
        }
    }

    private void InputPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var inputPath = InputPathTextBox.Text;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        OutputPathTextBox.Text = Path.ChangeExtension(inputPath, ".mscz");
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Choisir le fichier MuseScore de sortie",
            Filter = "Fichier MuseScore (*.mscz)|*.mscz",
            DefaultExt = "mscz"
        };

        if (dialog.ShowDialog() == true)
        {
            OutputPathTextBox.Text = dialog.FileName;
        }
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var inputPath = InputPathTextBox.Text;
        var outputPath = OutputPathTextBox.Text;

        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            MessageBox.Show(this, "Merci de renseigner le fichier source et le fichier de sortie.",
                "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ConvertButton.IsEnabled = false;
        OpenFolderButton.Visibility = Visibility.Collapsed;
        ConversionProgressBar.Value = 0;
        ConversionStatusText.Text = "";
        ProgressPanel.Visibility = Visibility.Visible;

        // IProgress<T> capture le SynchronizationContext du thread UI à la construction :
        // Report() peut donc être appelé depuis le thread d'arrière-plan qui lit la sortie
        // d'Audiveris, le callback ci-dessous s'exécute quand même sur le thread UI.
        var progress = new Progress<ConversionProgress>(p =>
        {
            ConversionProgressBar.Value = p.Percent;
            ConversionStatusText.Text = $"{p.Status} — {p.Percent}%";
        });

        var capturedLog = new List<string>();

        try
        {
            await ConvertScoreAsync(inputPath, outputPath, progress, capturedLog);
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            var logPath = WriteErrorLog(inputPath, outputPath, ex, capturedLog);
            MessageBox.Show(this, $"La conversion a échoué :\n{ex.Message}\n\nDétails : {logPath}",
                "Erreur de conversion", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ConvertButton.IsEnabled = true;
        }
    }

    // Écrit un fichier .log à côté de la destination prévue, avec l'exception complète
    // (message + pile d'appels) et tout ce qu'Audiveris/MuseScore 4 ont affiché avant l'échec.
    private static string WriteErrorLog(string inputPath, string outputPath, Exception ex, IReadOnlyList<string> capturedLog)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Directory.GetCurrentDirectory();
        }

        Directory.CreateDirectory(outputDir);

        var logFileName = $"{Path.GetFileNameWithoutExtension(outputPath)}_erreur_{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var logPath = Path.Combine(outputDir, logFileName);

        var content = new StringBuilder();
        content.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        content.AppendLine($"Source : {inputPath}");
        content.AppendLine($"Destination : {outputPath}");
        content.AppendLine();
        content.AppendLine("Erreur :");
        content.AppendLine(ex.ToString());
        content.AppendLine();
        content.AppendLine("Sortie d'Audiveris / MuseScore 4 :");
        foreach (var line in capturedLog)
        {
            content.AppendLine(line);
        }

        File.WriteAllText(logPath, content.ToString());
        return logPath;
    }

    private readonly record struct ConversionProgress(int Percent, string Status);

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(OutputPathTextBox.Text);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\""
        });
    }

    // Le pipeline Audiveris compte 20 étapes par page (LOAD, BINARY, ... PAGE — cf. handbook).
    // Audiveris occupe l'essentiel du temps de traitement ; MuseScore 4 ne fait qu'exporter
    // un fichier déjà prêt, d'où le partage de la barre 0-90% / 90-100%.
    private const int AudiverisStepsPerSheet = 20;
    private const double AudiverisPhaseWeight = 0.9;

    private static readonly Regex SheetCountRegex =
        new(@"\bBook\s+\d+\s*\|\s*(\d+)\s+sheets?\s+in\b", RegexOptions.Compiled);

    private static readonly Regex StepRegex =
        new(@"\[[^\]]*#(\d+)\]\s+StepMonitoring\s+\d+\s*\|\s*(\w+)", RegexOptions.Compiled);

    // Pipeline : image/PDF -> Audiveris (-> .mxl) -> MuseScore 4 (-> .mscz)
    private static async Task ConvertScoreAsync(string inputPath, string outputPath,
        IProgress<ConversionProgress> progress, List<string> capturedLog)
    {
        var audiverisExe = FindExecutable("Audiveris", "Audiveris.exe",
            [@"C:\Program Files\Audiveris\Audiveris.exe"], "Audiveris");
        var museScoreExe = FindExecutable("MuseScore", @"bin\MuseScore4.exe",
            [@"C:\Program Files\MuseScore 4\bin\MuseScore4.exe"], "MuseScore 4");

        var workDir = Path.Combine(Path.GetTempPath(), "Partition2MuseScore_" + Guid.NewGuid());
        Directory.CreateDirectory(workDir);

        try
        {
            var totalSheets = 1;
            var stepsSeen = 0;

            void OnAudiverisLine(string line)
            {
                capturedLog.Add(line);

                var sheetsMatch = SheetCountRegex.Match(line);
                if (sheetsMatch.Success)
                {
                    totalSheets = int.Parse(sheetsMatch.Groups[1].Value);
                    return;
                }

                var stepMatch = StepRegex.Match(line);
                if (!stepMatch.Success)
                {
                    return;
                }

                stepsSeen++;
                var currentSheet = int.Parse(stepMatch.Groups[1].Value);
                var fraction = Math.Min(1.0, stepsSeen / (double)(AudiverisStepsPerSheet * totalSheets));
                var percent = (int)(fraction * AudiverisPhaseWeight * 100);
                progress.Report(new ConversionProgress(percent,
                    $"Reconnaissance Audiveris — page {currentSheet}/{totalSheets}"));
            }

            progress.Report(new ConversionProgress(0, "Lancement d'Audiveris"));
            await RunProcessAsync(audiverisExe, $"-batch -export -output \"{workDir}\" \"{inputPath}\"", OnAudiverisLine);

            progress.Report(new ConversionProgress(90, "Fusion des mouvements détectés"));

            // Audiveris exporte un .mxl par "mouvement" détecté (système indenté = nouveau
            // mouvement pour lui). Sur un scan multi-pages d'une seule partition, ça produit
            // souvent plusieurs fichiers qu'il faut recoller en un seul score continu.
            var mxlPaths = Directory.GetFiles(workDir, "*.mxl")
                .OrderBy(GetMovementNumber)
                .ToArray();

            if (mxlPaths.Length == 0)
            {
                throw new FileNotFoundException("Audiveris n'a produit aucun fichier MusicXML (.mxl).");
            }

            var mergedScore = MergeMovements(mxlPaths.Select(ExtractScorePartwise).ToList());

            var musicXmlPath = Path.Combine(workDir, "merged.musicxml");
            mergedScore.Save(musicXmlPath);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            progress.Report(new ConversionProgress(95, "Export vers MuseScore 4"));
            await RunProcessAsync(museScoreExe, $"\"{musicXmlPath}\" -o \"{outputPath}\"", capturedLog.Add);

            progress.Report(new ConversionProgress(100, "Terminé"));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    // Audiveris nomme ses fichiers "<livre>.mvtN.mxl" ; on trie sur N pour garder l'ordre
    // d'origine de la partition (le tri alphabétique seul casserait mvt10 avant mvt2).
    private static int GetMovementNumber(string mxlPath)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(mxlPath), @"mvt(\d+)$", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    // Un .mxl est un zip MusicXML : container.xml indique le chemin du vrai fichier de score.
    private static XDocument ExtractScorePartwise(string mxlPath)
    {
        using var archive = ZipFile.OpenRead(mxlPath);

        var containerEntry = archive.GetEntry("META-INF/container.xml")
            ?? throw new InvalidDataException($"{Path.GetFileName(mxlPath)} : container.xml manquant.");

        string rootFilePath;
        using (var containerStream = containerEntry.Open())
        {
            rootFilePath = XDocument.Load(containerStream).Descendants("rootfile")
                .Select(e => (string?)e.Attribute("full-path"))
                .FirstOrDefault()
                ?? throw new InvalidDataException($"{Path.GetFileName(mxlPath)} : rootfile introuvable.");
        }

        var scoreEntry = archive.GetEntry(rootFilePath)
            ?? throw new InvalidDataException($"{Path.GetFileName(mxlPath)} : entrée '{rootFilePath}' introuvable.");

        using var scoreStream = scoreEntry.Open();
        return XDocument.Load(scoreStream);
    }

    private sealed record MergedPart(string Name, XElement Element);

    // Concatène les mesures de chaque mouvement suivant à la fin des parties du premier, en
    // associant les parties par leur nom (ex. "Piano", "S.") plutôt que par position : Audiveris
    // réattribue des id "P1", "P2"... indépendamment à chaque mouvement, donc la position seule
    // ne garantit pas qu'il s'agit du même instrument. Une partie absente d'un mouvement (ex. les
    // voix qui n'entrent qu'après une intro au piano seul) est comblée par des mesures de silence
    // pour que toutes les parties restent synchronisées sur le même nombre de mesures.
    private static XDocument MergeMovements(IReadOnlyList<XDocument> movements)
    {
        var merged = movements[0];
        var root = merged.Root!;
        var partList = root.Element("part-list")!;

        var parts = root.Elements("part")
            .Select(part => new MergedPart(GetPartName(partList, part), part))
            .ToList();

        foreach (var movement in movements.Skip(1))
        {
            var movementRoot = movement.Root!;
            var movementPartList = movementRoot.Element("part-list")!;
            var movementParts = movementRoot.Elements("part").ToList();
            var matchedNames = new HashSet<string>();

            foreach (var movementPart in movementParts)
            {
                var name = GetPartName(movementPartList, movementPart);
                matchedNames.Add(name);

                var existing = parts.FirstOrDefault(p => p.Name == name);
                if (existing is null)
                {
                    existing = AddNewPart(root, partList, parts, movementPartList, name);
                    parts.Add(existing);
                }

                var measureNumber = existing.Element.Elements("measure").Count();
                foreach (var measure in movementPart.Elements("measure"))
                {
                    measureNumber++;
                    measure.SetAttributeValue("number", measureNumber.ToString());
                    existing.Element.Add(measure);
                }
            }

            // Parties déjà connues mais absentes de ce mouvement (ex. les voix pas encore
            // entrées) : on bouche le trou avec des silences pour garder tout aligné.
            var reference = movementParts.FirstOrDefault()?.Elements("measure").ToList() ?? [];
            foreach (var part in parts.Where(p => !matchedNames.Contains(p.Name)))
            {
                var measureNumber = part.Element.Elements("measure").Count();
                foreach (var rest in BuildRestMeasures(reference, measureNumber + 1))
                {
                    part.Element.Add(rest);
                }
            }
        }

        return merged;
    }

    private static string GetPartName(XElement partList, XElement part)
    {
        var id = (string)part.Attribute("id")!;
        var scorePart = partList.Elements("score-part").First(sp => (string)sp.Attribute("id")! == id);
        return (string?)scorePart.Element("part-name") ?? id;
    }

    // Une partie qui apparaît seulement à partir d'un mouvement ultérieur (ex. une voix qui
    // n'entre qu'après l'intro) : on la crée et on la fait démarrer par des silences couvrant
    // tout ce qui a déjà été fusionné, pour rester synchronisée avec les parties existantes.
    private static MergedPart AddNewPart(XElement root, XElement partList, List<MergedPart> parts,
        XElement movementPartList, string name)
    {
        var newPartId = $"P{parts.Count + 1}";

        var scorePart = new XElement(movementPartList.Elements("score-part")
            .First(sp => (string?)sp.Element("part-name") == name));
        scorePart.SetAttributeValue("id", newPartId);
        partList.Add(scorePart);

        var partElement = new XElement("part", new XAttribute("id", newPartId));
        root.Add(partElement);

        var reference = parts.Count > 0 ? parts[0].Element.Elements("measure").ToList() : [];
        foreach (var rest in BuildRestMeasures(reference, 1))
        {
            partElement.Add(rest);
        }

        return new MergedPart(name, partElement);
    }

    // Génère des mesures de silence complet calées sur le nombre/durée des mesures de
    // référence (pour suivre les changements de mesure éventuels), avec leurs propres
    // <divisions> : chaque partie MusicXML porte ses divisions indépendamment des autres.
    private static List<XElement> BuildRestMeasures(IReadOnlyList<XElement> referenceMeasures, int startNumber)
    {
        var measures = new List<XElement>();
        var beats = 4;
        var beatType = 4;
        var number = startNumber;

        for (var i = 0; i < referenceMeasures.Count; i++)
        {
            var previousBeats = beats;
            var previousBeatType = beatType;
            var time = referenceMeasures[i].Element("attributes")?.Element("time");
            if (time is not null)
            {
                beats = (int?)time.Element("beats") ?? beats;
                beatType = (int?)time.Element("beat-type") ?? beatType;
            }

            var measure = new XElement("measure", new XAttribute("number", number++));

            if (i == 0 || beats != previousBeats || beatType != previousBeatType)
            {
                measure.Add(new XElement("attributes",
                    i == 0 ? new XElement("divisions", 1) : null,
                    new XElement("time", new XElement("beats", beats), new XElement("beat-type", beatType))));
            }

            measure.Add(new XElement("note",
                new XElement("rest", new XAttribute("measure", "yes")),
                new XElement("duration", beats * 4 / beatType)));

            measures.Add(measure);
        }

        return measures;
    }

    // Cherche l'exécutable via le registre Windows (InstallLocation déclaré par le .msi),
    // quel que soit le lecteur/dossier choisi à l'installation, avec un repli sur l'emplacement
    // par défaut pour les installations qui n'apparaissent pas dans le registre.
    private static string FindExecutable(string displayNameContains, string relativeExePath,
        string[] fallbackPaths, string toolName)
    {
        var installLocation = FindInstallLocationFromRegistry(displayNameContains);
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

    private static string? FindInstallLocationFromRegistry(string displayNameContains)
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
                    return installLocation;
                }
            }
        }

        return null;
    }

    private static Task RunProcessAsync(string fileName, string arguments, Action<string>? onLine = null)
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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return tcs.Task;
    }
}