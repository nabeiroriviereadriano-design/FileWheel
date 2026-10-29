using FileWheel.Conversion.Engines;
using FileWheel.Core.Formats;
using FileWheel.Core.Wheel;

namespace FileWheel.Conversion.Core;

/// <summary>Une conversion possible depuis un format, avec la disponibilite reelle de son moteur.</summary>
public sealed record RouteInfo(ConversionCapability Capability, bool Available, string? MissingEngine)
{
    public string Target => Capability.To;
    public string Label => Capability.DisplayLabel;
}

/// <summary>
/// Repertoire des conversions. Seules les capacites declarees par les fournisseurs existent ;
/// une capacite dont le moteur est absent est montree comme « a installer », jamais comme disponible.
/// </summary>
public sealed class ConversionRegistry : IConversionMenuSource
{
    private readonly List<IConversionProvider> _providers;
    public DependencyManager Dependencies { get; }

    public ConversionRegistry(DependencyManager deps, IEnumerable<IConversionProvider> providers)
    {
        Dependencies = deps;
        _providers = providers.ToList();
    }

    public IReadOnlyList<IConversionProvider> Providers => _providers;

    public IConversionProvider Provider(string id) =>
        _providers.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException("Fournisseur inconnu : " + id);

    public IEnumerable<ConversionCapability> AllCapabilities => _providers.SelectMany(p => p.Capabilities);

    public bool EngineOk(ConversionCapability c) =>
        (c.EngineId is null || Dependencies.IsAvailable(c.EngineId)) && c.ExtraEngines.All(Dependencies.IsAvailable);

    /// <summary>Premier moteur manquant pour une capacite (null si tout est present).</summary>
    public string? MissingEngine(ConversionCapability c)
    {
        if (c.EngineId is not null && !Dependencies.IsAvailable(c.EngineId)) return c.EngineId;
        return c.ExtraEngines.FirstOrDefault(e => !Dependencies.IsAvailable(e));
    }

    /// <summary>
    /// Conversions depuis <paramref name="source"/>, une par (type, cible) : la meilleure disponible,
    /// a defaut la meilleure « a installer ».
    /// </summary>
    public IReadOnlyList<RouteInfo> RoutesFrom(string sourceFormatId, bool includeUnavailable = true, bool multiInput = false)
    {
        var caps = AllCapabilities.Where(c => c.From.Contains(sourceFormatId) && (!multiInput || c.MultiInput || c.SupportsBatch));
        var routes = new List<RouteInfo>();
        foreach (var g in caps.GroupBy(c => (c.Kind, c.To)))
        {
            var ordered = g.OrderBy(c => c.Priority).ToList();
            var ok = ordered.FirstOrDefault(EngineOk);
            if (ok is not null) routes.Add(new RouteInfo(ok, true, null));
            else if (includeUnavailable)
            {
                var c = ordered[0];
                var missing = MissingEngine(c);
                routes.Add(new RouteInfo(c, false, missing is null ? null : EngineCatalog.Get(missing).Name));
            }
        }
        return routes.OrderBy(r => r.Available ? 0 : 1).ThenBy(r => r.Capability.Kind).ThenBy(r => FormatCatalog.Get(r.Target)?.Label ?? r.Target).ToList();
    }

    /// <summary>Routes communes a tous les formats d'une selection.</summary>
    public IReadOnlyList<RouteInfo> CommonRoutes(IEnumerable<string> sourceFormatIds)
    {
        var ids = sourceFormatIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return [];
        var first = RoutesFrom(ids[0]);
        if (ids.Count == 1) return first;
        var others = ids.Skip(1).Select(id => RoutesFrom(id).Select(r => (r.Capability.Kind, r.Target)).ToHashSet()).ToList();
        return first.Where(r => others.All(o => o.Contains((r.Capability.Kind, r.Target)))).ToList();
    }

    /// <summary>Meilleure capacite disponible pour une cible donnee.</summary>
    public ConversionCapability? Best(string sourceFormatId, string target, ConversionKind? kind = null) =>
        AllCapabilities.Where(c => c.From.Contains(sourceFormatId) && c.To == target && (kind is null || c.Kind == kind))
            .OrderBy(c => EngineOk(c) ? 0 : 1).ThenBy(c => c.Priority).FirstOrDefault();

    public ConversionCapability? ByKey(string key) => AllCapabilities.FirstOrDefault(c => c.Key == key);

    /// <summary>Recherche « WebP » : toutes les conversions dont la source ou la cible correspond.</summary>
    public IReadOnlyList<(ConversionCapability Cap, bool Available)> Search(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return [];
        bool Match(string id)
        {
            var f = FormatCatalog.Get(id);
            return id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                   (f is not null && (f.Label.Contains(text, StringComparison.OrdinalIgnoreCase) || f.Extensions.Any(e => e.Equals(text.TrimStart('.'), StringComparison.OrdinalIgnoreCase))));
        }
        return AllCapabilities.Where(c => Match(c.To) || c.From.Any(Match))
            .Select(c => (c, EngineOk(c)))
            .OrderBy(x => x.Item2 ? 0 : 1).ThenBy(x => x.c.To).ToList();
    }

    // ------------------------------------------------------------- roue

    private static readonly Dictionary<FormatCategory, string[]> Preferred = new()
    {
        [FormatCategory.Image] = ["jpg", "png", "webp", "pdf", "ico", "avif"],
        [FormatCategory.Video] = ["mp4", "webm", "mp3", "gif", "mkv", "jpg"],
        [FormatCategory.Audio] = ["mp3", "flac", "wav", "m4a", "opus", "ogg"],
        [FormatCategory.Document] = ["pdf", "docx", "odt", "txt", "html", "epub"],
        [FormatCategory.Spreadsheet] = ["pdf", "xlsx", "csv", "ods"],
        [FormatCategory.Presentation] = ["pdf", "pptx", "odp", "png"],
        [FormatCategory.Pdf] = ["png", "txt", "docx", "jpg"],
        [FormatCategory.Data] = ["json", "csv", "xlsx", "xml", "yaml", "tsv"],
        [FormatCategory.Text] = ["pdf", "html", "txt", "md", "docx"],
        [FormatCategory.Ebook] = ["epub", "pdf", "txt", "html", "azw3"],
        [FormatCategory.Model3D] = ["stl", "obj", "glb", "3mf", "ply"],
        [FormatCategory.Font] = ["ttf", "otf", "woff"],
        [FormatCategory.Email] = ["txt", "html", "eml"],
        [FormatCategory.Contact] = ["csv"],
        [FormatCategory.Calendar] = ["csv"],
        [FormatCategory.Archive] = ["7z", "zip"],
    };

    public IReadOnlyList<QuickTarget> QuickTargets(IReadOnlyList<SelectedEntry> selection)
    {
        var formats = selection.Select(s => s.Format?.Id).ToList();
        if (formats.Any(f => f is null)) return [];
        var routes = CommonRoutes(formats!);
        var cat = selection[0].Category;
        var order = Preferred.GetValueOrDefault(cat, []);
        var list = new List<QuickTarget>();
        var srcIds = formats.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var target in order)
        {
            if (srcIds.Count == 1 && srcIds.Contains(target)) continue;
            var r = routes.FirstOrDefault(x => x.Target == target && x.Capability.Kind is not (ConversionKind.Recompress or ConversionKind.Extract));
            if (r is null) continue;
            if (selection.Count > 1 && !r.Capability.SupportsBatch) continue;
            var label = r.Capability.Kind switch
            {
                ConversionKind.ExtractAudio => $"Audio {FormatCatalog.Get(target)?.Label}",
                ConversionKind.Thumbnail => "Miniature JPEG",
                ConversionKind.PdfPages => $"Pages en {FormatCatalog.Get(target)?.Label}",
                ConversionKind.ExtractText => "Texte (TXT)",
                _ => FormatCatalog.Get(target)?.Label ?? target.ToUpperInvariant(),
            };
            var ext = FormatCatalog.Get(target)?.PrimaryExtension.ToUpperInvariant() ?? target.ToUpperInvariant();
            var detail = r.Capability.Kind switch
            {
                ConversionKind.ExtractAudio => "Audio seulement",
                ConversionKind.Thumbnail => "Image de la vidéo",
                ConversionKind.PdfPages => "Pages en images",
                ConversionKind.ExtractText => "Texte seul",
                ConversionKind.ImageSequence => "Toutes les images",
                _ => "L'original est conservé",
            };
            list.Add(new QuickTarget(r.Capability.Key, label, GlyphFor(target), r.Available,
                r.Available ? null : $"Moteur {r.MissingEngine} à installer", ext, detail));
            if (list.Count >= 5) break;
        }
        return list;
    }

    public static string GlyphFor(string formatId) => FormatCatalog.Get(formatId)?.Category switch
    {
        FormatCategory.Image => Glyphs.Image,
        FormatCategory.Video => Glyphs.Video,
        FormatCategory.Audio => Glyphs.Audio,
        FormatCategory.Pdf or FormatCategory.Document or FormatCategory.Spreadsheet or FormatCategory.Presentation => Glyphs.Document,
        FormatCategory.Data => Glyphs.Data,
        FormatCategory.Text => Glyphs.Document,
        FormatCategory.Ebook => Glyphs.Book,
        FormatCategory.Model3D or FormatCategory.Cad => Glyphs.Model3D,
        FormatCategory.Font => Glyphs.Font,
        FormatCategory.Email => Glyphs.Mail,
        FormatCategory.Contact => Glyphs.Contact,
        FormatCategory.Calendar => Glyphs.Calendar,
        FormatCategory.Archive => Glyphs.Archive,
        _ => Glyphs.Convert,
    };
}
