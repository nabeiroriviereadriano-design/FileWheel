using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Logging;

namespace FileWheel.Conversion.Providers.Pdf;

/// <summary>
/// Outils PDF de la roue : fusionner, extraire / supprimer des pages, pivoter, compresser les images.
/// Chaque operation ecrit un NOUVEAU fichier (temporaire dans le dossier cible puis renommage vers un nom libre) :
/// les PDF d'origine ne sont jamais modifies.
/// </summary>
public static class PdfTools
{
    public static void EnsureEngine(DependencyManager deps) => Pdfium.EnsureLoaded(deps.Require(EngineIds.Pdfium));

    /// <summary>Fusionne les PDF dans l'ordre donne.</summary>
    public static string Merge(IReadOnlyList<string> inputs, string desiredOutput, CancellationToken ct = default)
    {
        if (inputs.Count < 2) throw new ConversionException(ConversionErrorKind.InvalidInput, "Choisissez au moins deux PDF à fusionner.");
        return WriteNew(desiredOutput, tmp =>
        {
            using var target = new Pdfium.NewDocument();
            foreach (var input in inputs)
            {
                ct.ThrowIfCancellationRequested();
                using var src = Pdfium.Open(input, null);
                target.Import(src, null);
            }
            target.SaveTo(tmp);
        });
    }

    /// <summary>Nouveau PDF contenant seulement les pages indiquees (numeros a partir de 1, dans l'ordre donne).</summary>
    public static string ExtractPages(string input, IReadOnlyList<int> pages, string desiredOutput, string? password = null)
    {
        if (pages.Count == 0) throw new ConversionException(ConversionErrorKind.InvalidOptions, "Aucune page choisie.");
        return WriteNew(desiredOutput, tmp =>
        {
            using var src = Pdfium.Open(input, password);
            Check(pages, src.PageCount);
            using var target = new Pdfium.NewDocument();
            target.Import(src, string.Join(',', pages));
            target.SaveTo(tmp);
        });
    }

    /// <summary>Copie du PDF sans les pages indiquees.</summary>
    public static string DeletePages(string input, IReadOnlyList<int> pages, string desiredOutput, string? password = null)
    {
        if (pages.Count == 0) throw new ConversionException(ConversionErrorKind.InvalidOptions, "Aucune page choisie.");
        return WriteNew(desiredOutput, tmp =>
        {
            using var src = Pdfium.Open(input, password);
            int count = src.PageCount;
            Check(pages, count);
            var distinct = pages.Distinct().OrderByDescending(p => p).ToList();
            if (distinct.Count >= count) throw new ConversionException(ConversionErrorKind.InvalidOptions, "Impossible de supprimer toutes les pages : il doit en rester au moins une.");
            foreach (var p in distinct) src.DeletePage(p - 1);
            src.SaveTo(tmp);
        });
    }

    /// <summary>Pivote les pages indiquees (toutes si null) de quarts de tour dans le sens horaire (1 = 90°, 2 = 180°, 3 = 90° a gauche).</summary>
    public static string Rotate(string input, IReadOnlyList<int>? pages, int quarterTurns, string desiredOutput, string? password = null)
    {
        return WriteNew(desiredOutput, tmp =>
        {
            using var src = Pdfium.Open(input, password);
            int count = src.PageCount;
            var list = pages ?? Enumerable.Range(1, count).ToList();
            Check(list, count);
            foreach (var p in list.Distinct()) src.Rotate(p - 1, quarterTurns);
            src.SaveTo(tmp);
        });
    }

    /// <summary>Recompresse les images du PDF (le texte et les dessins restent vectoriels).</summary>
    public static (string Path, PdfImageStats Stats) Compress(string input, PdfCompressOptions options, string desiredOutput,
        IProgress<ConversionProgress>? progress = null, CancellationToken ct = default, string? password = null)
    {
        var total = new PdfImageStats();
        var path = WriteNew(desiredOutput, tmp =>
        {
            using var src = Pdfium.Open(input, password);
            int count = src.PageCount;
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new ConversionProgress((double)i / count, $"Page {i + 1} / {count}"));
                var s = src.CompressImages(i, options);
                total.Images += s.Images;
                total.Recompressed += s.Recompressed;
                total.BytesBefore += s.BytesBefore;
                total.BytesAfter += s.BytesAfter;
            }
            src.SaveTo(tmp);
        });
        Log.Info($"PDF compressé : {total.Recompressed}/{total.Images} image(s) recompressée(s), {total.BytesBefore / 1024} → {total.BytesAfter / 1024} Kio");
        return (path, total);
    }

    /// <summary>Texte « 1-3, 5 » → numeros de page.</summary>
    public static List<int> ParsePages(string spec, int pageCount) => PdfiumProvider.ParseRange(spec, pageCount);

    /// <summary>Numeros de page → texte compact « 1-3, 5 ».</summary>
    public static string FormatPages(IEnumerable<int> pages)
    {
        var sorted = pages.Distinct().OrderBy(p => p).ToList();
        var parts = new List<string>();
        for (int i = 0; i < sorted.Count; i++)
        {
            int a = sorted[i], b = a;
            while (i + 1 < sorted.Count && sorted[i + 1] == b + 1) b = sorted[++i];
            parts.Add(a == b ? a.ToString() : $"{a}-{b}");
        }
        return string.Join(", ", parts);
    }

    private static void Check(IEnumerable<int> pages, int count)
    {
        foreach (var p in pages)
            if (p < 1 || p > count)
                throw new ConversionException(ConversionErrorKind.InvalidOptions, $"La page {p} n'existe pas (le document a {count} page(s)).");
    }

    private static string WriteNew(string desiredOutput, Action<string> write)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(desiredOutput))!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".~fw-{Guid.NewGuid():N}.tmp");
        try
        {
            write(tmp);
            var head = new byte[5];
            using (var fs = File.OpenRead(tmp))
            {
                if (fs.Length < 64 || fs.Read(head, 0, 5) != 5 || System.Text.Encoding.ASCII.GetString(head) != "%PDF-")
                    throw new ConversionException(ConversionErrorKind.WriteError, "Le PDF produit est invalide.");
            }
            var final = NameConflict.UniquePath(Path.GetFullPath(desiredOutput));
            File.Move(tmp, final);
            return final;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}
