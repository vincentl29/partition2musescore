using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Partition2MuseScore;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Process Audiveris/MuseScore 4 actuellement en cours, suivis pour pouvoir les arrêter
    // proprement si l'utilisateur ferme la fenêtre pendant une conversion (sinon ils continuent
    // de tourner en arrière-plan, orphelins, une fois la fenêtre fermée).
    private readonly List<Process> _activeProcesses = [];

    // Frames d'un "spinner" textuel : alterne notes de musique et chiffres binaires pour
    // évoquer ce que fait le pipeline (partition → données binaires → score numérique),
    // plutôt qu'une rotation sans rapport avec le métier de l'appli.
    private static readonly string[] SpinnerFrames = ["♪", "01", "♫", "10", "♩", "11", "♬", "00"];

    // Fait défiler SpinnerFrames sur le TextBlock donné à intervalle fixe ; à arrêter via
    // StopGlyphSpinner une fois l'opération terminée (sinon il continue de tourner pour rien).
    private static DispatcherTimer StartGlyphSpinner(TextBlock target)
    {
        var frameIndex = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) =>
        {
            frameIndex = (frameIndex + 1) % SpinnerFrames.Length;
            target.Text = SpinnerFrames[frameIndex];
        };
        timer.Start();
        return timer;
    }

    private static void StopGlyphSpinner(DispatcherTimer timer) => timer.Stop();

    public MainWindow()
    {
        InitializeComponent();
        _ = LoadOcrLanguagesAsync();
        _ = LoadVersionInfoAsync();
    }

    // Affiche la version installée (registre Windows) et la dernière version publiée sur GitHub
    // pour Audiveris et MuseScore. Lancé une fois au démarrage, sans bloquer l'UI : la requête
    // réseau GitHub peut être lente ou indisponible, et la conversion n'en dépend pas. Si l'un des
    // deux outils est absent (PC neuf, juste après l'installation du Setup.msi) ou a du retard,
    // déclenche une installation/mise à jour winget en arrière-plan (voir ApplyToolActionsAsync)
    // sans attendre qu'elle se termine.
    private async Task LoadVersionInfoAsync()
    {
        var audiveris = await ToolVersionChecker.GetVersionInfoAsync("Audiveris");
        var museScore = await ToolVersionChecker.GetVersionInfoAsync("MuseScore");

        AudiverisVersionText.Text = ToolVersionChecker.Describe("Audiveris", audiveris);
        MuseScoreVersionText.Text = ToolVersionChecker.Describe("MuseScore", museScore);

        var actions = new Dictionary<string, (ToolUpdater.ToolAction Action, ToolVersionChecker.VersionInfo Info)>();
        foreach (var (name, info) in new[] { ("Audiveris", audiveris), ("MuseScore", museScore) })
        {
            if (info.Installed is null)
            {
                actions[name] = (ToolUpdater.ToolAction.Install, info);
            }
            else if (ToolVersionChecker.IsUpgradeAvailable(info))
            {
                actions[name] = (ToolUpdater.ToolAction.Upgrade, info);
            }
        }

        if (actions.Count == 0)
        {
            return;
        }

        // Premier lancement (Audiveris et/ou MuseScore absents, p. ex. juste après le Setup.msi) :
        // le formulaire de conversion ne peut rien faire sans ces outils, donc on le masque
        // derrière un écran d'attente dédié plutôt que de laisser cliquer sur "Convertir" pour un
        // échec garanti. Une simple mise à jour (outil déjà installé, juste périmé) reste en
        // arrière-plan comme avant : l'ancienne version fonctionne encore pendant le téléchargement.
        if (actions.Values.Any(a => a.Action == ToolUpdater.ToolAction.Install))
        {
            await ShowFirstRunInstallScreenAsync(actions);
        }
        else
        {
            _ = ApplyToolActionsAsync(actions);
        }
    }

    // Masque le formulaire de conversion et affiche un écran d'attente (texte explicatif +
    // animation) pendant l'installation initiale via winget, puis bascule de retour vers le
    // formulaire une fois terminé (succès ou échec — en cas d'échec, les TextBlocks de version
    // affichent déjà "installation automatique indisponible", cf. ApplyToolActionsAsync).
    private async Task ShowFirstRunInstallScreenAsync(
        Dictionary<string, (ToolUpdater.ToolAction Action, ToolVersionChecker.VersionInfo Info)> actions)
    {
        MainContentGrid.Visibility = Visibility.Collapsed;
        FirstRunOverlay.Visibility = Visibility.Visible;

        var toolNames = string.Join(" et ", actions.Keys);
        FirstRunStatusText.Text = $"Installation en cours : {toolNames}...";

        var spinnerTimer = StartGlyphSpinner(FirstRunSpinnerGlyph);

        await ApplyToolActionsAsync(actions);

        StopGlyphSpinner(spinnerTimer);
        FirstRunOverlay.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
    }

    // Installe ou met à jour, via winget, les outils repérés comme absents ou périmés
    // ci-dessus. Tourne en arrière-plan : la fenêtre reste utilisable pendant l'opération. Une
    // seule invite UAC apparaît au total (voir ToolUpdater), même si Audiveris et MuseScore sont
    // tous les deux concernés.
    private async Task ApplyToolActionsAsync(
        Dictionary<string, (ToolUpdater.ToolAction Action, ToolVersionChecker.VersionInfo Info)> actions)
    {
        foreach (var (name, (action, _)) in actions)
        {
            var suffix = action == ToolUpdater.ToolAction.Install ? " — installation en cours..." : " — mise à jour en cours...";
            GetVersionTextBlock(name).Text += suffix;
        }

        var results = await ToolUpdater.TryApplyAsync(actions.ToDictionary(kv => kv.Key, kv => kv.Value.Action));

        foreach (var (name, (action, info)) in actions)
        {
            var failureSuffix = action == ToolUpdater.ToolAction.Install
                ? " (installation automatique indisponible)"
                : " (mise à jour automatique indisponible)";

            GetVersionTextBlock(name).Text = results.GetValueOrDefault(name)
                ? ToolVersionChecker.Describe(name, await ToolVersionChecker.GetVersionInfoAsync(name))
                : ToolVersionChecker.Describe(name, info) + failureSuffix;
        }
    }

    private TextBlock GetVersionTextBlock(string toolName) => toolName switch
    {
        "Audiveris" => AudiverisVersionText,
        "MuseScore" => MuseScoreVersionText,
        _ => throw new ArgumentOutOfRangeException(nameof(toolName)),
    };

    // Noms français de tous les codes Tesseract du dépôt tesseract-ocr/tessdata (cf.
    // OcrLanguageCatalog) ; un code absent de ce dictionnaire (nouvelle langue ajoutée au dépôt)
    // s'affiche tel quel (en majuscules) plutôt que de bloquer la liste.
    private static readonly Dictionary<string, string> OcrLanguageNames = new()
    {
        ["afr"] = "Afrikaans",
        ["amh"] = "Amharique",
        ["ara"] = "Arabe",
        ["asm"] = "Assamais",
        ["aze"] = "Azéri",
        ["aze_cyrl"] = "Azéri (cyrillique)",
        ["bel"] = "Biélorusse",
        ["ben"] = "Bengali",
        ["bod"] = "Tibétain",
        ["bos"] = "Bosniaque",
        ["bre"] = "Breton",
        ["bul"] = "Bulgare",
        ["cat"] = "Catalan",
        ["ceb"] = "Cébouano",
        ["ces"] = "Tchèque",
        ["chi_sim"] = "Chinois simplifié",
        ["chi_sim_vert"] = "Chinois simplifié (vertical)",
        ["chi_tra"] = "Chinois traditionnel",
        ["chi_tra_vert"] = "Chinois traditionnel (vertical)",
        ["chr"] = "Cherokee",
        ["cos"] = "Corse",
        ["cym"] = "Gallois",
        ["dan"] = "Danois",
        ["dan_frak"] = "Danois (fraktur)",
        ["deu"] = "Allemand",
        ["deu_frak"] = "Allemand (fraktur)",
        ["deu_latf"] = "Allemand (latin fraktur)",
        ["div"] = "Maldivien",
        ["dzo"] = "Dzongkha",
        ["ell"] = "Grec moderne",
        ["eng"] = "Anglais",
        ["enm"] = "Anglais moyen (historique)",
        ["epo"] = "Espéranto",
        ["equ"] = "Équations mathématiques",
        ["est"] = "Estonien",
        ["eus"] = "Basque",
        ["fao"] = "Féroïen",
        ["fas"] = "Persan",
        ["fil"] = "Filipino",
        ["fin"] = "Finnois",
        ["fra"] = "Français",
        ["frm"] = "Français moyen (historique)",
        ["fry"] = "Frison",
        ["gla"] = "Gaélique écossais",
        ["gle"] = "Irlandais",
        ["glg"] = "Galicien",
        ["grc"] = "Grec ancien",
        ["guj"] = "Gujarati",
        ["hat"] = "Créole haïtien",
        ["heb"] = "Hébreu",
        ["hin"] = "Hindi",
        ["hrv"] = "Croate",
        ["hun"] = "Hongrois",
        ["hye"] = "Arménien",
        ["iku"] = "Inuktitut",
        ["ind"] = "Indonésien",
        ["isl"] = "Islandais",
        ["ita"] = "Italien",
        ["ita_old"] = "Italien (ancien)",
        ["jav"] = "Javanais",
        ["jpn"] = "Japonais",
        ["jpn_vert"] = "Japonais (vertical)",
        ["kan"] = "Kannada",
        ["kat"] = "Géorgien",
        ["kat_old"] = "Géorgien (ancien)",
        ["kaz"] = "Kazakh",
        ["khm"] = "Khmer",
        ["kir"] = "Kirghize",
        ["kmr"] = "Kurde (kurmandji)",
        ["kor"] = "Coréen",
        ["kor_vert"] = "Coréen (vertical)",
        ["lao"] = "Lao",
        ["lat"] = "Latin",
        ["lav"] = "Letton",
        ["lit"] = "Lituanien",
        ["ltz"] = "Luxembourgeois",
        ["mal"] = "Malayalam",
        ["mar"] = "Marathi",
        ["mkd"] = "Macédonien",
        ["mlt"] = "Maltais",
        ["mon"] = "Mongol",
        ["mri"] = "Maori",
        ["msa"] = "Malais",
        ["mya"] = "Birman",
        ["nep"] = "Népalais",
        ["nld"] = "Néerlandais",
        ["nor"] = "Norvégien",
        ["oci"] = "Occitan",
        ["ori"] = "Odia",
        ["pan"] = "Pendjabi",
        ["pol"] = "Polonais",
        ["por"] = "Portugais",
        ["pus"] = "Pachto",
        ["que"] = "Quechua",
        ["ron"] = "Roumain",
        ["rus"] = "Russe",
        ["san"] = "Sanskrit",
        ["sin"] = "Cingalais",
        ["slk"] = "Slovaque",
        ["slk_frak"] = "Slovaque (fraktur)",
        ["slv"] = "Slovène",
        ["snd"] = "Sindhi",
        ["spa"] = "Espagnol",
        ["spa_old"] = "Espagnol (ancien)",
        ["sqi"] = "Albanais",
        ["srp"] = "Serbe",
        ["srp_latn"] = "Serbe (latin)",
        ["sun"] = "Soundanais",
        ["swa"] = "Swahili",
        ["swe"] = "Suédois",
        ["syr"] = "Syriaque",
        ["tam"] = "Tamoul",
        ["tat"] = "Tatar",
        ["tel"] = "Télougou",
        ["tgk"] = "Tadjik",
        ["tgl"] = "Tagalog",
        ["tha"] = "Thaï",
        ["tir"] = "Tigrigna",
        ["ton"] = "Tongien",
        ["tur"] = "Turc",
        ["uig"] = "Ouïghour",
        ["ukr"] = "Ukrainien",
        ["urd"] = "Ourdou",
        ["uzb"] = "Ouzbek",
        ["uzb_cyrl"] = "Ouzbek (cyrillique)",
        ["vie"] = "Vietnamien",
        ["yid"] = "Yiddish",
        ["yor"] = "Yoruba",
    };

    // Propose à la fois les langues déjà installées dans Audiveris (dossier tessdata de
    // Tesseract) et celles disponibles au téléchargement depuis le dépôt GitHub
    // tesseract-ocr/tessdata (cf. OcrLanguageCatalog) : les langues pas encore installées sont
    // marquées "(à télécharger)" et seront récupérées à la volée si l'utilisateur les choisit
    // (cf. Convert_Click / OcrLanguageCatalog.EnsureInstalledAsync).
    private async Task LoadOcrLanguagesAsync()
    {
        var installed = OcrLanguageCatalog.GetInstalledLanguageCodes();
        var available = await OcrLanguageCatalog.GetAvailableLanguageCodesAsync();

        var codes = installed.Union(available)
            .OrderBy(code => OcrLanguageNames.GetValueOrDefault(code, code))
            .ToList();

        if (codes.Count == 0)
        {
            codes = ["eng"];
        }

        OcrLanguageComboBox.Items.Clear();

        if (installed.Contains("fra") && installed.Contains("eng"))
        {
            OcrLanguageComboBox.Items.Add(new ComboBoxItem { Content = "Français + Anglais", Tag = "fra+eng" });
        }

        foreach (var code in codes)
        {
            var name = OcrLanguageNames.GetValueOrDefault(code, code.ToUpperInvariant());
            var suffix = installed.Contains(code) ? "" : " (à télécharger)";
            OcrLanguageComboBox.Items.Add(new ComboBoxItem { Content = name + suffix, Tag = code });
        }

        OcrLanguageComboBox.SelectedIndex = 0;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        Process[] runningProcesses;
        lock (_activeProcesses)
        {
            runningProcesses = _activeProcesses.ToArray();
        }

        if (runningProcesses.Length > 0)
        {
            var result = MessageBox.Show(this,
                "Une conversion est en cours (Audiveris/MuseScore 4). Quitter maintenant interrompra le traitement.\n\nQuitter quand même ?",
                "Conversion en cours", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            foreach (var process in runningProcesses)
            {
                ProcessRunner.TryKill(process);
            }
        }

        base.OnClosing(e);
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir la partition source",
            Filter = "Images, PDF et projets Audiveris (*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.omr)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.omr|Projet Audiveris (*.omr)|*.omr|Tous les fichiers (*.*)|*.*"
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

    private void BrowseStyle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir un style MuseScore",
            Filter = "Style MuseScore (*.mss)|*.mss|Tous les fichiers (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            StylePathTextBox.Text = dialog.FileName;
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

        var spinnerTimer = StartGlyphSpinner(SpinnerGlyph);

        // IProgress<T> capture le SynchronizationContext du thread UI à la construction :
        // Report() peut donc être appelé depuis le thread d'arrière-plan qui lit la sortie
        // d'Audiveris, le callback ci-dessous s'exécute quand même sur le thread UI.
        var progress = new Progress<ConversionProgress>(p =>
        {
            ConversionProgressBar.Value = p.Percent;
            ConversionStatusText.Text = $"{p.Status} — {p.Percent}%";
        });

        var capturedLog = new List<string>();
        var options = new ConversionOptions(
            (string)((ComboBoxItem)OcrLanguageComboBox.SelectedItem).Tag,
            KeepOmrCheckBox.IsChecked == true,
            string.IsNullOrWhiteSpace(StylePathTextBox.Text) ? null : StylePathTextBox.Text);

        try
        {
            await OcrLanguageCatalog.EnsureInstalledAsync(options.OcrLanguage, progress);
            await ScoreConverter.ConvertAsync(inputPath, outputPath, options, progress, capturedLog, _activeProcesses);
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            var logPath = ScoreConverter.WriteErrorLog(inputPath, outputPath, ex, capturedLog);
            MessageBox.Show(this, $"La conversion a échoué :\n{ex.Message}\n\nDétails : {logPath}",
                "Erreur de conversion", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ConvertButton.IsEnabled = true;
            // Arrêter le timer évite qu'il continue de tourner pour rien derrière le panneau masqué.
            StopGlyphSpinner(spinnerTimer);
        }
    }

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

}