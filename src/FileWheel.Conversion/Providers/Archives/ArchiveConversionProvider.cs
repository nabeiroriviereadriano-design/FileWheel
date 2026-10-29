using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;

namespace FileWheel.Conversion.Providers.Archives;

/// <summary>Expose l'extraction et la conversion d'archives dans le convertisseur.</summary>
public sealed class ArchiveConversionProvider : ProviderBase
{
    private readonly ArchiveService _svc;
    private readonly DependencyManager _deps;

    public ArchiveConversionProvider(ArchiveService svc, DependencyManager deps)
    {
        _svc = svc;
        _deps = deps;
    }

    public override string Id => "archives";
    public override string DisplayName => "Archives (intégré + 7-Zip)";

    public override IReadOnlyList<ConversionCapability> Capabilities
    {
        get
        {
            var caps = new List<ConversionCapability>();
            var pwd = OptionDef.Text("source_password", "Mot de passe de l'archive (si chiffrée)") with { Advanced = true };
            // Extraction integree
            caps.Add(Cap(Id, ["zip", "tar", "tgz", "gz"], "extract", ConversionKind.Extract, [pwd], folder: true, priority: 10,
                label: "Extraire dans un dossier", limitations: "Chemins dangereux et liens ignorés ; plafonds de taille appliqués."));
            caps.Add(Cap(Id, ["7z", "xz", "bz2", "rar"], "extract", ConversionKind.Extract, [pwd], folder: true, priority: 20,
                engine: EngineIds.SevenZip, label: "Extraire dans un dossier",
                limitations: "RAR : uniquement si 7-Zip est installé sur le PC (lecture seule, la création de RAR n'est pas possible)."));

            var level = OptionDef.Int("level", "Niveau de compression", 6, 0, 9, "", "0 = aucune, 9 = maximale (plus lent).");
            var newPwd = OptionDef.Text("password", "Mot de passe de la nouvelle archive (facultatif)") with { Advanced = true };
            foreach (var target in new[] { "zip", "7z", "tgz" })
            {
                var from = ArchiveService.ExtractFormats.Where(f => f != target).ToArray();
                bool needs7 = target == "7z";
                caps.Add(Cap(Id, from, target, ConversionKind.Recompress, target == "tgz" ? [level, pwd] : [level, newPwd, pwd],
                    priority: needs7 ? 20 : 10, engine: needs7 ? EngineIds.SevenZip : null,
                    label: $"Convertir l'archive en {(target == "tgz" ? "TAR.GZ" : target.ToUpperInvariant())}",
                    limitations: "Le contenu est extrait puis recompressé ; l'archive d'origine est conservée."));
            }
            return caps;
        }
    }

    public override async Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var srcPwd = req.Opt("source_password");
        string? sp = string.IsNullOrEmpty(srcPwd) ? null : srcPwd;
        var extracted = await _svc.ExtractAsync(req.Input, req.WorkDir, sp, progress, ct).ConfigureAwait(false);
        var warnings = new List<string>();
        if (_svc.LastSkipped.Count > 0) warnings.Add($"{_svc.LastSkipped.Count} entrée(s) ignorée(s) par sécurité (chemins dangereux ou liens).");

        if (req.Capability.Kind == ConversionKind.Extract)
        {
            // Le contenu va dans le dossier temporaire prepare par le gestionnaire
            if (Directory.Exists(extracted))
            {
                foreach (var e in Directory.GetFileSystemEntries(extracted))
                {
                    var dest = Path.Combine(req.TempOutput, Path.GetFileName(e));
                    if (Directory.Exists(e)) Directory.Move(e, dest); else File.Move(e, dest);
                }
            }
            return new ConversionResult([req.TempOutput], warnings);
        }

        var pwd = req.Opt("password");
        var items = Directory.GetFileSystemEntries(extracted).ToList();
        var created = await _svc.CreateAsync(items, req.TargetFormat, req.OptInt("level", 6), string.IsNullOrEmpty(pwd) ? null : pwd,
            req.WorkDir, "recompresse", progress, ct).ConfigureAwait(false);
        File.Move(created, req.TempOutput);
        return new ConversionResult([req.TempOutput], warnings);
    }
}
