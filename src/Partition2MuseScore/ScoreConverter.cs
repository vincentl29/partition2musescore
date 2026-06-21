using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Partition2MuseScore;

internal readonly record struct ConversionProgress(int Percent, string Status);

internal readonly record struct ConversionOptions(
    string OcrLanguage, bool KeepOmr, string? StylePath);

// Orchestre le pipeline complet : image/PDF -> Audiveris (-> .mxl) -> fusion -> MuseScore (-> .mscz).
internal static class ScoreConverter
{
    // Le pipeline Audiveris compte 20 étapes par page (LOAD, BINARY, ... PAGE — cf. handbook).
    // Audiveris occupe l'essentiel du temps de traitement ; MuseScore 4 ne fait qu'exporter
    // un fichier déjà prêt, d'où le partage de la barre 0-90% / 90-100%.
    private const int AudiverisStepsPerSheet = 20;
    private const double AudiverisPhaseWeight = 0.9;

    private static readonly Regex SheetCountRegex =
        new(@"\bBook\s+\d+\s*\|\s*(\d+)\s+sheets?\s+in\b", RegexOptions.Compiled);

    private static readonly Regex StepRegex =
        new(@"\[[^\]]*#(\d+)\]\s+StepMonitoring\s+\d+\s*\|\s*(\w+)", RegexOptions.Compiled);

    public static async Task ConvertAsync(string inputPath, string outputPath, ConversionOptions options,
        IProgress<ConversionProgress> progress, List<string> capturedLog, List<Process> activeProcesses)
    {
        var audiverisExe = ToolLocator.FindExecutable("Audiveris", "Audiveris.exe",
            [@"C:\Program Files\Audiveris\Audiveris.exe"], "Audiveris");
        var museScoreExe = ToolLocator.FindExecutable("MuseScore", @"bin\MuseScore4.exe",
            [@"C:\Program Files\MuseScore 4\bin\MuseScore4.exe"], "MuseScore 4");

        var workDir = Path.Combine(Path.GetTempPath(), "Partition2MuseScore_" + Guid.NewGuid());
        Directory.CreateDirectory(workDir);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

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

            var audiverisInputPath = inputPath;
            if (string.Equals(Path.GetExtension(inputPath), ".omr", StringComparison.OrdinalIgnoreCase))
            {
                // Un .omr est déjà un projet Audiveris (éventuellement corrigé à la main dans son
                // interface après un -save précédent) : aucun prétraitement à faire, Audiveris le
                // recharge directement et ne retranscrit que les étapes pas encore atteintes.
                // Particularité vérifiée empiriquement : quand l'entrée est un .omr, Audiveris
                // IGNORE -output pour l'export et écrit le(s) .mxl à côté du .omr lui-même (son
                // emplacement courant, pas une métadonnée interne) -- on copie donc le .omr dans
                // workDir avant de le passer à Audiveris, sinon le .mxl atterrirait dans le
                // dossier source de l'utilisateur et la recherche ci-dessous ne trouverait rien.
                progress.Report(new ConversionProgress(0, "Reprise du projet Audiveris (.omr)"));
                audiverisInputPath = Path.Combine(workDir, Path.GetFileName(inputPath));
                File.Copy(inputPath, audiverisInputPath);
            }
            else if (ImagePreprocessor.CanPreprocess(inputPath))
            {
                progress.Report(new ConversionProgress(0, "Prétraitement de l'image"));
                audiverisInputPath = ImagePreprocessor.Preprocess(inputPath, workDir);
            }
            else if (PdfPagePreprocessor.CanPreprocess(inputPath))
            {
                audiverisInputPath = PdfPagePreprocessor.Preprocess(inputPath, workDir, progress);
            }

            progress.Report(new ConversionProgress(0, "Lancement d'Audiveris"));
            var languageConstant = $"org.audiveris.omr.text.Language.defaultSpecification={options.OcrLanguage}";
            var saveFlag = options.KeepOmr ? " -save" : "";
            await ProcessRunner.RunAsync(audiverisExe,
                $"-batch -export{saveFlag} -output \"{workDir}\" -constant {languageConstant} \"{audiverisInputPath}\"",
                activeProcesses, OnAudiverisLine);

            if (options.KeepOmr)
            {
                var omrPath = Directory.GetFiles(workDir, "*.omr").FirstOrDefault();
                if (omrPath is not null)
                {
                    var omrDestination = Path.ChangeExtension(outputPath, ".omr");
                    File.Copy(omrPath, omrDestination, overwrite: true);
                }
            }

            progress.Report(new ConversionProgress(90, "Fusion des mouvements détectés"));

            var mxlPaths = Directory.GetFiles(workDir, "*.mxl");
            if (mxlPaths.Length == 0)
            {
                throw new FileNotFoundException("Audiveris n'a produit aucun fichier MusicXML (.mxl).");
            }

            var mergedScore = MusicXmlMerger.Merge(mxlPaths);

            var musicXmlPath = Path.Combine(workDir, "merged.musicxml");
            mergedScore.Save(musicXmlPath);

            progress.Report(new ConversionProgress(95, "Export vers MuseScore 4"));
            await RunMuseScoreExportAsync(museScoreExe, musicXmlPath, outputPath, options.StylePath, capturedLog, activeProcesses);

            progress.Report(new ConversionProgress(100, "Terminé"));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    // L'export MuseScore 4 a été observé en échec ponctuel (code retour atypique, aucune sortie
    // console), sans qu'on parvienne à le reproduire en relançant la même commande sur le même
    // fichier — symptôme d'un incident transitoire (ex. verrou furtif d'un antivirus sur le
    // fichier MusicXML temporaire) plutôt qu'un problème de contenu. L'étape étant rapide,
    // on retente quelques fois avant d'abandonner.
    private static async Task RunMuseScoreExportAsync(string museScoreExe, string musicXmlPath,
        string outputPath, string? stylePath, List<string> capturedLog, List<Process> activeProcesses)
    {
        const int maxAttempts = 3;
        var styleArgument = stylePath is null ? "" : $" -S \"{stylePath}\"";
        var arguments = $"\"{musicXmlPath}\"{styleArgument} -o \"{outputPath}\"";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await ProcessRunner.RunAsync(museScoreExe, arguments, activeProcesses, capturedLog.Add);
                return;
            }
            catch (InvalidOperationException) when (attempt < maxAttempts)
            {
                capturedLog.Add($"MuseScore 4 a échoué (tentative {attempt}/{maxAttempts}), nouvel essai...");
                await Task.Delay(1000);
            }
        }
    }

    // Écrit un fichier .log à côté de la destination prévue, avec l'exception complète
    // (message + pile d'appels) et tout ce qu'Audiveris/MuseScore 4 ont affiché avant l'échec.
    public static string WriteErrorLog(string inputPath, string outputPath, Exception ex, IReadOnlyList<string> capturedLog)
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
}
