using FileWheel.Core.Formats;

namespace FileWheel.Conversion.Core;

public enum OptionKind { Choice, Int, Bool, Text, Time }

/// <summary>Valeur proposee pour une option a choix.</summary>
public sealed record OptionChoice(string Value, string Label);

/// <summary>
/// Description d'un reglage expose par un fournisseur. L'interface genere les controles
/// a partir de ces descriptions : un reglage non decrit n'existe pas.
/// </summary>
public sealed record OptionDef(string Key, string Label, OptionKind Kind, string Default)
{
    public IReadOnlyList<OptionChoice> Choices { get; init; } = [];
    public int Min { get; init; }
    public int Max { get; init; } = int.MaxValue;
    public string Help { get; init; } = "";
    /// <summary>Reglage reserve a la fenetre avancee.</summary>
    public bool Advanced { get; init; }
    /// <summary>Unite affichee (px, %, kbit/s...).</summary>
    public string Unit { get; init; } = "";

    public static OptionDef Choice(string key, string label, string def, params (string v, string l)[] choices) =>
        new(key, label, OptionKind.Choice, def) { Choices = choices.Select(c => new OptionChoice(c.v, c.l)).ToList() };

    public static OptionDef Int(string key, string label, int def, int min, int max, string unit = "", string help = "") =>
        new(key, label, OptionKind.Int, def.ToString()) { Min = min, Max = max, Unit = unit, Help = help };

    public static OptionDef Bool(string key, string label, bool def, string help = "") =>
        new(key, label, OptionKind.Bool, def ? "true" : "false") { Help = help };

    public static OptionDef Text(string key, string label, string def = "", string help = "") =>
        new(key, label, OptionKind.Text, def) { Help = help };

    public static OptionDef Time(string key, string label, string help = "") =>
        new(key, label, OptionKind.Time, "") { Help = help };
}

public enum LossKind
{
    /// <summary>Aucune perte de donnees attendue.</summary>
    None,
    /// <summary>Perte de qualite (compression avec perte, reencodage).</summary>
    Lossy,
    /// <summary>Perte possible de structure ou de mise en page (documents, 3D...).</summary>
    Structure,
}

public enum ConversionKind
{
    Convert, Remux, ExtractAudio, Thumbnail, ImageSequence, PdfPages, ExtractText, Merge, Compress, Extract, Split, Recompress,
}

/// <summary>Une conversion qu'un fournisseur sait REELLEMENT faire.</summary>
public sealed record ConversionCapability
{
    public required string ProviderId { get; init; }
    public required IReadOnlySet<string> From { get; init; }
    public required string To { get; init; }
    public ConversionKind Kind { get; init; } = ConversionKind.Convert;
    /// <summary>Libelle de la conversion (« Extraire l'audio », « Pages en images »...).</summary>
    public string? Label { get; init; }
    public IReadOnlyList<OptionDef> Options { get; init; } = [];
    public bool SupportsBatch { get; init; } = true;
    /// <summary>Plusieurs entrees produisent une seule sortie (images vers PDF, fusion audio).</summary>
    public bool MultiInput { get; init; }
    /// <summary>La sortie est un dossier (pages PDF, sequence d'images, extraction).</summary>
    public bool OutputIsFolder { get; init; }
    public LossKind Loss { get; init; } = LossKind.None;
    public string? LossNote { get; init; }
    public string? Limitations { get; init; }
    public bool Cancellable { get; init; } = true;
    /// <summary>Le moteur peut suspendre une tache en cours (aucun ne le permet aujourd'hui).</summary>
    public bool Pausable { get; init; }
    /// <summary>Plus petit = prefere quand plusieurs fournisseurs proposent la meme sortie.</summary>
    public int Priority { get; init; } = 50;
    /// <summary>Moteur externe requis (null = integre).</summary>
    public string? EngineId { get; init; }
    /// <summary>Moteurs supplementaires necessaires (chaines de conversion).</summary>
    public IReadOnlyList<string> ExtraEngines { get; init; } = [];
    /// <summary>Cle unique et stable (fournisseur + type + sources + cible), utilisee par l'historique et les profils.</summary>
    public string Key => _key ??= $"{ProviderId}:{Kind}:{string.Join(',', From.Order(StringComparer.Ordinal))}>{To}";
    private string? _key;

    public string DisplayLabel => Label ?? $"Convertir en {FormatCatalog.Get(To)?.Label ?? To.ToUpperInvariant()}";
}

/// <summary>Demande de conversion preparee par le gestionnaire.</summary>
public sealed class ConversionRequest
{
    public required IReadOnlyList<string> Inputs { get; init; }
    public required FormatInfo SourceFormat { get; init; }
    public required ConversionCapability Capability { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
    /// <summary>Ou le fournisseur doit ecrire (fichier ou dossier temporaire).</summary>
    public string TempOutput { get; set; } = "";
    /// <summary>Dossier de travail prive du fournisseur (supprime apres la tache).</summary>
    public string WorkDir { get; set; } = "";
    /// <summary>Chemin final (informatif : c'est le gestionnaire qui y deplace le resultat).</summary>
    public string FinalOutput { get; set; } = "";

    public string Input => Inputs[0];
    public string TargetFormat => Capability.To;

    public string Opt(string key)
    {
        if (Options.TryGetValue(key, out var v) && v is not null) return v;
        return Capability.Options.FirstOrDefault(o => o.Key == key)?.Default ?? "";
    }

    public int OptInt(string key, int fallback)
    {
        var def = Capability.Options.FirstOrDefault(o => o.Key == key);
        if (!int.TryParse(Opt(key), out var v)) return fallback;
        if (def is not null) v = Math.Clamp(v, def.Min, def.Max);
        return v;
    }

    public bool OptBool(string key) => string.Equals(Opt(key), "true", StringComparison.OrdinalIgnoreCase);
}

public sealed record ConversionProgress(double? Fraction, string Stage, TimeSpan? Remaining = null);

/// <summary>Avertissements et informations calculees avant de lancer la conversion.</summary>
public sealed class ConversionAnalysis
{
    public List<string> Warnings { get; } = new();
    public List<string> Notes { get; } = new();
    public long? EstimatedBytes { get; set; }
    public bool Blocking { get; set; }
}

public enum ConversionErrorKind
{
    UnknownFormat, NotSupported, MissingCodec, FileLocked, DiskFull, PermissionDenied, Corrupt, EngineMissing,
    NameConflict, Cancelled, WriteError, EngineCrashed, InvalidInput, Protected, TooLarge, InvalidOptions, Unsafe, Unknown,
}

/// <summary>Erreur de conversion avec un message destine a l'utilisateur.</summary>
public sealed class ConversionException : Exception
{
    public ConversionErrorKind Kind { get; }
    /// <summary>Detail technique pour le journal (sortie du moteur, code de retour...).</summary>
    public string? Technical { get; }

    public ConversionException(ConversionErrorKind kind, string userMessage, string? technical = null, Exception? inner = null)
        : base(userMessage, inner)
    {
        Kind = kind;
        Technical = technical;
    }
}

/// <summary>Resultat d'une conversion terminee.</summary>
public sealed record ConversionResult(IReadOnlyList<string> Outputs, IReadOnlyList<string> Warnings)
{
    public static ConversionResult Single(string path, params string[] warnings) => new([path], warnings);
}

/// <summary>
/// Un moteur de conversion. Chaque fournisseur declare ses capacites ; le gestionnaire
/// ne lui transmet que des demandes qui y correspondent, apres verification du moteur.
/// </summary>
public interface IConversionProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>Capacites reellement disponibles (le moteur requis doit etre installe).</summary>
    IReadOnlyList<ConversionCapability> Capabilities { get; }

    /// <summary>Analyse avant conversion : pertes, transparence, animation...</summary>
    ConversionAnalysis Analyze(ConversionRequest request);

    Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct);
}

/// <summary>Base commune : analyse par defaut fondee sur les proprietes des formats.</summary>
public abstract class ProviderBase : IConversionProvider
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract IReadOnlyList<ConversionCapability> Capabilities { get; }

    public virtual ConversionAnalysis Analyze(ConversionRequest request)
    {
        var a = new ConversionAnalysis();
        var src = request.SourceFormat;
        var dst = FormatCatalog.Get(request.TargetFormat);
        if (request.Capability.LossNote is { } note) a.Notes.Add(note);
        if (request.Capability.Limitations is { } lim) a.Notes.Add(lim);
        if (dst is not null)
        {
            if (src.Lossy && dst.Lossless && src.Category == dst.Category)
                a.Notes.Add($"La source {src.Label} est déjà compressée avec perte : la conversion en {dst.Label} ne restaure pas la qualité d'origine, elle évite seulement une perte supplémentaire.");
            if (src.Category == FormatCategory.Image && dst.Category == FormatCategory.Image)
            {
                if (src.Alpha && !dst.Alpha)
                    a.Warnings.Add($"{dst.Label} ne gère pas la transparence : les zones transparentes seront remplies (fond blanc par défaut).");
                if (src.Animation && !dst.Animation)
                    a.Warnings.Add($"{dst.Label} ne gère pas l'animation : seule la première image sera conservée si la source est animée.");
            }
        }
        return a;
    }

    public abstract Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct);

    protected static ConversionCapability Cap(string provider, IEnumerable<string> from, string to, ConversionKind kind = ConversionKind.Convert,
        IReadOnlyList<OptionDef>? options = null, LossKind loss = LossKind.None, string? lossNote = null, string? limitations = null,
        int priority = 50, string? engine = null, string? label = null, bool folder = false, bool multi = false, bool batch = true,
        IReadOnlyList<string>? extraEngines = null) =>
        new()
        {
            ProviderId = provider, From = new HashSet<string>(from, StringComparer.OrdinalIgnoreCase), To = to, Kind = kind,
            Options = options ?? [], Loss = loss, LossNote = lossNote, Limitations = limitations, Priority = priority,
            EngineId = engine, Label = label, OutputIsFolder = folder, MultiInput = multi, SupportsBatch = batch,
            ExtraEngines = extraEngines ?? [],
        };
}
