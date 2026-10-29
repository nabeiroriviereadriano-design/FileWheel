using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Providers.Data;
using FileWheel.Conversion.Providers.Pdf;
using FileWheel.Conversion.Providers.Text;

namespace FileWheel.Conversion.Providers.Office;

/// <summary>
/// Documents, classeurs et presentations via LibreOffice en mode sans interface (programme externe).
/// Chaque conversion utilise un profil LibreOffice temporaire et prive : aucune interference avec
/// un LibreOffice ouvert par l'utilisateur, et plusieurs conversions peuvent tourner en parallele.
/// </summary>
public sealed class LibreOfficeProvider : ProviderBase
{
    private readonly DependencyManager _deps;
    private readonly PdfiumProvider? _pdfium;

    public LibreOfficeProvider(DependencyManager deps, PdfiumProvider? pdfium)
    {
        _deps = deps;
        _pdfium = pdfium;
    }

    public override string Id => "libreoffice";
    public override string DisplayName => "LibreOffice (documents)";

    private static readonly string[] Writer = ["doc", "docx", "odt", "rtf", "txt", "html"];
    private static readonly string[] Calc = ["xls", "xlsx", "ods", "csv"];
    private static readonly string[] Impress = ["ppt", "pptx", "odp"];

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } = Build();

    private static List<ConversionCapability> Build()
    {
        const string E = EngineIds.LibreOffice;
        const string layout = "Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées.";
        var caps = new List<ConversionCapability>();
        caps.Add(Cap("libreoffice", Writer.Concat(Calc).Concat(Impress).Append("md"), "pdf", engine: E, priority: 20, limitations: layout,
            options: [OptionDef.Bool("pdfa", "PDF/A (archivage)", false) with { Advanced = true }]));
        foreach (var to in new[] { "docx", "odt", "rtf", "doc", "html", "txt", "epub" })
            caps.Add(Cap("libreoffice", Writer.Where(f => f != to).Append("md"), to, engine: E, priority: to is "txt" or "html" ? 40 : 20,
                loss: to is "txt" ? LossKind.Structure : LossKind.None, lossNote: to == "txt" ? "Texte seul : la mise en forme est perdue." : null,
                limitations: layout));
        foreach (var to in new[] { "xlsx", "ods", "csv", "xls" })
            caps.Add(Cap("libreoffice", Calc.Where(f => f != to), to, engine: E, priority: to == "csv" ? 30 : 20,
                lossNote: to == "csv" ? "CSV : seule la première feuille est exportée, sans formules ni mise en forme." : null, limitations: layout));
        foreach (var to in new[] { "pptx", "odp", "ppt" })
            caps.Add(Cap("libreoffice", Impress.Where(f => f != to), to, engine: E, priority: 20, limitations: layout));
        caps.Add(Cap("libreoffice", Impress, "png", ConversionKind.PdfPages, [OptionDef.Int("dpi", "Résolution", 150, 36, 600, "ppp")],
            engine: E, extraEngines: [EngineIds.Pdfium], folder: true, priority: 20, label: "Diapositives en images PNG",
            lossNote: "Chaque diapositive devient une image (animations perdues)."));
        caps.Add(Cap("libreoffice", ["pdf"], "docx", engine: E, priority: 30, loss: LossKind.Structure, label: "Reconstruire en DOCX modifiable",
            lossNote: "Reconstruction approximative : le texte est placé dans des zones positionnées. Un PDF complexe (colonnes, tableaux) ne sera pas fidèle.",
            limitations: "PDF scanné : pas de texte reconnu (pas d'OCR)."));
        caps.Add(Cap("libreoffice", ["pdf"], "odt", engine: E, priority: 30, loss: LossKind.Structure, label: "Reconstruire en ODT modifiable",
            lossNote: "Reconstruction approximative (zones de texte positionnées)."));
        return caps;
    }

    private static (string convertTo, string? infilter) Filter(string src, string to) => (src, to) switch
    {
        ("pdf", "docx") => ("docx:MS Word 2007 XML", "writer_pdf_import"),
        ("pdf", "odt") => ("odt", "writer_pdf_import"),
        (_, "pdf") when src is "html" or "md" => ("pdf:writer_pdf_Export", "HTML (StarWriter)"),
        ("html" or "md", _) => (Filter("odt", to).convertTo, "HTML (StarWriter)"),
        (_, "pdf") => ("pdf", null),
        (_, "docx") => ("docx:MS Word 2007 XML", null),
        (_, "doc") => ("doc:MS Word 97", null),
        (_, "odt") => ("odt", null),
        (_, "rtf") => ("rtf", null),
        (_, "txt") => ("txt:Text (encoded):UTF8", null),
        (_, "html") => ("html:XHTML Writer File:UTF8", null),
        (_, "epub") => ("epub", null),
        (_, "xlsx") => ("xlsx:Calc MS Excel 2007 XML", null),
        (_, "xls") => ("xls:MS Excel 97", null),
        (_, "ods") => ("ods", null),
        (_, "csv") => ("csv:Text - txt - csv (StarCalc):44,34,76,1", null),
        (_, "pptx") => ("pptx:Impress MS PowerPoint 2007 XML", null),
        (_, "ppt") => ("ppt:MS PowerPoint 97", null),
        (_, "odp") => ("odp", null),
        _ => throw new ConversionException(ConversionErrorKind.NotSupported, "Conversion LibreOffice inconnue."),
    };

    public override async Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var soffice = _deps.Require(EngineIds.LibreOffice);
        var warnings = new List<string>();
        var src = req.SourceFormat.Id;
        var input = req.Input;

        // Markdown : rendu HTML integre puis LibreOffice
        if (src == "md")
        {
            var html = Path.Combine(req.WorkDir, "document.html");
            TextIO.WriteUtf8(html, Markdown.ToHtmlDocument(TextIO.Read(req.Input).text, Path.GetFileNameWithoutExtension(req.Input)));
            input = html;
            src = "html";
        }

        bool slides = req.Capability.Kind == ConversionKind.PdfPages;
        var target = slides ? "pdf" : req.TargetFormat;
        var (convertTo, infilter) = Filter(src, target);
        if (target == "pdf" && req.OptBool("pdfa"))
            convertTo = "pdf:" + (src is "xls" or "xlsx" or "ods" or "csv" ? "calc_pdf_Export" : src is "ppt" or "pptx" or "odp" ? "impress_pdf_Export" : "writer_pdf_Export") + ":{\"SelectPdfVersion\":{\"type\":\"long\",\"value\":\"2\"}}";

        var outDir = Path.Combine(req.WorkDir, "sortie");
        Directory.CreateDirectory(outDir);
        var profile = new Uri(Path.Combine(req.WorkDir, "profil-lo") + Path.DirectorySeparatorChar).AbsoluteUri;
        // Copie sous un nom neutre : LibreOffice derive le nom de sortie du nom d'entree
        var ext = Path.GetExtension(input);
        var neutral = Path.Combine(req.WorkDir, "entree" + (ext.Length > 0 ? ext : "." + src));
        if (!string.Equals(input, neutral, StringComparison.OrdinalIgnoreCase)) File.Copy(input, neutral, overwrite: true);

        var args = new List<string> { "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck", "--nofirststartwizard", "-env:UserInstallation=" + profile };
        if (infilter is not null) args.Add("--infilter=" + infilter);
        if (src == "csv") args.Add("--infilter=CSV:44,34,76,1");
        args.AddRange(["--convert-to", convertTo, "--outdir", outDir, neutral]);
        progress.Report(new ConversionProgress(null, "LibreOffice convertit le document…"));
        var r = await ProcessRunner.RunAsync(soffice, args, ct, workingDir: req.WorkDir, timeout: TimeSpan.FromMinutes(20)).ConfigureAwait(false);
        var produced = Directory.EnumerateFiles(outDir).FirstOrDefault();
        if (produced is null || new FileInfo(produced).Length == 0)
        {
            var text = r.StdErrTail + r.StdOutTail;
            if (text.Contains("password", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.Protected, "Document protégé par mot de passe : LibreOffice ne peut pas l'ouvrir sans interface.");
            if (text.Contains("Error: source file could not be loaded", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.Corrupt, "LibreOffice ne parvient pas à ouvrir ce document (endommagé ou format non reconnu).", text);
            if (text.Contains("no export filter", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.NotSupported, "LibreOffice ne sait pas exporter ce document dans ce format.", text);
            throw new ConversionException(ConversionErrorKind.EngineCrashed, $"LibreOffice n'a produit aucun fichier (code {r.ExitCode}).", ProcessRunner.LastLines(text, 4));
        }

        if (!slides)
        {
            File.Move(produced, req.TempOutput);
            if (target == "csv") warnings.Add("Seule la première feuille du classeur a été exportée.");
            return new ConversionResult([req.TempOutput], warnings);
        }

        // Diapositives : PDF intermediaire puis rendu PDFium
        if (_pdfium is null) throw new ConversionException(ConversionErrorKind.EngineMissing, "PDFium est nécessaire pour les images de diapositives.");
        var sub = new ConversionRequest
        {
            Inputs = [produced], SourceFormat = FileWheel.Core.Formats.FormatCatalog.Require("pdf"),
            Capability = _pdfium.Capabilities.First(c => c.To == "png" && c.Kind == ConversionKind.PdfPages),
            Options = new Dictionary<string, string> { ["dpi"] = req.OptInt("dpi", 150).ToString() },
            TempOutput = req.TempOutput, WorkDir = req.WorkDir, FinalOutput = req.FinalOutput,
        };
        var res = await _pdfium.ConvertAsync(sub, progress, ct).ConfigureAwait(false);
        // Noms : « Presentation - page 01.png » -> « Presentation - diapo 01.png »
        var stem = FileWheel.Core.FileOperations.PathSafety.SanitizeFileName(Path.GetFileNameWithoutExtension(req.Input));
        foreach (var f in Directory.GetFiles(req.TempOutput))
        {
            var name = Path.GetFileName(f).Replace("entree - page", stem + " - diapo");
            if (name != Path.GetFileName(f)) File.Move(f, Path.Combine(req.TempOutput, name));
        }
        return new ConversionResult([req.TempOutput], res.Warnings);
    }
}
