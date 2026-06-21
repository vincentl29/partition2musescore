using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Partition2MuseScore;

// Audiveris exporte un .mxl par "mouvement" détecté (système indenté = nouveau mouvement pour
// lui). Sur un scan multi-pages d'une seule partition, ça produit souvent plusieurs fichiers
// qu'il faut recoller en un seul score continu avant de les passer à MuseScore 4.
internal static class MusicXmlMerger
{
    // Trie les fichiers par numéro de mouvement puis les fusionne en un seul document MusicXML.
    public static XDocument Merge(IEnumerable<string> mxlPaths)
    {
        var orderedPaths = mxlPaths.OrderBy(GetMovementNumber);
        return MergeMovements(orderedPaths.Select(ExtractScorePartwise).ToList());
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

    private sealed record PartInfo(string Key, XElement ScorePart, XElement Part);

    private sealed record MergedPart(string Key, XElement Element);

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

        var parts = BuildPartInfos(partList, root)
            .Select(info => new MergedPart(info.Key, info.Part))
            .ToList();

        foreach (var movement in movements.Skip(1))
        {
            var movementRoot = movement.Root!;
            var movementPartList = movementRoot.Element("part-list")!;
            var movementInfos = BuildPartInfos(movementPartList, movementRoot);
            var matchedKeys = new HashSet<string>();

            foreach (var info in movementInfos)
            {
                matchedKeys.Add(info.Key);

                var existing = parts.FirstOrDefault(p => p.Key == info.Key);
                if (existing is null)
                {
                    existing = AddNewPart(root, partList, parts, info);
                    parts.Add(existing);
                }

                var measureNumber = existing.Element.Elements("measure").Count();
                foreach (var measure in info.Part.Elements("measure"))
                {
                    measureNumber++;
                    measure.SetAttributeValue("number", measureNumber.ToString());
                    existing.Element.Add(measure);
                }
            }

            // Parties déjà connues mais absentes de ce mouvement (ex. les voix pas encore
            // entrées) : on bouche le trou avec des silences pour garder tout aligné.
            var reference = movementInfos.Count > 0 ? movementInfos[0].Part.Elements("measure").ToList() : [];
            foreach (var part in parts.Where(p => !matchedKeys.Contains(p.Key)))
            {
                var measureNumber = part.Element.Elements("measure").Count();
                foreach (var rest in BuildRestMeasures(reference, measureNumber + 1))
                {
                    part.Element.Add(rest);
                }
            }
        }

        ReorderPartsVoicesFirst(root, partList, parts);

        return merged;
    }

    // Convention de partition chorale : les voix se lisent au-dessus de l'accompagnement
    // instrumental. Audiveris exporte ses parties dans l'ordre où il les détecte sur la page
    // (souvent le piano avant les voix qui n'entrent qu'ensuite), donc on les replace ici en
    // tête, en conservant leur ordre relatif d'origine au sein de chaque groupe (OrderBy est
    // un tri stable).
    private static void ReorderPartsVoicesFirst(XElement root, XElement partList, List<MergedPart> parts)
    {
        var scoreParts = partList.Elements("score-part").ToList();
        XElement ScorePartFor(MergedPart part) =>
            scoreParts.First(sp => (string)sp.Attribute("id")! == (string)part.Element.Attribute("id")!);

        var ordered = parts.OrderBy(p => IsVocalPart(ScorePartFor(p), p.Element) ? 0 : 1).ToList();
        if (ordered.SequenceEqual(parts))
        {
            return;
        }

        partList.ReplaceNodes(ordered.Select(ScorePartFor));

        root.Elements("part").Remove();
        foreach (var part in ordered)
        {
            root.Add(part.Element);
        }
    }

    // Reconnaît une partie vocale via le signal le plus fiable observé empiriquement chez
    // Audiveris : il assigne toujours le patch GM "Voice Oohs" (programme 54) à une portée
    // qu'il a identifiée comme un chant, indépendamment du nom OCR ("S.", "A.", "Voice"...).
    // Le motif sur part-name/part-abbreviation et la présence de <lyric> servent de filets de
    // sécurité si ce signal venait à manquer (autre version d'Audiveris, etc.).
    private static readonly Regex VocalNamePattern = new(
        @"^(voix?|voice|chant|cho?eur|choir|vocal(?:e?s)?|soprano|mezzo([\s-]?soprano)?|alto|c[oô]ntralto|t[ée]nor|bar[yi]ton|bass?e?|[satb]\.?)$",
        RegexOptions.IgnoreCase);

    private static bool IsVocalPart(XElement scorePart, XElement part)
    {
        var instrumentName = (string?)scorePart.Element("score-instrument")?.Element("instrument-name") ?? "";
        if (instrumentName.Contains("voice", StringComparison.OrdinalIgnoreCase)
            || instrumentName.Contains("choir", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var partName = ((string?)scorePart.Element("part-name") ?? "").Trim();
        var abbreviation = ((string?)scorePart.Element("part-abbreviation") ?? "").Trim();
        if (VocalNamePattern.IsMatch(partName) || VocalNamePattern.IsMatch(abbreviation))
        {
            return true;
        }

        return part.Descendants("lyric").Any();
    }

    // Audiveris peut nommer plusieurs parties identiquement (ex. deux voix nommées "Voice" quand
    // l'OCR ne peut pas lire leurs abréviations "S."/"A." faute de langue installée) : se fier au
    // seul nom casserait la fusion en confondant deux instruments différents. On construit donc
    // une clé qui ajoute un suffixe (#2, #3...) à la N-ième occurrence d'un même nom, pour garder
    // chaque partie distincte même quand Audiveris ne peut pas les différencier par leur nom.
    private static List<PartInfo> BuildPartInfos(XElement partList, XElement root)
    {
        var occurrences = new Dictionary<string, int>();
        var result = new List<PartInfo>();

        foreach (var part in root.Elements("part"))
        {
            var id = (string)part.Attribute("id")!;
            var scorePart = partList.Elements("score-part").First(sp => (string)sp.Attribute("id")! == id);
            var name = (string?)scorePart.Element("part-name") ?? id;

            var occurrence = occurrences.GetValueOrDefault(name);
            occurrences[name] = occurrence + 1;
            var key = occurrence == 0 ? name : $"{name}#{occurrence + 1}";

            result.Add(new PartInfo(key, scorePart, part));
        }

        return result;
    }

    // Une partie qui apparaît seulement à partir d'un mouvement ultérieur (ex. une voix qui
    // n'entre qu'après l'intro) : on la crée et on la fait démarrer par des silences couvrant
    // tout ce qui a déjà été fusionné, pour rester synchronisée avec les parties existantes.
    private static MergedPart AddNewPart(XElement root, XElement partList, List<MergedPart> parts, PartInfo info)
    {
        var newPartId = $"P{parts.Count + 1}";

        var scorePart = new XElement(info.ScorePart);
        scorePart.SetAttributeValue("id", newPartId);
        partList.Add(scorePart);

        var partElement = new XElement("part", new XAttribute("id", newPartId));
        root.Add(partElement);

        var reference = parts.Count > 0 ? parts[0].Element.Elements("measure").ToList() : [];
        foreach (var rest in BuildRestMeasures(reference, 1))
        {
            partElement.Add(rest);
        }

        return new MergedPart(info.Key, partElement);
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
}
