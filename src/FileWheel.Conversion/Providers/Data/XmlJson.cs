using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace FileWheel.Conversion.Providers.Data;

/// <summary>
/// Correspondance XML &lt;-&gt; JSON (convention explicite et documentee) :
/// - attribut « id » -&gt; propriete « @id » ;
/// - texte d'un element qui a aussi des attributs ou enfants -&gt; « #text » ;
/// - elements freres de meme nom -&gt; tableau ;
/// - element sans attribut ni enfant -&gt; valeur texte.
/// Les DTD et entites externes sont refusees (protection XXE).
/// </summary>
public static class XmlJson
{
    public static XmlReaderSettings SafeSettings => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = 512L << 20,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    public static XDocument LoadSafe(string text)
    {
        using var sr = new StringReader(text);
        using var xr = XmlReader.Create(sr, SafeSettings);
        return XDocument.Load(xr, LoadOptions.None);
    }

    public static JsonObject ToJson(XDocument doc)
    {
        var root = doc.Root ?? throw new FormatException("Document XML vide.");
        return new JsonObject { [Name(root.Name)] = Element(root) };
    }

    private static string Name(XName n) => n.LocalName;

    private static JsonNode? Element(XElement e)
    {
        bool hasAttrs = e.Attributes().Any(a => !a.IsNamespaceDeclaration);
        bool hasChildren = e.HasElements;
        var text = string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (!hasAttrs && !hasChildren) return text.Length == 0 && e.IsEmpty ? null : JsonValue.Create(text);

        var obj = new JsonObject();
        foreach (var a in e.Attributes().Where(a => !a.IsNamespaceDeclaration))
            obj["@" + Name(a.Name)] = a.Value;
        foreach (var group in e.Elements().GroupBy(c => Name(c.Name)))
        {
            var items = group.ToList();
            if (items.Count == 1) obj[group.Key] = Element(items[0]);
            else
            {
                var arr = new JsonArray();
                foreach (var it in items) arr.Add(Element(it));
                obj[group.Key] = arr;
            }
        }
        if (text.Length > 0) obj["#text"] = text;
        return obj;
    }

    public static XDocument FromJson(JsonNode? node, string rootName = "racine")
    {
        // Un objet a une seule cle devient l'element racine
        if (node is JsonObject o && o.Count == 1 && o.First().Value is not JsonArray)
        {
            var (k, v) = o.First();
            var root = new XElement(SafeName(k));
            Fill(root, v);
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }
        var r = new XElement(SafeName(rootName));
        Fill(r, node);
        return new XDocument(new XDeclaration("1.0", "utf-8", null), r);
    }

    private static void Fill(XElement el, JsonNode? v)
    {
        switch (v)
        {
            case null:
                break;
            case JsonObject o:
                foreach (var (k, child) in o)
                {
                    if (k.StartsWith('@') && child is JsonValue av) { el.SetAttributeValue(SafeName(k[1..]), ValueText(av)); continue; }
                    if (k == "#text" && child is JsonValue tv) { el.Add(new XText(ValueText(tv))); continue; }
                    if (child is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            var c = new XElement(SafeName(k));
                            Fill(c, item);
                            el.Add(c);
                        }
                        continue;
                    }
                    var ce = new XElement(SafeName(k));
                    Fill(ce, child);
                    el.Add(ce);
                }
                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    var c = new XElement("element");
                    Fill(c, item);
                    el.Add(c);
                }
                break;
            case JsonValue jv:
                el.Add(new XText(ValueText(jv)));
                break;
        }
    }

    private static string ValueText(JsonValue v) => v.GetValueKind() switch
    {
        JsonValueKind.String => v.GetValue<string>(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => v.ToJsonString(),
    };

    /// <summary>Nom d'element XML valide a partir d'une cle JSON quelconque.</summary>
    public static string SafeName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "_";
        var sb = new StringBuilder();
        foreach (var c in key)
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        var s = sb.ToString();
        if (!(char.IsLetter(s[0]) || s[0] == '_')) s = "_" + s;
        if (s.StartsWith("xml", StringComparison.OrdinalIgnoreCase)) s = "_" + s;
        try { return XmlConvert.VerifyName(s); }
        catch { return "_" + Guid.NewGuid().ToString("N")[..6]; }
    }

    public static string Serialize(XDocument doc)
    {
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(new StringWriterUtf8(sb), new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
            doc.Save(w);
        return sb.ToString();
    }

    private sealed class StringWriterUtf8(StringBuilder sb) : StringWriter(sb)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
