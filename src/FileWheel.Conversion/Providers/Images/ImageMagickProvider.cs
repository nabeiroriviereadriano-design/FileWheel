using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Formats;
using FileWheel.Core.Logging;

namespace FileWheel.Conversion.Providers.Images;

/// <summary>
/// Images via ImageMagick (programme externe). Les formats proposes viennent de
/// « magick -list format » : seuls les codecs presents en lecture/ecriture sont annonces.
///
/// Securite : le format d'entree est impose par un prefixe explicite (« PNG:chemin ») issu de
/// notre propre detection, ce qui empeche ImageMagick de choisir un decodeur dangereux d'apres
/// le contenu (MVG, MSL, TEXT...). Les SVG qui referencent des ressources externes sont refuses
/// (aucun acces reseau ni lecture de fichiers tiers a l'insu de l'utilisateur).
/// </summary>
public sealed class ImageMagickProvider : ProviderBase
{
    private readonly DependencyManager _deps;
    private IReadOnlyList<ConversionCapability>? _caps;
    private string? _capsFor;
    private Dictionary<string, string>? _modes;

    // Nos identifiants -> codeur ImageMagick
    private static readonly Dictionary<string, string> Coder = new()
    {
        ["png"] = "PNG", ["jpg"] = "JPEG", ["webp"] = "WEBP", ["bmp"] = "BMP", ["tiff"] = "TIFF", ["gif"] = "GIF",
        ["avif"] = "AVIF", ["heic"] = "HEIC", ["ico"] = "ICO", ["svg"] = "SVG", ["tga"] = "TGA", ["dds"] = "DDS",
        ["exr"] = "EXR", ["hdr"] = "HDR", ["psd"] = "PSD", ["pdf"] = "PDF",
    };

    private static readonly string[] Targets = ["png", "jpg", "webp", "bmp", "tiff", "gif", "avif", "heic", "ico", "tga", "dds", "exr", "psd", "pdf"];
    private static readonly string[] RawCoders = ["DNG", "CR2", "CR3", "NEF", "ARW", "ORF", "RW2", "RAF", "PEF", "SRW", "NRW", "SR2", "3FR", "ERF", "KDC", "MRW", "X3F", "IIQ", "RWL"];
    private static readonly string[] AssumedWindowsPortable =
        ["PNG:rw", "JPEG:rw", "WEBP:rw", "BMP:rw", "TIFF:rw", "GIF:rw", "AVIF:rw", "HEIC:rw", "ICO:rw", "SVG:r", "TGA:rw", "DDS:rw", "EXR:rw", "HDR:rw", "PSD:rw", "PDF:w", "DNG:r", "CR2:r", "NEF:r", "ARW:r"];

    public ImageMagickProvider(DependencyManager deps)
    {
        _deps = deps;
        deps.Changed += () => { _caps = null; _modes = null; };
    }

    public override string Id => "imagemagick";
    public override string DisplayName => "ImageMagick (images)";

    private Dictionary<string, string> Modes()
    {
        if (_modes is not null) return _modes;
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exe = _deps.PathOf(EngineIds.ImageMagick);
        if (exe is not null)
        {
            try
            {
                var r = ProcessRunner.RunAsync(exe, ["-list", "format"], CancellationToken.None, timeout: TimeSpan.FromSeconds(30), tailChars: 400_000).GetAwaiter().GetResult();
                foreach (Match x in Regex.Matches(r.StdOutTail, @"^\s*([A-Z0-9]+)\*?\s+(?:\S+\s+)?([r-][w-][+-])\s", RegexOptions.Multiline))
                    m[x.Groups[1].Value] = x.Groups[2].Value;
            }
            catch (Exception ex) { Log.Warn("Liste des formats ImageMagick illisible : " + ex.Message); }
        }
        if (m.Count == 0)
            foreach (var s in AssumedWindowsPortable) { var p = s.Split(':'); m[p[0]] = p[1].Contains('w') ? "rw-" : "r--"; }
        // Lecture PDF : necessite Ghostscript, que FileWheel n'utilise pas (PDFium s'en charge).
        if (m.TryGetValue("PDF", out var pdfMode)) m["PDF"] = "-" + pdfMode[1..];
        _modes = m;
        return m;
    }

    private bool CanRead(string id) =>
        id == "raw" ? RawCoders.Any(c => Modes().TryGetValue(c, out var md) && md[0] == 'r')
                    : Coder.TryGetValue(id, out var c2) && Modes().TryGetValue(c2, out var md2) && md2[0] == 'r';

    private bool CanWrite(string id) => Coder.TryGetValue(id, out var c) && Modes().TryGetValue(c, out var m) && m[1] == 'w';

    public override IReadOnlyList<ConversionCapability> Capabilities
    {
        get
        {
            var key = _deps.PathOf(EngineIds.ImageMagick) ?? "";
            if (_caps is not null && _capsFor == key) return _caps;
            var sources = Coder.Keys.Where(k => k != "pdf").Append("raw").Where(CanRead).ToList();
            var caps = new List<ConversionCapability>();
            foreach (var t in Targets.Where(CanWrite))
            {
                var dst = FormatCatalog.Require(t);
                var opts = Options(t);
                if (t == "pdf")
                {
                    caps.Add(Cap(Id, sources, "pdf", ConversionKind.Convert, opts, LossKind.None, engine: EngineIds.ImageMagick, priority: 30,
                        label: "Convertir en PDF"));
                    caps.Add(Cap(Id, sources, "pdf", ConversionKind.Merge, opts, LossKind.None, engine: EngineIds.ImageMagick, priority: 30,
                        multi: true, batch: false, label: "Assembler les images en un PDF"));
                    continue;
                }
                caps.Add(Cap(Id, sources, t, ConversionKind.Convert, opts, dst.Lossy ? LossKind.Lossy : LossKind.None,
                    dst.Lossy ? $"{dst.Label} compresse avec perte (qualité réglable)." : null,
                    limitations: t == "ico" ? "Icône multi-tailles 16 à 256 px." : null,
                    engine: EngineIds.ImageMagick, priority: 20));
            }
            _caps = caps;
            _capsFor = key;
            return caps;
        }
    }

    private static List<OptionDef> Options(string target)
    {
        var o = new List<OptionDef>();
        if (target is "jpg" or "webp" or "avif" or "heic" or "pdf")
            o.Add(OptionDef.Int("quality", "Qualité", 90, 1, 100, "%"));
        if (target == "webp") o.Add(OptionDef.Bool("lossless", "WebP sans perte", false));
        o.Add(OptionDef.Choice("resize", "Redimensionner", "none", ("none", "Taille d'origine"), ("percent", "Pourcentage"), ("width", "Largeur fixe"), ("height", "Hauteur fixe"), ("fit", "Tenir dans un cadre L × H"), ("exact", "Taille exacte (déforme)")));
        o.Add(OptionDef.Int("percent", "Pourcentage", 50, 1, 1000, "%"));
        o.Add(OptionDef.Int("width", "Largeur", 1920, 1, 30000, "px"));
        o.Add(OptionDef.Int("height", "Hauteur", 1080, 1, 30000, "px"));
        o.Add(OptionDef.Bool("enlarge", "Autoriser l'agrandissement", false) with { Advanced = true });
        o.Add(OptionDef.Choice("rotate", "Rotation", "0", ("0", "Aucune"), ("90", "90° horaire"), ("180", "180°"), ("270", "90° anti-horaire")));
        o.Add(OptionDef.Bool("autoorient", "Appliquer l'orientation EXIF", true) with { Advanced = true });
        o.Add(OptionDef.Bool("strip", "Supprimer les métadonnées (EXIF, GPS…)", false));
        o.Add(OptionDef.Choice("background", "Fond si la transparence est perdue", "white", ("white", "Blanc"), ("black", "Noir")) with { Advanced = true });
        o.Add(OptionDef.Choice("colorspace", "Profil colorimétrique", "keep", ("keep", "Conserver"), ("srgb", "Convertir en sRGB")) with { Advanced = true });
        o.Add(OptionDef.Choice("depth", "Profondeur de couleur", "auto", ("auto", "Automatique"), ("8", "8 bits"), ("16", "16 bits")) with { Advanced = true });
        if (target is "gif" or "webp")
            o.Add(OptionDef.Choice("animation", "Animation", "keep", ("keep", "Conserver"), ("first", "Première image seulement")));
        o.Add(OptionDef.Int("density", "Résolution de rendu SVG", 96, 24, 1200, "ppp") with { Advanced = true, Help = "Pour les images vectorielles (SVG)." });
        if (target is "png" or "tiff") o.Add(OptionDef.Int("compression", "Niveau de compression", 6, 0, 9) with { Advanced = true });
        return o;
    }

    public override ConversionAnalysis Analyze(ConversionRequest request)
    {
        var a = base.Analyze(request);
        // Remplacer l'avertissement generique par une verification reelle si possible
        var exe = _deps.PathOf(EngineIds.ImageMagick);
        if (exe is not null && request.SourceFormat.Alpha && !(FormatCatalog.Get(request.TargetFormat)?.Alpha ?? true))
        {
            try
            {
                var r = ProcessRunner.RunAsync(exe, ["identify", "-format", "%A\n", InputSpec(request.Input, request.SourceFormat.Id, firstFrame: true)], CancellationToken.None, timeout: TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
                var alpha = r.StdOutTail.Trim().ToLowerInvariant();
                if (r.ExitCode == 0 && alpha is "false" or "undefined")
                    a.Warnings.RemoveAll(w => w.Contains("transparence"));
                else if (r.ExitCode == 0)
                {
                    a.Warnings.RemoveAll(w => w.Contains("transparence"));
                    a.Warnings.Add($"Cette image est transparente : {FormatCatalog.Get(request.TargetFormat)?.Label} ne gère pas la transparence, le fond sera {(request.Opt("background") == "black" ? "noir" : "blanc")}.");
                }
            }
            catch { }
        }
        if (request.SourceFormat.Id == "raw")
            a.Notes.Add("Photo RAW : développement par défaut d'ImageMagick (LibRaw), sans les réglages de votre logiciel photo.");
        if (request.SourceFormat.Id == "psd")
            a.Notes.Add("PSD : seule l'image aplatie (composite) est convertie, les calques sont perdus.");
        if (request.SourceFormat.Id == "svg" && SvgCheck(request.Input) is { } svgError)
        {
            a.Warnings.Add(svgError);
            a.Blocking = true;
        }
        return a;
    }

    /// <summary>Refuse les SVG avec entites XML ou references vers des ressources externes.</summary>
    public static string? SvgCheck(string path)
    {
        string text;
        try
        {
            using var r = new StreamReader(path);
            var buf = new char[4 << 20];
            int n = r.ReadBlock(buf, 0, buf.Length);
            text = new string(buf, 0, n);
        }
        catch (Exception ex) { return "SVG illisible : " + ex.Message; }
        if (text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            return "SVG refusé par sécurité : il déclare des entités XML (risque de lecture de fichiers).";
        foreach (Match m in Regex.Matches(text, @"(?:href|src)\s*=\s*[""']\s*([^""']*)", RegexOptions.IgnoreCase))
        {
            var v = m.Groups[1].Value.Trim();
            if (v.Length == 0 || v.StartsWith('#') || v.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            return "SVG refusé par sécurité : il fait référence à une ressource externe (" + (v.Length > 60 ? v[..60] + "…" : v) + ").";
        }
        if (Regex.IsMatch(text, @"url\(\s*['""]?\s*(?:https?:|file:)", RegexOptions.IgnoreCase))
            return "SVG refusé par sécurité : il charge une ressource externe via url().";
        return null;
    }

    private static string InputSpec(string path, string formatId, bool firstFrame)
    {
        string coder = formatId == "raw" ? Path.GetExtension(path).TrimStart('.').ToUpperInvariant() : Coder.GetValueOrDefault(formatId, "");
        if (formatId == "raw" && !RawCoders.Contains(coder)) coder = "DNG";
        var spec = (coder.Length > 0 ? coder + ":" : "") + path;
        return firstFrame ? spec + "[0]" : spec;
    }

    /// <summary>Un nom contenant des caracteres interpretes par ImageMagick est copie sous un nom neutre.</summary>
    private static string SafeInput(string input, string workDir, int index)
    {
        var name = Path.GetFileName(input);
        if (name.IndexOfAny(['[', ']', '%', '@', '*', '?', '~', ':']) < 0 && !input.StartsWith('-')) return input;
        var copy = Path.Combine(workDir, $"entree{index}{Path.GetExtension(input)}");
        File.Copy(input, copy, overwrite: true);
        return copy;
    }

    public override async Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var exe = _deps.Require(EngineIds.ImageMagick);
        if (req.SourceFormat.Id == "svg" && SvgCheck(req.Input) is { } svgErr)
            throw new ConversionException(ConversionErrorKind.Unsafe, svgErr);
        var target = req.TargetFormat;
        var dst = FormatCatalog.Require(target);
        var warnings = new List<string>();
        var args = new List<string>();

        bool keepAnimation = (target is "gif" or "webp") && req.Opt("animation") != "first" && req.SourceFormat.Animation;
        bool multiPagePdf = target == "pdf";
        if (req.SourceFormat.Id == "svg") { args.Add("-density"); args.Add(req.OptInt("density", 96).ToString()); }

        for (int i = 0; i < req.Inputs.Count; i++)
        {
            var input = SafeInput(req.Inputs[i], req.WorkDir, i);
            var fmt = i == 0 ? req.SourceFormat.Id : FormatDetector.Detect(req.Inputs[i]).Format?.Id ?? req.SourceFormat.Id;
            if (fmt == "svg" && i > 0 && SvgCheck(req.Inputs[i]) is { } e2) throw new ConversionException(ConversionErrorKind.Unsafe, e2);
            bool first = !keepAnimation && !(multiPagePdf && fmt == "tiff");
            args.Add(InputSpec(input, fmt, firstFrame: first && ((FormatCatalog.Get(fmt)?.Animation ?? false) || fmt is "ico" or "tiff" or "psd")));
        }
        if (keepAnimation) args.Add("-coalesce");
        if (req.OptBool("autoorient")) args.Add("-auto-orient");

        var resize = req.Opt("resize");
        string grow = req.OptBool("enlarge") ? "" : ">";
        switch (resize)
        {
            case "percent": args.AddRange(["-resize", req.OptInt("percent", 50) + "%"]); break;
            case "width": args.AddRange(["-resize", req.OptInt("width", 1920) + "x" + grow]); break;
            case "height": args.AddRange(["-resize", "x" + req.OptInt("height", 1080) + grow]); break;
            case "fit": args.AddRange(["-resize", $"{req.OptInt("width", 1920)}x{req.OptInt("height", 1080)}{grow}"]); break;
            case "exact": args.AddRange(["-resize", $"{req.OptInt("width", 1920)}x{req.OptInt("height", 1080)}!"]); break;
        }
        var rot = req.Opt("rotate");
        if (rot is "90" or "180" or "270") args.AddRange(["-rotate", rot]);
        if (req.Opt("colorspace") == "srgb") args.AddRange(["-colorspace", "sRGB"]);
        if (req.Opt("depth") is "8" or "16") args.AddRange(["-depth", req.Opt("depth")]);

        if (!dst.Alpha)
        {
            var bg = req.Opt("background") == "black" ? "black" : "white";
            args.AddRange(["-background", bg, "-alpha", "remove", "-alpha", "off"]);
        }
        if (req.OptBool("strip")) args.Add("-strip");
        if (target is "jpg" or "webp" or "avif" or "heic" or "pdf") args.AddRange(["-quality", req.OptInt("quality", 90).ToString()]);
        if (target == "webp" && req.OptBool("lossless")) args.AddRange(["-define", "webp:lossless=true"]);
        if (target is "png" or "tiff")
        {
            var level = req.OptInt("compression", 6);
            if (target == "png") args.AddRange(["-define", $"png:compression-level={level}"]);
            else args.AddRange(["-compress", level == 0 ? "None" : "Zip"]);
        }
        if (target == "ico") args.AddRange(["-define", "icon:auto-resize=256,128,64,48,32,16"]);
        if (target == "gif" && keepAnimation) args.AddRange(["-layers", "Optimize"]);
        if (target == "dds") args.AddRange(["-define", "dds:compression=dxt5"]);
        args.Add(Coder[target] + ":" + req.TempOutput);

        if (req.SourceFormat.Animation && !keepAnimation && target is not ("gif" or "webp"))
            warnings.Add($"Si la source est animée, seule la première image est convertie ({dst.Label} ne gère pas l'animation).");

        progress.Report(new ConversionProgress(null, "Conversion de l'image…"));
        var r = await ProcessRunner.RunAsync(exe, args, ct, workingDir: req.WorkDir, timeout: TimeSpan.FromMinutes(30)).ConfigureAwait(false);
        if (r.ExitCode != 0 || !File.Exists(req.TempOutput))
        {
            var e = r.StdErrTail;
            if (e.Contains("no decode delegate", StringComparison.OrdinalIgnoreCase) || e.Contains("NoDecodeDelegate", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.MissingCodec, "ImageMagick ne sait pas lire ce format dans cette installation.", e);
            if (e.Contains("no encode delegate", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.MissingCodec, $"ImageMagick ne sait pas écrire en {dst.Label} dans cette installation.", e);
            if (e.Contains("improper image header", StringComparison.OrdinalIgnoreCase) || e.Contains("corrupt", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("insufficient image data", StringComparison.OrdinalIgnoreCase) || e.Contains("Premature end", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.Corrupt, "Image endommagée ou tronquée.", e);
            if (e.Contains("cache resources exhausted", StringComparison.OrdinalIgnoreCase) || e.Contains("width or height exceeds limit", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.TooLarge, "Image trop grande pour les limites d'ImageMagick.", e);
            throw new ConversionException(ConversionErrorKind.EngineCrashed, $"ImageMagick a échoué (code {r.ExitCode}).", ProcessRunner.LastLines(e, 4));
        }
        // Animation : certaines versions d'ImageMagick n'ecrivent qu'une image (WebP anime notamment).
        if (keepAnimation)
        {
            int Frames(string p, string coderFmt)
            {
                var id = ProcessRunner.RunAsync(exe, ["identify", "-format", "%n\n", coderFmt + p], ct).GetAwaiter().GetResult();
                return int.TryParse(id.StdOutTail.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var n) ? n : 0;
            }
            int before = Frames(req.Input, Coder.GetValueOrDefault(req.SourceFormat.Id, "") is { Length: > 0 } c ? c + ":" : "");
            int after = Frames(req.TempOutput, Coder[target] + ":");
            if (before > 1 && after == 1)
                warnings.Add($"Cette version d'ImageMagick n'a pas pu conserver l'animation en {dst.Label} : seule la première des {before} images a été écrite.");
        }
        // Avertissements non bloquants d'ImageMagick (profil couleur, etc.) dans le journal seulement
        if (r.StdErrTail.Length > 0) Log.Debug("ImageMagick : " + ProcessRunner.LastLines(r.StdErrTail, 3));
        return new ConversionResult([req.TempOutput], warnings);
    }
}
