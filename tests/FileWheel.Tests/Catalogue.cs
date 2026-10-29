using System.Text;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Formats;
using FileWheel.Core.Settings;

namespace FileWheel.Tests;

/// <summary>
/// Genere la liste des conversions REELLEMENT declarees par les fournisseurs (aucune liste ecrite a la main) :
/// dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md
/// </summary>
public static class Catalogue
{
    public static void Write(string path)
    {
        var engine = new ConversionEngine(() => new ConversionSettings());
        var sb = new StringBuilder();
        sb.AppendLine("# Conversions prises en charge par FileWheel");
        sb.AppendLine();
        sb.AppendLine("Liste **générée automatiquement** à partir des capacités déclarées par chaque fournisseur (`dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md`).");
        sb.AppendLine("Une conversion n'apparaît dans la roue que si son moteur est présent ; sinon elle est grisée avec le nom du moteur à installer.");
        sb.AppendLine("Les conversions par les codecs Windows (WIC) ne figurent pas ici : elles dépendent des codecs installés sur le PC (PNG, JPEG, BMP, TIFF, GIF, ICO, et selon les extensions du Store : HEIC, WebP, AVIF, JPEG XR, RAW).");
        sb.AppendLine();
        sb.AppendLine("Les formats d'images ImageMagick listés ici sont ceux de l'environnement de test ; sous Windows, la liste réelle est relue au démarrage depuis l'ImageMagick installé (`magick -list format`) et peut être plus longue.");
        sb.AppendLine();
        sb.AppendLine("Légende des pertes : **aucune** = contenu conservé ; **qualité** = compression avec perte (image, son, vidéo) ; **structure** = mise en forme, métadonnées ou éléments que le format cible ne sait pas représenter. Le détail est affiché avant chaque conversion.");
        sb.AppendLine();
        foreach (var provider in engine.Registry.Providers)
        {
            var caps = provider.Capabilities.ToList();
            if (caps.Count == 0) continue;
            var engineIds = caps.Select(c => c.EngineId).Concat(caps.SelectMany(c => c.ExtraEngines)).Where(e => e is not null).Distinct().ToList();
            var engines = engineIds.Count == 0 ? "intégré (aucun programme externe)" : string.Join(", ", engineIds.Select(e => EngineCatalog.All.FirstOrDefault(x => x.Id == e)?.Name ?? e));
            sb.AppendLine($"## {provider.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"Moteur : {engines}. {caps.Count} conversion(s).");
            sb.AppendLine();
            sb.AppendLine("| Depuis | Vers | Type | Perte | Remarques |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var c in caps.OrderBy(c => c.To).ThenBy(c => c.Kind))
            {
                var from = string.Join(", ", c.From.Order().Select(f => FormatCatalog.Get(f)?.PrimaryExtension ?? f));
                var to = c.OutputIsFolder ? $"dossier de {FormatCatalog.Get(c.To)?.PrimaryExtension ?? c.To}" : FormatCatalog.Get(c.To)?.PrimaryExtension ?? c.To;
                var loss = c.Loss switch { LossKind.None => "aucune", LossKind.Lossy => "qualité (compression)", _ => "structure / mise en forme" };
                var detail = string.Join(" ", new[] { c.LossNote, c.Limitations, c.MultiInput ? "Plusieurs fichiers → un seul." : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var notes = c.Label is { Length: > 0 } lbl ? (detail.Length > 0 ? $"**{lbl}** — {detail}" : $"**{lbl}**") : detail;
                sb.AppendLine($"| {Esc(from)} | {Esc(to)} | {KindLabel(c.Kind)} | {loss} | {Esc(notes)} |");
            }
            sb.AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"Catalogue écrit : {path} ({engine.Registry.AllCapabilities.Count()} capacités)");
    }

    private static string KindLabel(ConversionKind k) => k switch
    {
        ConversionKind.Convert => "conversion",
        ConversionKind.Remux => "changement de conteneur",
        ConversionKind.ExtractAudio => "extraction audio",
        ConversionKind.Thumbnail => "miniature",
        ConversionKind.ImageSequence => "séquence d'images",
        ConversionKind.PdfPages => "pages en images",
        ConversionKind.ExtractText => "extraction de texte",
        ConversionKind.Merge => "assemblage",
        ConversionKind.Compress => "compression",
        ConversionKind.Extract => "extraction",
        ConversionKind.Split => "découpage",
        ConversionKind.Recompress => "recompression",
        _ => k.ToString(),
    };

    private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
