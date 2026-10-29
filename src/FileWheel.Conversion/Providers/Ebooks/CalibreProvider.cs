using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Providers.Text;

namespace FileWheel.Conversion.Providers.Ebooks;

/// <summary>
/// Livres numeriques via Calibre (« ebook-convert », programme externe).
/// Les livres proteges (DRM) sont detectes AVANT conversion et refuses : FileWheel ne cherche
/// jamais a contourner une protection.
/// </summary>
public sealed class CalibreProvider : ProviderBase
{
    private readonly DependencyManager _deps;

    public CalibreProvider(DependencyManager deps) => _deps = deps;

    public override string Id => "calibre";
    public override string DisplayName => "Calibre (livres numériques)";

    private static readonly string[] Sources = ["epub", "mobi", "azw3", "fb2", "docx", "odt", "rtf", "txt", "html", "pdf"];

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } =
    [
        .. new[] { "epub", "azw3", "mobi", "pdf", "txt", "fb2", "docx" }.Select(to =>
            Cap("calibre", Sources.Where(s => s != to), to, ConversionKind.Convert,
                to == "pdf"
                    ? [OptionDef.Choice("paper", "Format de page", "a5", ("a4", "A4"), ("a5", "A5 (liseuse)"), ("letter", "Lettre US")), OptionDef.Int("fontsize", "Taille du texte", 12, 6, 36, "pt")]
                    : [],
                LossKind.Structure,
                to switch
                {
                    "txt" => "Texte seul : images et mise en forme perdues.",
                    "pdf" => "Mise en page recalculée : pagination différente de l'original.",
                    _ => "Mise en page adaptée par Calibre : polices et éléments interactifs peuvent changer.",
                },
                "Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte.",
                engine: EngineIds.Calibre, priority: to is "epub" or "azw3" or "mobi" or "fb2" ? 20 : 40)),
    ];

    public override ConversionAnalysis Analyze(ConversionRequest request)
    {
        var a = base.Analyze(request);
        if (DrmCheck(request.Input, request.SourceFormat.Id) is { } drm)
        {
            a.Warnings.Add(drm);
            a.Blocking = true;
        }
        return a;
    }

    /// <summary>Message si le livre est protege, sinon null.</summary>
    public static string? DrmCheck(string path, string format)
    {
        try
        {
            if (format == "epub")
            {
                using var zip = ZipFile.OpenRead(path);
                return Epub.IsDrmProtected(zip, out var reason) ? reason : null;
            }
            if (format is "mobi" or "azw3")
            {
                using var fs = File.OpenRead(path);
                var head = new byte[90];
                if (fs.Read(head, 0, head.Length) < 86) return null;
                uint rec0 = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(78));
                if (rec0 + 14 > fs.Length) return null;
                fs.Position = rec0 + 12;
                var enc = new byte[2];
                fs.ReadExactly(enc);
                int type = BinaryPrimitives.ReadUInt16BigEndian(enc);
                return type != 0 ? "Ce livre Kindle/Mobipocket est protégé par un DRM : FileWheel ne contourne pas les protections." : null;
            }
        }
        catch { }
        return null;
    }

    public override async Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var exe = _deps.Require(EngineIds.Calibre);
        if (DrmCheck(req.Input, req.SourceFormat.Id) is { } drm) throw new ConversionException(ConversionErrorKind.Protected, drm);
        // ebook-convert deduit les formats des extensions : nom d'entree neutre avec la bonne extension
        var input = Path.Combine(req.WorkDir, "livre." + FileWheel.Core.Formats.FormatCatalog.OutputExtension(req.SourceFormat.Id));
        File.Copy(req.Input, input, overwrite: true);
        var output = Path.Combine(req.WorkDir, "sortie." + FileWheel.Core.Formats.FormatCatalog.OutputExtension(req.TargetFormat));
        var args = new List<string> { input, output };
        if (req.TargetFormat == "pdf")
        {
            args.AddRange(["--paper-size", req.Opt("paper") switch { "a4" => "a4", "letter" => "letter", _ => "a5" }]);
            args.AddRange(["--pdf-default-font-size", req.OptInt("fontsize", 12).ToString()]);
        }
        var r = await ProcessRunner.RunAsync(exe, args, ct, onStdOutLine: line =>
        {
            var m = Regex.Match(line, @"^(\d{1,3})% (.*)$");
            if (m.Success) progress.Report(new ConversionProgress(int.Parse(m.Groups[1].Value) / 100.0, "Calibre : " + m.Groups[2].Value.Trim()));
        }, workingDir: req.WorkDir, timeout: TimeSpan.FromMinutes(30)).ConfigureAwait(false);
        if (r.ExitCode != 0 || !File.Exists(output))
        {
            var text = r.StdErrTail + r.StdOutTail;
            if (text.Contains("DRM", StringComparison.Ordinal))
                throw new ConversionException(ConversionErrorKind.Protected, "Livre protégé par DRM : conversion impossible.");
            throw new ConversionException(ConversionErrorKind.EngineCrashed, $"Calibre a échoué (code {r.ExitCode}).", ProcessRunner.LastLines(text, 5));
        }
        File.Move(output, req.TempOutput);
        return new ConversionResult([req.TempOutput], []);
    }
}
