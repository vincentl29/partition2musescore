using System.IO;
using ImageMagick;

namespace Partition2MuseScore;

// Nettoie une image avant Audiveris, en suivant les recommandations du handbook (p.139-142) pour
// les scans/photos de qualité moyenne : niveaux de gris, redressement, débruitage, net renforcé.
// Cleanup() est aussi réutilisé par PdfPagePreprocessor pour appliquer le même traitement à chaque
// page d'un PDF, rendue séparément en PNG 300 DPI avant d'être réassemblée pour Audiveris.
internal static class ImagePreprocessor
{
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"];

    public static bool CanPreprocess(string inputPath) =>
        SupportedExtensions.Contains(Path.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase);

    public static string Preprocess(string inputPath, string workDir)
    {
        using var image = new MagickImage(inputPath);
        Cleanup(image);

        var outputPath = Path.Combine(workDir, "preprocessed" + Path.GetExtension(inputPath));
        image.Write(outputPath);
        return outputPath;
    }

    public static void Cleanup(MagickImage image)
    {
        image.BackgroundColor = MagickColors.White;
        image.AutoOrient();
        image.ColorType = ColorType.Grayscale;
        image.Deskew(new Percentage(40));
        image.Despeckle();
        image.GaussianBlur(0, 1.75);
        image.UnsharpMask(0, 1.0);
    }
}
