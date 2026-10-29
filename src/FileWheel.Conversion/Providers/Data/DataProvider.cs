using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Data;

/// <summary>
/// Donnees structurees, integre : JSON, XML, YAML, CSV, TSV, XLSX (lecture et ecriture simples).
/// La syntaxe de la source est toujours validee avant conversion ; une erreur indique la ligne.
/// </summary>
public sealed class DataProvider : ProviderBase
{
    public override string Id => "data";
    public override string DisplayName => "Données (intégré)";

    private static readonly string[] Formats = ["json", "xml", "yaml", "csv", "tsv", "xlsx"];
    private static readonly string[] TabularFormats = ["csv", "tsv", "xlsx"];

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } = Build();

    private static List<ConversionCapability> Build()
    {
        var caps = new List<ConversionCapability>();
        foreach (var to in Formats)
        {
            var from = Formats.Where(f => f != to || f is "csv" or "tsv").ToArray();
            var opts = new List<OptionDef>
            {
                OptionDef.Choice("delimiter_in", "Séparateur de la source CSV", "auto", ("auto", "Détection automatique"), (",", "Virgule"), (";", "Point-virgule"), ("tab", "Tabulation"), ("|", "Barre verticale")) with { Advanced = true },
                OptionDef.Bool("header", "La première ligne contient les noms de colonnes", true),
                OptionDef.Int("sheet", "Feuille du classeur (XLSX)", 1, 1, 1000) with { Advanced = true },
            };
            if (TabularFormats.Contains(to))
                opts.Add(OptionDef.Choice("nested", "Données imbriquées", "flatten", ("flatten", "Aplatir en colonnes (parent.enfant)"), ("json", "JSON dans la cellule"), ("refuse", "Refuser la conversion")));
            if (to is "csv")
            {
                var listSep = CultureInfo.CurrentCulture.TextInfo.ListSeparator == ";" ? ";" : ",";
                opts.Add(OptionDef.Choice("delimiter", "Séparateur", listSep, (",", "Virgule"), (";", "Point-virgule (Excel français)"), ("tab", "Tabulation")));
            }
            if (to is "csv" or "tsv")
            {
                opts.Add(OptionDef.Bool("bom", "Marque UTF-8 (BOM) pour Excel", true) with { Advanced = true });
                opts.Add(OptionDef.Bool("formulas", "Neutraliser les formules (=, +, -, @)", true) with { Advanced = true, Help = "Empêche un tableur d'exécuter du contenu comme formule." });
            }
            if (to is "json" or "yaml" or "xml")
                opts.Add(OptionDef.Bool("infer", "Typer les nombres et booléens (sources CSV/XLSX)", true));
            if (to == "xml") opts.Add(OptionDef.Text("root", "Nom de l'élément racine", "racine") with { Advanced = true });
            caps.Add(Cap("data", from, to, ConversionKind.Convert, opts, LossKind.None,
                limitations: to is "csv" or "tsv" or "xlsx" ? "Les structures imbriquées sont aplaties selon la stratégie choisie." :
                             to == "xml" ? "Convention : attributs « @nom », texte « #text », éléments répétés en tableaux." : null,
                priority: 10));
        }
        // Classeur XLSX : toutes les feuilles en CSV (dossier)
        caps.Add(Cap("data", ["xlsx"], "csv", ConversionKind.Split,
        [
            OptionDef.Choice("delimiter", "Séparateur", CultureInfo.CurrentCulture.TextInfo.ListSeparator == ";" ? ";" : ",", (",", "Virgule"), (";", "Point-virgule"), ("tab", "Tabulation")),
            OptionDef.Bool("bom", "Marque UTF-8 (BOM) pour Excel", true) with { Advanced = true },
        ], folder: true, priority: 20, label: "Toutes les feuilles en CSV (un fichier par feuille)"));
        return caps;
    }

    public override async Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        return await Task.Run(() => Convert(req, ct), ct).ConfigureAwait(false);
    }

    private ConversionResult Convert(ConversionRequest req, CancellationToken ct)
    {
        var notes = new List<string>();
        var src = req.SourceFormat.Id;
        var to = req.TargetFormat;

        if (req.Capability.Kind == ConversionKind.Split)
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(req.Input);
            var sheets = Xlsx.Sheets(zip);
            var stem = Path.GetFileNameWithoutExtension(req.Input);
            for (int i = 1; i <= sheets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (t, _, name) = Xlsx.Read(req.Input, i, header: false);
                var path = Path.Combine(req.TempOutput, Xlsx.SheetFileName(stem, name, "csv"));
                WriteDelimited(req, path, t, DelimiterOut(req), header: false, notes);
            }
            return new ConversionResult([req.TempOutput], notes);
        }

        bool header = req.Opt("header") != "false";
        bool tabularSource = src is "csv" or "tsv" or "xlsx";
        if (tabularSource)
        {
            var table = ReadTable(req, header, notes);
            ct.ThrowIfCancellationRequested();
            switch (to)
            {
                case "csv": WriteDelimited(req, req.TempOutput, table, DelimiterOut(req), header, notes); break;
                case "tsv": WriteDelimited(req, req.TempOutput, table, '\t', header, notes); break;
                case "xlsx": Xlsx.Write(req.TempOutput, table, Path.GetFileNameWithoutExtension(req.Input)); break;
                default: WriteTree(req, Tabular.ToJson(table, req.Opt("infer") != "false"), to); break;
            }
            return new ConversionResult([req.TempOutput], notes);
        }

        var tree = ReadTree(req, notes);
        ct.ThrowIfCancellationRequested();
        switch (to)
        {
            case "csv":
            case "tsv":
            case "xlsx":
            {
                var t = Tabular.FromJson(tree, req.Opt("nested"), notes);
                if (t.Headers.Count == 0) throw new ConversionException(ConversionErrorKind.InvalidInput, "Aucune donnée tabulaire trouvée dans ce document.");
                if (to == "xlsx") Xlsx.Write(req.TempOutput, t, Path.GetFileNameWithoutExtension(req.Input));
                else WriteDelimited(req, req.TempOutput, t, to == "tsv" ? '\t' : DelimiterOut(req), true, notes);
                break;
            }
            default:
                WriteTree(req, tree, to);
                break;
        }
        return new ConversionResult([req.TempOutput], notes);
    }

    private static char DelimiterOut(ConversionRequest req) => req.Opt("delimiter") switch
    {
        ";" => ';',
        "tab" => '\t',
        _ => ',',
    };

    private static Table ReadTable(ConversionRequest req, bool header, List<string> notes)
    {
        if (req.SourceFormat.Id == "xlsx")
        {
            var (t, count, name) = Xlsx.Read(req.Input, req.OptInt("sheet", 1), header);
            if (count > 1) notes.Add($"Le classeur contient {count} feuilles : seule « {name} » est convertie (réglage « Feuille », ou « Toutes les feuilles en CSV »).");
            return t;
        }
        var (text, enc) = TextIO.Read(req.Input);
        if (enc.CodePage == 1252) notes.Add("Encodage de la source détecté : Windows-1252 (converti en UTF-8).");
        char delim = req.SourceFormat.Id == "tsv" ? '\t' : req.Opt("delimiter_in") switch
        {
            ";" => ';', "tab" => '\t', "|" => '|', "," => ',', _ => Csv.DetectDelimiter(text),
        };
        if (req.SourceFormat.Id == "csv" && req.Opt("delimiter_in") == "auto")
            notes.Add($"Séparateur détecté : {(delim == '\t' ? "tabulation" : delim == ';' ? "point-virgule" : delim == ',' ? "virgule" : delim.ToString())}.");
        List<List<string>> rows;
        try { rows = Csv.Parse(text, delim); }
        catch (FormatException ex) { throw new ConversionException(ConversionErrorKind.Corrupt, "CSV invalide : " + ex.Message, inner: ex); }
        var widths = rows.Select(r => r.Count).Distinct().ToList();
        if (widths.Count > 1) notes.Add($"Lignes de longueurs différentes ({widths.Min()} à {widths.Max()} colonnes) : les cellules manquantes sont laissées vides.");
        return Tabular.FromRows(rows, header);
    }

    private static JsonNode? ReadTree(ConversionRequest req, List<string> notes)
    {
        var (text, _) = TextIO.Read(req.Input);
        switch (req.SourceFormat.Id)
        {
            case "json":
                try
                {
                    return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256 });
                }
                catch (JsonException ex)
                {
                    throw new ConversionException(ConversionErrorKind.Corrupt, $"JSON invalide (ligne {(ex.LineNumber ?? 0) + 1}) : {ex.Message.Split('.')[0]}.", inner: ex);
                }
            case "xml":
                try { return XmlJson.ToJson(XmlJson.LoadSafe(text)); }
                catch (XmlException ex) when (ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConversionException(ConversionErrorKind.Unsafe, "XML refusé : il contient une DTD (risque de sécurité XXE).", inner: ex);
                }
                catch (XmlException ex)
                {
                    throw new ConversionException(ConversionErrorKind.Corrupt, $"XML invalide (ligne {ex.LineNumber}) : {ex.Message}", inner: ex);
                }
            case "yaml":
                try
                {
                    var node = MiniYaml.Parse(text, out bool multi);
                    if (multi) notes.Add("Le fichier YAML contient plusieurs documents : seul le premier est converti.");
                    return node;
                }
                catch (MiniYaml.YamlException ex)
                {
                    throw new ConversionException(ConversionErrorKind.Corrupt, ex.Message, inner: ex);
                }
            default:
                throw new ConversionException(ConversionErrorKind.NotSupported, "Format de données source non pris en charge.");
        }
    }

    private static void WriteTree(ConversionRequest req, JsonNode? tree, string to)
    {
        switch (to)
        {
            case "json":
                TextIO.WriteUtf8(req.TempOutput, tree?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null");
                break;
            case "yaml":
                TextIO.WriteUtf8(req.TempOutput, MiniYaml.Write(tree));
                break;
            case "xml":
                var root = req.Opt("root");
                TextIO.WriteUtf8(req.TempOutput, XmlJson.Serialize(XmlJson.FromJson(tree, string.IsNullOrWhiteSpace(root) ? "racine" : root)));
                break;
            default:
                throw new ConversionException(ConversionErrorKind.NotSupported, "Format de sortie non pris en charge.");
        }
    }

    private static void WriteDelimited(ConversionRequest req, string path, Table t, char delim, bool header, List<string> notes)
    {
        bool neutralize = req.Opt("formulas") != "false";
        bool fromTabular = req.SourceFormat.Id is "csv" or "tsv";
        int neutralized = 0;
        IEnumerable<IReadOnlyList<string>> rows = t.AllRows(header);
        if (neutralize && !fromTabular)
        {
            rows = rows.Select(r => (IReadOnlyList<string>)r.Select(v =>
            {
                var n = Csv.NeutralizeFormula(v);
                if (!ReferenceEquals(n, v) && n != v) neutralized++;
                return n;
            }).ToList()).ToList();
        }
        var text = Csv.Write(rows, delim);
        TextIO.WriteUtf8(path, text, bom: req.Opt("bom") != "false");
        if (neutralized > 0) notes.Add($"{neutralized} cellule(s) commençant par =, +, - ou @ ont été préfixées d'une apostrophe pour éviter leur exécution comme formule.");
    }
}
