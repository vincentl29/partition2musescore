using System.IO;
using ImageMagick;
using PDFtoImage;

namespace Partition2MuseScore;

// Rend chaque page d'un PDF en PNG 300 DPI (recommandation du handbook, p.139), puis réassemble le
// tout en un nouveau PDF. Audiveris reçoit ce PDF reconstruit exactement comme l'original : un seul
// Book multi-feuillets, dans l'ordre des pages — ce qui préserve sa détection de mouvements à cheval
// sur plusieurs pages (cf. MusicXmlMerger). Passer les PNG séparément à Audiveris créerait un Book
// distinct par page (vérifié empiriquement) et casserait cette reconstruction.
//
// Contrairement à ImagePreprocessor, on n'applique ici QUE la conversion en niveaux de gris — pas
// son Deskew/Despeckle/GaussianBlur/UnsharpMask. Une page rendue numériquement par PDFium est déjà
// axe-alignée et sans bruit de capture ; ce pipeline est conçu pour des photos/scans physiques
// dégradés (handbook p.139-142), pas pour ce cas. Vérifié empiriquement sur
// `partitions-sources/Ave Maria - Dante Andreo.pdf` : Deskew() y déclenche un bug interne
// d'Audiveris (NullPointerException dans PartwiseBuilder.createDummyPart, "refPart is null") qui
// corrompt l'export et fait échouer l'import MuseScore 4 (code 1320) ; Despeckle/GaussianBlur, sans
// crasher, effacent des liaisons (slurs) fines et dégradent la reconnaissance sans bénéfice sur une
// source déjà propre.
internal static class PdfPagePreprocessor
{
    private const int Dpi = 300;

    public static bool CanPreprocess(string inputPath) =>
        string.Equals(Path.GetExtension(inputPath), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static string Preprocess(string inputPath, string workDir, IProgress<ConversionProgress> progress)
    {
        var pdfBytes = File.ReadAllBytes(inputPath);
        var pageCount = Conversion.GetPageCount(pdfBytes);

        using var collection = new MagickImageCollection();
        for (var page = 0; page < pageCount; page++)
        {
            progress.Report(new ConversionProgress(0,
                $"Extraction et prétraitement de la page {page + 1}/{pageCount}"));

            var pagePngPath = Path.Combine(workDir, $"page_{page + 1:D3}.png");
            Conversion.SavePng(pagePngPath, pdfBytes, page, password: null, options: new RenderOptions(Dpi: Dpi));

            var image = new MagickImage(pagePngPath);
            // PDFtoImage n'écrit aucune métadonnée de résolution dans le PNG : sans cette ligne,
            // Magick.NET suppose 1 px = 1 pt en écrivant le PDF, ce qui produit une page ~4x trop
            // grande en pouces — Audiveris la re-rasterise alors hors de sa limite de 20M pixels.
            image.Density = new Density(Dpi, Dpi, DensityUnit.PixelsPerInch);
            image.ColorType = ColorType.Grayscale;
            collection.Add(image);
        }

        var outputPath = Path.Combine(workDir, "preprocessed.pdf");
        collection.Write(outputPath);
        return outputPath;
    }
}
