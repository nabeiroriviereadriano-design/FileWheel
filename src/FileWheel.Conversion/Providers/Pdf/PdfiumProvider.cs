using System.Text;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Providers.Data;
using FileWheel.Conversion.Providers.Images;
using FileWheel.Core.FileOperations;

namespace FileWheel.Conversion.Providers.Pdf;

/// <summary>
/// PDF via PDFium : pages en images PNG (ou JPEG avec ImageMagick), extraction du texte.
/// L'extraction de texte ne fait pas de reconnaissance optique : un PDF scanne (images) donne
/// un texte vide, ce qui est signale.
/// </summary>
public sealed class PdfiumProvider : ProviderBase
{
    private readonly DependencyManager _deps;

    public PdfiumProvider(DependencyManager deps) => _deps = deps;

    public override string Id => "pdfium";
    public override string DisplayName => "PDFium (PDF)";

    private static readonly OptionDef Pages = OptionDef.Text("pages", "Pages", "", "Vide = toutes. Exemples : 1-3, 5, 8-");
    private static readonly OptionDef Password = OptionDef.Text("password", "Mot de passe du PDF (s'il est protégé)") with { Advanced = true };

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } =
    [
        Cap("pdfium", ["pdf"], "png", ConversionKind.PdfPages,
            [OptionDef.Int("dpi", "Résolution", 150, 36, 600, "ppp"), Pages, Password], engine: EngineIds.Pdfium, folder: true, priority: 10,
            label: "Pages en images PNG", lossNote: "Rendu sans perte à la résolution choisie (le texte devient une image)."),
        Cap("pdfium", ["pdf"], "jpg", ConversionKind.PdfPages,
            [OptionDef.Int("dpi", "Résolution", 150, 36, 600, "ppp"), OptionDef.Int("quality", "Qualité JPEG", 90, 1, 100, "%"), Pages, Password],
            loss: LossKind.Lossy, engine: EngineIds.Pdfium, extraEngines: [EngineIds.ImageMagick], folder: true, priority: 10, label: "Pages en images JPEG"),
        Cap("pdfium", ["pdf"], "txt", ConversionKind.ExtractText, [Pages, Password], LossKind.Structure,
            "Extraction du texte : mise en page, images et tableaux non conservés.",
            "Sans reconnaissance optique (OCR) : un PDF scanné ne contient pas de texte extractible.",
            engine: EngineIds.Pdfium, priority: 10, label: "Extraire le texte (TXT)"),
    ];

    /// <summary>Nombre de pages (apercu dans le convertisseur).</summary>
    public int? PageCount(string path)
    {
        var lib = _deps.PathOf(EngineIds.Pdfium);
        if (lib is null) return null;
        try
        {
            Pdfium.EnsureLoaded(lib);
            using var d = Pdfium.Open(path, null);
            return d.PageCount;
        }
        catch { return null; }
    }

    public static List<int> ParseRange(string spec, int pageCount)
    {
        var pages = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(spec))
        {
            for (int i = 1; i <= pageCount; i++) pages.Add(i);
            return pages.ToList();
        }
        foreach (var part0 in spec.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var part = part0.Trim();
            if (part.Contains('-'))
            {
                var ab = part.Split('-', 2);
                int a = ab[0].Trim().Length == 0 ? 1 : int.Parse(ab[0].Trim());
                int b = ab[1].Trim().Length == 0 ? pageCount : int.Parse(ab[1].Trim());
                if (a < 1 || b < a) throw new ConversionException(ConversionErrorKind.InvalidOptions, $"Plage de pages invalide : « {part} ».");
                for (int i = a; i <= Math.Min(b, pageCount); i++) pages.Add(i);
            }
            else
            {
                if (!int.TryParse(part, out var p) || p < 1) throw new ConversionException(ConversionErrorKind.InvalidOptions, $"Numéro de page invalide : « {part} ».");
                if (p <= pageCount) pages.Add(p);
            }
        }
        if (pages.Count == 0) throw new ConversionException(ConversionErrorKind.InvalidOptions, $"Aucune des pages demandées n'existe (le document a {pageCount} page(s)).");
        return pages.ToList();
    }

    public override async Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var lib = _deps.Require(EngineIds.Pdfium);
        Pdfium.EnsureLoaded(lib);
        var pwd = req.Opt("password");
        var warnings = new List<string>();
        using var doc = await Task.Run(() => Pdfium.Open(req.Input, string.IsNullOrEmpty(pwd) ? null : pwd), ct).ConfigureAwait(false);
        int count = doc.PageCount;
        if (count <= 0) throw new ConversionException(ConversionErrorKind.Corrupt, "Le PDF ne contient aucune page.");
        var pages = ParseRange(req.Opt("pages"), count);

        if (req.Capability.Kind == ConversionKind.ExtractText)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var p in pages)
            {
                ct.ThrowIfCancellationRequested();
                var t = await Task.Run(() => doc.ExtractText(p - 1), ct).ConfigureAwait(false);
                if (pages.Count > 1) sb.Append($"--- Page {p} ---\n");
                sb.Append(t.Replace("\r\n", "\n").Replace('\r', '\n')).Append("\n\n");
                progress.Report(new ConversionProgress((double)++n / pages.Count, $"Page {p} / {count}"));
            }
            var text = sb.ToString();
            if (text.Count(char.IsLetterOrDigit) < pages.Count * 5)
                warnings.Add("Très peu de texte trouvé : ce PDF est probablement une numérisation (images). L'OCR n'est pas inclus dans cette version.");
            TextIO.WriteUtf8(req.TempOutput, text);
            return new ConversionResult([req.TempOutput], warnings);
        }

        int dpi = req.OptInt("dpi", 150);
        var stem = PathSafety.SanitizeFileName(Path.GetFileNameWithoutExtension(req.Input));
        int digits = Math.Max(2, count.ToString().Length);
        int done = 0;
        string? magick = req.TargetFormat == "jpg" ? _deps.Require(EngineIds.ImageMagick) : null;
        foreach (var p in pages)
        {
            ct.ThrowIfCancellationRequested();
            var (w, h, rgba) = await Task.Run(() => doc.Render(p - 1, dpi), ct).ConfigureAwait(false);
            var png = Path.Combine(magick is null ? req.TempOutput : req.WorkDir, $"{stem} - page {p.ToString().PadLeft(digits, '0')}.png");
            await using (var fs = new FileStream(png, FileMode.CreateNew))
                PngCodec.Encode(fs, w, h, rgba, keepAlpha: false);
            if (magick is not null)
            {
                var jpg = Path.Combine(req.TempOutput, Path.GetFileNameWithoutExtension(png) + ".jpg");
                var r = await ProcessRunner.RunAsync(magick, ["PNG:" + png, "-quality", req.OptInt("quality", 90).ToString(), "JPEG:" + jpg], ct).ConfigureAwait(false);
                File.Delete(png);
                if (r.ExitCode != 0) throw new ConversionException(ConversionErrorKind.EngineCrashed, "ImageMagick n'a pas pu écrire le JPEG.", r.StdErrTail);
            }
            progress.Report(new ConversionProgress((double)++done / pages.Count, $"Page {p} / {count}"));
        }
        return new ConversionResult([req.TempOutput], warnings);
    }
}
