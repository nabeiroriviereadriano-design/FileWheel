using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Providers.Pdf;
using static FileWheel.Tests.Env;
using static FileWheel.Tests.T;

namespace FileWheel.Tests;

/// <summary>Outils PDF de la roue (PDFium) : sorties relues avec poppler (pdfinfo / pdftotext).</summary>
public static class PdfToolsTests
{
    private static string Out(string name) => Path.Combine(Directory.CreateDirectory(Path.Combine(Work, "outils-pdf")).FullName, name);

    /// <summary>Encodeur JPEG des tests (ImageMagick) ; sous Windows l'application utilise les codecs WPF.</summary>
    public static byte[] MagickJpeg(int w, int h, byte[] rgb, int quality)
    {
        var raw = Path.Combine(Work, $"brut-{Guid.NewGuid():N}.rgb");
        var jpg = Path.ChangeExtension(raw, ".jpg");
        File.WriteAllBytes(raw, rgb);
        try
        {
            Must("convert", "-size", $"{w}x{h}", "-depth", "8", "rgb:" + raw, "-quality", quality.ToString(), jpg);
            return File.ReadAllBytes(jpg);
        }
        finally { File.Delete(raw); if (File.Exists(jpg)) File.Delete(jpg); }
    }

    public static async Task RunAll()
    {
        Suite("Outils PDF (fusionner, pages, rotation, compression)");

        await Run("Fusion de deux PDF : pages additionnées, originaux intacts", () =>
        {
            RequireEngine(EngineIds.Pdfium);
            PdfTools.EnsureEngine(Engine.Dependencies);
            var a = Sample("rapport.pdf");
            var before = Hash(a);
            int na = PdfPages(a);
            var r = PdfTools.Merge([a, a], Out("fusion.pdf"));
            Equal(na * 2, PdfPages(r), "pages");
            Equal(before, Hash(a), "original inchangé");
            var again = PdfTools.Merge([a, a], Out("fusion.pdf"));
            Check(Path.GetFileName(again) == "fusion (2).pdf", "nom libre : " + Path.GetFileName(again));
        });

        await Run("Extraire des pages : seules les pages choisies, dans l'ordre", () =>
        {
            RequireEngine(EngineIds.Pdfium);
            PdfTools.EnsureEngine(Engine.Dependencies);
            var src = PdfTools.Merge([Sample("rapport.pdf"), Sample("rapport.pdf")], Out("source-extraction.pdf"));
            int n = PdfPages(src);
            Check(n >= 2, "au moins deux pages");
            var r = PdfTools.ExtractPages(src, [n, 1], Out("extrait.pdf"));
            Equal(2, PdfPages(r), "pages extraites");
            Throws<ConversionException>(() => PdfTools.ExtractPages(src, [n + 5], Out("x.pdf")), "page inexistante");
        });

        await Run("Supprimer des pages : jamais toutes, texte des autres conservé", () =>
        {
            RequireEngine(EngineIds.Pdfium);
            PdfTools.EnsureEngine(Engine.Dependencies);
            var src = PdfTools.Merge([Sample("rapport.pdf"), Sample("rapport.pdf")], Out("source-suppression.pdf"));
            int n = PdfPages(src);
            var r = PdfTools.DeletePages(src, [1], Out("sans-page-1.pdf"));
            Equal(n - 1, PdfPages(r), "pages restantes");
            Check(PdfText(r).Contains("Rapport annuel"), "texte conservé");
            Throws<ConversionException>(() => PdfTools.DeletePages(src, Enumerable.Range(1, n).ToList(), Out("vide.pdf")), "toutes les pages");
        });

        await Run("Pivoter : rotation enregistrée (90°, puis 180° cumulés)", () =>
        {
            RequireEngine(EngineIds.Pdfium);
            PdfTools.EnsureEngine(Engine.Dependencies);
            var r = PdfTools.Rotate(Sample("rapport.pdf"), null, 1, Out("pivoté.pdf"));
            using (var d = Pdfium.Open(r, null)) Equal(1, d.GetRotation(0), "quart de tour");
            var r2 = PdfTools.Rotate(r, [1], 2, Out("pivoté encore.pdf"));
            using (var d = Pdfium.Open(r2, null)) Equal(3, d.GetRotation(0), "rotations cumulées");
            var info = Must("pdfinfo", r);
            Check(info.Contains("rot:  90") || info.Contains("Page rot:") , "pdfinfo lit le PDF pivoté");
        });

        await Run("Compresser : images recompressées, texte intact, fichier plus léger", async () =>
        {
            RequireEngine(EngineIds.Pdfium);
            if (Which("convert") is null) Skip("ImageMagick absent");
            PdfTools.EnsureEngine(Engine.Dependencies);
            // PDF avec une grande photo JPEG de haute qualite (assemblage integre, sans recompression)
            var photo = Path.Combine(Work, "grande photo.jpg");
            Must("convert", "-size", "2400x1800", "plasma:fractal", "-blur", "0x1", "-quality", "98", photo);
            var r = await Convert(photo, Key("jpg", "pdf"), outDir: Directory.CreateDirectory(Path.Combine(Work, "pdf-photo")).FullName);
            var pdf = r.Outputs[0];
            long before = new FileInfo(pdf).Length;
            var (path, stats) = PdfTools.Compress(pdf, new PdfCompressOptions(60, 96, MagickJpeg) { MinImageBytes = 1024 }, Out("compressé.pdf"));
            long after = new FileInfo(path).Length;
            Check(stats.Recompressed >= 1, $"images recompressées : {stats.Recompressed}/{stats.Images}");
            Check(after < before * 0.7, $"taille {before / 1024} Kio → {after / 1024} Kio");
            Equal(1, PdfPages(path), "pages");
            // L'image est toujours dessinee : rendu non blanc
            Must("pdftoppm", "-r", "20", "-png", "-singlefile", path, Path.Combine(Work, "rendu-compressé"));
            var stat = Must("convert", Path.Combine(Work, "rendu-compressé.png"), "-format", "%[fx:mean]", "info:");
            Check(double.Parse(stat, System.Globalization.CultureInfo.InvariantCulture) < 0.97, "image visible après compression : moyenne " + stat);

            // Document mixte (texte + photo) : le texte reste du texte
            var mixed = PdfTools.Merge([Sample("rapport.pdf"), pdf], Out("mixte.pdf"));
            var (mixedOut, mixedStats) = PdfTools.Compress(mixed, new PdfCompressOptions(60, 96, MagickJpeg) { MinImageBytes = 1024 }, Out("mixte compressé.pdf"));
            Check(mixedStats.Recompressed >= 1, "photo recompressée");
            Check(PdfText(mixedOut).Contains("Rapport annuel"), "texte toujours extractible");
            Check(new FileInfo(mixedOut).Length < new FileInfo(mixed).Length * 0.7, "document mixte plus léger");
        });

        await Run("Compresser un PDF de texte : rien à gagner, PDF valide", () =>
        {
            RequireEngine(EngineIds.Pdfium);
            PdfTools.EnsureEngine(Engine.Dependencies);
            var (path, stats) = PdfTools.Compress(Sample("rapport.pdf"), new PdfCompressOptions(60, 96, MagickJpeg), Out("texte compressé.pdf"));
            Equal(0, stats.Recompressed, "aucune image recompressée");
            Check(PdfText(path).Contains("Rapport annuel"), "texte");
        });

        await Run("Numéros de page : « 1-3, 5 » et retour", () =>
        {
            Equal("1-3, 5, 8-9", PdfTools.FormatPages([9, 1, 2, 3, 5, 8, 2]), "format compact");
            Equal("1,2,3,5", string.Join(",", PdfTools.ParsePages("1-3,5", 9)), "lecture");
        });
    }
}
