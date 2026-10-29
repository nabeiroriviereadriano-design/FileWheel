using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Data;
using FileWheel.Conversion.Providers.Pdf;

namespace FileWheel.Conversion.Providers.Text;

/// <summary>
/// Conversions documentaires integrees (aucun moteur a installer) :
/// Markdown -&gt; HTML/TXT, HTML -&gt; TXT, TXT -&gt; HTML/PDF, images JPEG/PNG -&gt; PDF, EPUB -&gt; HTML/TXT.
/// </summary>
public sealed class BuiltinDocumentProvider : ProviderBase
{
    public override string Id => "builtin-docs";
    public override string DisplayName => "Documents (intégré)";

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } = Build();

    private static List<ConversionCapability> Build()
    {
        var pdfText = new List<OptionDef>
        {
            OptionDef.Int("fontsize", "Taille du texte", 11, 6, 32, "pt"),
            OptionDef.Bool("mono", "Police à chasse fixe (Courier)", false),
            OptionDef.Choice("paper", "Format", "a4", ("a4", "A4"), ("letter", "Lettre US")),
        };
        var pdfImages = new List<OptionDef>
        {
            OptionDef.Choice("page", "Taille des pages", "a4", ("a4", "A4 (orientation automatique)"), ("letter", "Lettre US"), ("image", "Taille de l'image")),
            OptionDef.Int("margin", "Marge", 20, 0, 200, "pt"),
            OptionDef.Int("dpi", "Résolution supposée des images", 96, 36, 1200, "ppp") with { Advanced = true },
        };
        return
        [
            Cap("builtin-docs", ["md"], "html", priority: 10, limitations: "Le HTML brut éventuel est échappé (jamais exécuté)."),
            Cap("builtin-docs", ["md"], "txt", priority: 10),
            Cap("builtin-docs", ["html"], "txt", priority: 10, loss: LossKind.Structure, lossNote: "Seul le texte est conservé (mise en forme, images et liens perdus)."),
            Cap("builtin-docs", ["txt"], "html", priority: 10),
            Cap("builtin-docs", ["txt"], "pdf", options: pdfText, priority: 20,
                limitations: "Polices standard PDF (Helvetica/Courier) : les caractères hors alphabet latin occidental sont remplacés par « ? »."),
            Cap("builtin-docs", ["jpg", "png"], "pdf", options: pdfImages, priority: 10,
                lossNote: "JPEG intégré tel quel (aucune recompression), PNG intégré sans perte."),
            Cap("builtin-docs", ["jpg", "png"], "pdf", ConversionKind.Merge, pdfImages, priority: 10, multi: true, batch: false,
                label: "Assembler les images en un PDF", lossNote: "Une page par image, dans l'ordre de la liste."),
            Cap("builtin-docs", ["epub"], "html", priority: 10, loss: LossKind.Structure,
                lossNote: "Un seul fichier HTML (chapitres à la suite, images incluses). Mise en page d'origine approximative.",
                limitations: "Livres protégés par DRM refusés."),
            Cap("builtin-docs", ["epub"], "txt", priority: 10, loss: LossKind.Structure, lossNote: "Texte seul.", limitations: "Livres protégés par DRM refusés."),
        ];
    }

    public override Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct) =>
        Task.Run(() => Convert(req, progress, ct), ct);

    private static ConversionResult Convert(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var warnings = new List<string>();
        var title = Path.GetFileNameWithoutExtension(req.Input);
        switch ((req.SourceFormat.Id, req.TargetFormat))
        {
            case ("md", "html"):
                TextIO.WriteUtf8(req.TempOutput, Markdown.ToHtmlDocument(TextIO.Read(req.Input).text, title));
                break;
            case ("md", "txt"):
                TextIO.WriteUtf8(req.TempOutput, HtmlText.ToText(Markdown.ToHtml(TextIO.Read(req.Input).text)));
                break;
            case ("html", "txt"):
                TextIO.WriteUtf8(req.TempOutput, HtmlText.ToText(TextIO.Read(req.Input).text));
                break;
            case ("txt", "html"):
                TextIO.WriteUtf8(req.TempOutput, HtmlText.FromText(TextIO.Read(req.Input).text, title));
                break;
            case ("txt", "pdf"):
            {
                var (text, _) = TextIO.Read(req.Input);
                var w = new PdfWriter { Title = title };
                var letter = req.Opt("paper") == "letter";
                w.AddText(text, new PdfWriter.TextOptions(req.OptInt("fontsize", 11), req.OptBool("mono"), letter ? 612 : 595.28, letter ? 792 : 841.89), ct);
                w.Save(req.TempOutput);
                if (w.UnmappableCharacters > 0)
                    warnings.Add($"{w.UnmappableCharacters} caractère(s) non représentables avec les polices standard ont été remplacés par « ? ». Utilisez LibreOffice pour un rendu complet.");
                break;
            }
            case ("jpg" or "png", "pdf"):
            {
                var w = new PdfWriter { Title = title };
                var mode = req.Opt("page") switch { "letter" => PdfWriter.PageMode.Letter, "image" => PdfWriter.PageMode.Image, _ => PdfWriter.PageMode.A4 };
                int n = 0;
                foreach (var input in req.Inputs)
                {
                    ct.ThrowIfCancellationRequested();
                    var fmt = FileWheel.Core.Formats.FormatDetector.Detect(input).Format?.Id;
                    if (fmt is not ("jpg" or "png"))
                        throw new ConversionException(ConversionErrorKind.NotSupported, $"« {Path.GetFileName(input)} » n'est ni un JPEG ni un PNG : utilisez ImageMagick pour les autres formats.");
                    w.AddImage(input, mode, req.OptInt("margin", 20), req.OptInt("dpi", 96));
                    progress.Report(new ConversionProgress((double)++n / req.Inputs.Count, $"Page {n}"));
                }
                w.Save(req.TempOutput);
                break;
            }
            case ("epub", "html"):
                TextIO.WriteUtf8(req.TempOutput, Epub.ToHtml(Epub.Read(req.Input, loadImages: true)));
                break;
            case ("epub", "txt"):
                TextIO.WriteUtf8(req.TempOutput, Epub.ToText(Epub.Read(req.Input, loadImages: false)));
                break;
            default:
                throw new ConversionException(ConversionErrorKind.NotSupported, "Conversion non prise en charge par le moteur intégré.");
        }
        return new ConversionResult([req.TempOutput], warnings);
    }
}
