using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Data;

/// <summary>Table simple : en-tetes + lignes de texte.</summary>
public sealed class Table
{
    public List<string> Headers { get; } = new();
    public List<List<string>> Rows { get; } = new();

    public IEnumerable<IReadOnlyList<string>> AllRows(bool withHeader)
    {
        if (withHeader) yield return Headers;
        foreach (var r in Rows) yield return r;
    }
}

/// <summary>
/// Passage arbre (JSON/XML/YAML) &lt;-&gt; table (CSV/TSV/XLSX), avec une strategie explicite pour
/// les donnees imbriquees :
/// - « flatten » : colonnes « a.b » et « liste[0] » (aucune donnee perdue) ;
/// - « json » : la valeur imbriquee est ecrite en JSON dans la cellule ;
/// - « refuse » : erreur expliquant ou se trouve l'imbrication.
/// </summary>
public static class Tabular
{
    public static Table FromJson(JsonNode? node, string nested, List<string> notes)
    {
        var rows = new List<JsonNode?>();
        switch (node)
        {
            case JsonArray arr: rows.AddRange(arr); break;
            case JsonObject obj:
                // Objet contenant un unique tableau d'objets : on prend ce tableau (cas courant des API)
                var arrays = obj.Where(kv => kv.Value is JsonArray a && a.Count > 0 && a.All(x => x is JsonObject)).ToList();
                if (obj.Count == 1 && arrays.Count == 1)
                {
                    rows.AddRange((JsonArray)arrays[0].Value!);
                    notes.Add($"Les lignes viennent du tableau « {arrays[0].Key} ».");
                }
                else
                {
                    rows.Add(obj);
                    notes.Add("Le document est un objet unique : il produit une seule ligne.");
                }
                break;
            default:
                rows.Add(node);
                break;
        }

        var flatRows = new List<Dictionary<string, string>>();
        var headers = new List<string>();
        var seen = new HashSet<string>();
        bool flattened = false;
        int index = 0;
        foreach (var r in rows)
        {
            index++;
            var flat = new Dictionary<string, string>();
            if (r is JsonObject o)
            {
                foreach (var (k, v) in o) Flatten(k, v, flat, nested, index, ref flattened);
            }
            else if (r is JsonArray ra)
            {
                Flatten("valeur", ra, flat, nested, index, ref flattened);
            }
            else flat["valeur"] = ScalarText(r);
            foreach (var k in flat.Keys) if (seen.Add(k)) headers.Add(k);
            flatRows.Add(flat);
        }
        if (flattened && nested == "flatten") notes.Add("Données imbriquées aplaties en colonnes « parent.enfant » et « liste[0] » (stratégie réglable).");
        if (flattened && nested == "json") notes.Add("Données imbriquées écrites en texte JSON dans les cellules.");

        var t = new Table();
        t.Headers.AddRange(headers);
        foreach (var fr in flatRows) t.Rows.Add(headers.Select(h => fr.GetValueOrDefault(h, "")).ToList());
        return t;
    }

    private static void Flatten(string prefix, JsonNode? v, Dictionary<string, string> into, string nested, int row, ref bool flattened)
    {
        if (v is JsonObject o)
        {
            flattened = true;
            if (nested == "refuse") throw Nested(prefix, row);
            if (nested == "json") { into[prefix] = o.ToJsonString(); return; }
            if (o.Count == 0) { into[prefix] = ""; return; }
            foreach (var (k, c) in o) Flatten(prefix + "." + k, c, into, nested, row, ref flattened);
            return;
        }
        if (v is JsonArray a)
        {
            if (a.All(x => x is JsonValue or null) && nested != "flatten")
            {
                flattened = true;
                if (nested == "refuse") throw Nested(prefix, row);
                into[prefix] = a.ToJsonString();
                return;
            }
            flattened = true;
            if (nested == "refuse") throw Nested(prefix, row);
            if (nested == "json") { into[prefix] = a.ToJsonString(); return; }
            if (a.Count == 0) { into[prefix] = ""; return; }
            for (int i = 0; i < a.Count; i++) Flatten($"{prefix}[{i}]", a[i], into, nested, row, ref flattened);
            return;
        }
        into[prefix] = ScalarText(v);
    }

    private static ConversionException Nested(string key, int row) =>
        new(ConversionErrorKind.InvalidOptions,
            $"Conversion directe impossible : la ligne {row} contient des données imbriquées (« {key} »). " +
            "Choisissez la stratégie « Aplatir en colonnes » ou « JSON dans la cellule » dans les réglages.");

    public static string ScalarText(JsonNode? v)
    {
        if (v is null) return "";
        if (v is JsonValue jv)
            return jv.GetValueKind() switch
            {
                JsonValueKind.String => jv.GetValue<string>(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => jv.ToJsonString(),
                _ => "",
            };
        return v.ToJsonString();
    }

    public static Table FromRows(List<List<string>> rows, bool header)
    {
        var t = new Table();
        if (rows.Count == 0) return t;
        int width = rows.Max(r => r.Count);
        if (header)
        {
            var h = rows[0];
            var used = new HashSet<string>();
            for (int i = 0; i < width; i++)
            {
                var name = i < h.Count && h[i].Trim().Length > 0 ? h[i].Trim() : $"colonne{i + 1}";
                var unique = name;
                int n = 2;
                while (!used.Add(unique)) unique = $"{name}_{n++}";
                t.Headers.Add(unique);
            }
        }
        else for (int i = 0; i < width; i++) t.Headers.Add($"colonne{i + 1}");
        foreach (var r in rows.Skip(header ? 1 : 0))
        {
            var row = new List<string>(r);
            while (row.Count < width) row.Add("");
            t.Rows.Add(row);
        }
        return t;
    }

    /// <summary>Table -&gt; tableau d'objets JSON. Les nombres et booleens sont types si <paramref name="infer"/>.</summary>
    public static JsonArray ToJson(Table t, bool infer)
    {
        var arr = new JsonArray();
        foreach (var r in t.Rows)
        {
            var o = new JsonObject();
            for (int i = 0; i < t.Headers.Count; i++) o[t.Headers[i]] = infer ? Infer(r[i]) : JsonValue.Create(r[i]);
            arr.Add(o);
        }
        return arr;
    }

    public static JsonNode? Infer(string s)
    {
        if (s.Length == 0) return null;
        if (s is "true" or "TRUE" or "vrai" or "VRAI") return JsonValue.Create(true);
        if (s is "false" or "FALSE" or "faux" or "FAUX") return JsonValue.Create(false);
        // Les identifiants a zeros initiaux (codes postaux, references) restent du texte
        bool leadingZero = s.Length > 1 && s[0] == '0' && s[1] != '.' && s[1] != ',';
        if (!leadingZero && s.Length <= 15 && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
        if (!leadingZero && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && s.Contains('.') && !double.IsInfinity(d)) return JsonValue.Create(d);
        return JsonValue.Create(s);
    }
}
