using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FileWheel.Conversion.Providers.Data;

/// <summary>
/// YAML sans dependance : sous-ensemble courant de YAML 1.2 (schema « core »).
///
/// Pris en charge : mappings et sequences en blocs, sequences compactes (« - cle: valeur »),
/// scalaires simples / entre apostrophes / entre guillemets, blocs litteraux « | » et replies « > »,
/// collections en ligne [a, b] et {a: 1}, commentaires, marqueur « --- ».
/// Refuse explicitement (message clair, jamais de resultat incomplet) : ancres et alias (&amp;, *),
/// etiquettes (!), cles complexes (?), documents multiples au-dela du premier (signale).
/// </summary>
public static class MiniYaml
{
    public sealed class YamlException(string message, int line) : Exception($"YAML ligne {line} : {message}")
    {
        public int Line { get; } = line;
    }

    private sealed record Line(int Number, int Indent, string Text);

    public static JsonNode? Parse(string text, out bool multipleDocuments)
    {
        multipleDocuments = false;
        var lines = new List<Line>();
        int docs = 0;
        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            var l = raw[i];
            if (l.StartsWith("%")) continue; // directive
            if (l.TrimEnd() == "---" || l.StartsWith("--- "))
            {
                docs++;
                if (docs > 1 || lines.Count > 0) { multipleDocuments = true; break; }
                var rest = l.Length > 4 ? l[4..] : "";
                if (rest.Trim().Length > 0) lines.Add(new Line(i + 1, 0, StripComment(rest).TrimEnd()));
                continue;
            }
            if (l.TrimEnd() == "...") break;
            int indent = 0;
            while (indent < l.Length && l[indent] == ' ') indent++;
            if (indent < l.Length && l[indent] == '\t' && l.Trim().Length > 0)
                throw new YamlException("tabulation utilisée pour l'indentation (interdit en YAML).", i + 1);
            var content = l[indent..];
            // Les blocs litteraux gardent leurs lignes telles quelles (commentaires compris)
            lines.Add(new Line(i + 1, indent, content));
        }
        int pos = 0;
        SkipBlank(lines, ref pos);
        if (pos >= lines.Count) return null;
        var node = ParseBlock(lines, ref pos, lines[pos].Indent);
        SkipBlank(lines, ref pos);
        if (pos < lines.Count) throw new YamlException("contenu inattendu (indentation incohérente).", lines[pos].Number);
        return node;
    }

    private static bool IsBlank(Line l)
    {
        var t = l.Text.TrimStart();
        return t.Length == 0 || t.StartsWith('#');
    }

    private static void SkipBlank(List<Line> lines, ref int pos)
    {
        while (pos < lines.Count && IsBlank(lines[pos])) pos++;
    }

    private static JsonNode? ParseBlock(List<Line> lines, ref int pos, int indent)
    {
        SkipBlank(lines, ref pos);
        if (pos >= lines.Count) return null;
        var line = lines[pos];
        var t = StripComment(line.Text).TrimEnd();
        if (t == "-" || t.StartsWith("- ")) return ParseSequence(lines, ref pos, line.Indent);
        if (FindKeySeparator(t) >= 0) return ParseMapping(lines, ref pos, line.Indent);
        // Scalaire seul (eventuellement sur plusieurs lignes)
        var sb = new StringBuilder(t);
        pos++;
        while (pos < lines.Count && !IsBlank(lines[pos]) && lines[pos].Indent >= indent)
        {
            sb.Append(' ').Append(StripComment(lines[pos].Text).Trim());
            pos++;
        }
        return Scalar(sb.ToString(), line.Number);
    }

    private static JsonArray ParseSequence(List<Line> lines, ref int pos, int indent)
    {
        var arr = new JsonArray();
        while (true)
        {
            SkipBlank(lines, ref pos);
            if (pos >= lines.Count) break;
            var line = lines[pos];
            if (line.Indent != indent) break;
            var t = StripComment(line.Text).TrimEnd();
            if (!(t == "-" || t.StartsWith("- "))) break;
            var content = t.Length > 1 ? t[2..].TrimStart() : "";
            int contentIndent = indent + (t.Length - content.Length);
            if (content.Length == 0)
            {
                pos++;
                SkipBlank(lines, ref pos);
                if (pos < lines.Count && lines[pos].Indent > indent) arr.Add(ParseBlock(lines, ref pos, lines[pos].Indent));
                else arr.Add(null);
                continue;
            }
            bool nestedSeq = content == "-" || content.StartsWith("- ");
            bool compactMap = !content.StartsWith('[') && !content.StartsWith('{') && FindKeySeparator(content) >= 0;
            if (nestedSeq || compactMap)
            {
                // Element compact : on reecrit la ligne comme si son contenu commencait a contentIndent
                lines[pos] = new Line(line.Number, contentIndent, content);
                arr.Add(ParseBlock(lines, ref pos, contentIndent));
                continue;
            }
            arr.Add(InlineValue(content, lines, ref pos, line.Number));
        }
        return arr;
    }

    private static JsonObject ParseMapping(List<Line> lines, ref int pos, int indent)
    {
        var obj = new JsonObject();
        while (true)
        {
            SkipBlank(lines, ref pos);
            if (pos >= lines.Count) break;
            var line = lines[pos];
            if (line.Indent != indent) break;
            var t = StripComment(line.Text).TrimEnd();
            if (t.StartsWith("- ") || t == "-") break;
            if (t.StartsWith("? ")) throw new YamlException("clés complexes (« ? ») non prises en charge.", line.Number);
            int sep = FindKeySeparator(t);
            if (sep < 0) throw new YamlException($"« {Trunc(t)} » : deux-points attendu après la clé.", line.Number);
            var keyText = t[..sep].Trim();
            var key = keyText.StartsWith('"') || keyText.StartsWith('\'') ? Unquote(keyText, line.Number) : keyText;
            if (key.StartsWith('&') || key.StartsWith('*') || key.StartsWith('!'))
                throw new YamlException("ancres, alias et étiquettes ne sont pas pris en charge.", line.Number);
            if (obj.ContainsKey(key)) throw new YamlException($"clé « {key} » en double.", line.Number);
            var rest = t[(sep + 1)..].Trim();
            if (rest.Length == 0)
            {
                pos++;
                SkipBlank(lines, ref pos);
                if (pos < lines.Count && lines[pos].Indent > indent) obj[key] = ParseBlock(lines, ref pos, lines[pos].Indent);
                else if (pos < lines.Count && lines[pos].Indent == indent && (StripComment(lines[pos].Text).TrimEnd() is "-" || lines[pos].Text.StartsWith("- ")))
                    obj[key] = ParseSequence(lines, ref pos, indent);
                else obj[key] = null;
                continue;
            }
            if (rest[0] is '|' or '>')
            {
                obj[key] = BlockScalar(lines, ref pos, indent, rest, line.Number);
                continue;
            }
            obj[key] = InlineValue(rest, lines, ref pos, line.Number);
        }
        return obj;
    }

    private static JsonNode? InlineValue(string rest, List<Line> lines, ref int pos, int lineNo)
    {
        if (rest.StartsWith('&') || rest.StartsWith('*'))
            throw new YamlException("ancres et alias (&, *) ne sont pas pris en charge : convertissez d'abord le fichier sans références.", lineNo);
        if (rest.StartsWith('!'))
            throw new YamlException("les étiquettes de type (!) ne sont pas prises en charge.", lineNo);
        if (rest.StartsWith('[') || rest.StartsWith('{'))
        {
            // Collection en ligne, eventuellement sur plusieurs lignes
            var sb = new StringBuilder(rest);
            pos++;
            while (!Balanced(sb.ToString()))
            {
                if (pos >= lines.Count) throw new YamlException("collection en ligne non fermée.", lineNo);
                sb.Append(' ').Append(StripComment(lines[pos].Text).Trim());
                pos++;
            }
            int i = 0;
            var s = sb.ToString();
            var node = ParseFlow(s, ref i, lineNo);
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length) throw new YamlException("texte inattendu après une collection en ligne.", lineNo);
            return node;
        }
        if (rest.StartsWith('"') && !ClosedQuote(rest))
        {
            // Chaine entre guillemets sur plusieurs lignes
            var sb = new StringBuilder(rest);
            pos++;
            while (!ClosedQuote(sb.ToString()))
            {
                if (pos >= lines.Count) throw new YamlException("guillemet non fermé.", lineNo);
                sb.Append(' ').Append(lines[pos].Text.Trim());
                pos++;
            }
            return JsonValue.Create(Unquote(sb.ToString().Trim(), lineNo));
        }
        pos++;
        // Scalaire simple sur plusieurs lignes (continuation plus indentee)
        var plain = new StringBuilder(rest);
        while (pos < lines.Count && !IsBlank(lines[pos]) && lines[pos].Indent > 0 && !rest.StartsWith('"') && !rest.StartsWith('\'')
               && IsContinuation(lines, pos))
        {
            plain.Append(' ').Append(StripComment(lines[pos].Text).Trim());
            pos++;
        }
        return Scalar(plain.ToString(), lineNo);
    }

    private static bool IsContinuation(List<Line> lines, int pos)
    {
        // Une ligne plus indentee que la cle precedente qui n'est ni cle ni element de liste
        var t = StripComment(lines[pos].Text).Trim();
        if (t.StartsWith("- ") || t == "-" || FindKeySeparator(t) >= 0) return false;
        int prevIndent = lines[pos - 1].Indent;
        return lines[pos].Indent > prevIndent;
    }

    private static JsonNode BlockScalar(List<Line> lines, ref int pos, int indent, string header, int lineNo)
    {
        bool literal = header[0] == '|';
        string chomp = header.Contains('-') ? "strip" : header.Contains('+') ? "keep" : "clip";
        pos++;
        var body = new List<string>();
        int blockIndent = -1;
        while (pos < lines.Count)
        {
            var l = lines[pos];
            if (l.Text.Trim().Length == 0) { body.Add(""); pos++; continue; }
            if (l.Indent <= indent) break;
            if (blockIndent < 0) blockIndent = l.Indent;
            if (l.Indent < blockIndent) break;
            body.Add(new string(' ', l.Indent - blockIndent) + l.Text);
            pos++;
        }
        // Les lignes vides finales appartiennent au bloc seulement pour le « keep »
        int trailing = 0;
        while (body.Count > 0 && body[^1].Length == 0) { body.RemoveAt(body.Count - 1); trailing++; }
        string text;
        if (literal) text = string.Join("\n", body);
        else
        {
            var sb = new StringBuilder();
            for (int i = 0; i < body.Count; i++)
            {
                if (i > 0) sb.Append(body[i].Length == 0 || body[i - 1].Length == 0 || body[i].StartsWith(' ') ? "\n" : " ");
                sb.Append(body[i]);
            }
            text = sb.ToString().Replace(" \n", "\n");
        }
        text = chomp switch
        {
            "strip" => text,
            "keep" => text + "\n" + new string('\n', trailing),
            _ => body.Count > 0 ? text + "\n" : "",
        };
        _ = lineNo;
        return JsonValue.Create(text)!;
    }

    // ------------------------------------------------------------------ collections en ligne

    private static JsonNode? ParseFlow(string s, ref int i, int lineNo)
    {
        Ws(s, ref i);
        if (i >= s.Length) return null;
        if (s[i] == '[')
        {
            i++;
            var arr = new JsonArray();
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return arr; }
            while (true)
            {
                arr.Add(ParseFlow(s, ref i, lineNo));
                Ws(s, ref i);
                if (i >= s.Length) throw new YamlException("« ] » attendu.", lineNo);
                if (s[i] == ',') { i++; Ws(s, ref i); if (i < s.Length && s[i] == ']') { i++; return arr; } continue; }
                if (s[i] == ']') { i++; return arr; }
                throw new YamlException("« , » ou « ] » attendu.", lineNo);
            }
        }
        if (s[i] == '{')
        {
            i++;
            var obj = new JsonObject();
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                Ws(s, ref i);
                var key = FlowScalarText(s, ref i, lineNo, isKey: true);
                Ws(s, ref i);
                JsonNode? val = null;
                if (i < s.Length && s[i] == ':') { i++; val = ParseFlow(s, ref i, lineNo); }
                if (obj.ContainsKey(key)) throw new YamlException($"clé « {key} » en double.", lineNo);
                obj[key] = val;
                Ws(s, ref i);
                if (i >= s.Length) throw new YamlException("« } » attendu.", lineNo);
                if (s[i] == ',') { i++; Ws(s, ref i); if (i < s.Length && s[i] == '}') { i++; return obj; } continue; }
                if (s[i] == '}') { i++; return obj; }
                throw new YamlException("« , » ou « } » attendu.", lineNo);
            }
        }
        bool quoted = s[i] is '"' or '\'';
        var text = FlowScalarText(s, ref i, lineNo, isKey: false);
        return quoted ? JsonValue.Create(text) : Scalar(text, lineNo);
    }

    private static string FlowScalarText(string s, ref int i, int lineNo, bool isKey)
    {
        if (i < s.Length && (s[i] == '"' || s[i] == '\''))
        {
            char q = s[i];
            int start = i;
            i++;
            while (i < s.Length)
            {
                if (q == '"' && s[i] == '\\') { i += 2; continue; }
                if (s[i] == q)
                {
                    if (q == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                    i++;
                    return Unquote(s[start..i], lineNo);
                }
                i++;
            }
            throw new YamlException("guillemet non fermé.", lineNo);
        }
        int st = i;
        while (i < s.Length && s[i] is not (',' or ']' or '}') && !(isKey && s[i] == ':' && (i + 1 >= s.Length || s[i + 1] == ' ')))
        {
            if (!isKey && s[i] == ':' && i + 1 < s.Length && s[i + 1] == ' ') break;
            i++;
        }
        return s[st..i].Trim();
    }

    private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    private static bool Balanced(string s)
    {
        int depth = 0;
        bool dq = false, sq = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (dq) { if (c == '\\') i++; else if (c == '"') dq = false; continue; }
            if (sq) { if (c == '\'') sq = false; continue; }
            if (c == '"') dq = true;
            else if (c == '\'') sq = true;
            else if (c is '[' or '{') depth++;
            else if (c is ']' or '}') depth--;
        }
        return depth <= 0;
    }

    private static bool ClosedQuote(string s)
    {
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ scalaires

    private static string StripComment(string s)
    {
        bool dq = false, sq = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (dq) { if (c == '\\') i++; else if (c == '"') dq = false; continue; }
            if (sq) { if (c == '\'') sq = false; continue; }
            if (c == '"' && (i == 0 || s[i - 1] is ' ' or ':' or '[' or '{' or ',' or '-')) dq = true;
            else if (c == '\'' && (i == 0 || s[i - 1] is ' ' or ':' or '[' or '{' or ',' or '-')) sq = true;
            else if (c == '#' && (i == 0 || s[i - 1] == ' ')) return s[..i];
        }
        return s;
    }

    /// <summary>Position du « : » separant cle et valeur (hors guillemets), ou -1.</summary>
    private static int FindKeySeparator(string t)
    {
        if (t.StartsWith('[') || t.StartsWith('{')) return -1;
        bool dq = false, sq = false;
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (dq) { if (c == '\\') i++; else if (c == '"') dq = false; continue; }
            if (sq) { if (c == '\'') sq = false; continue; }
            if (c == '"' && i == 0) dq = true;
            else if (c == '\'' && i == 0) sq = true;
            else if (c == ':' && (i + 1 == t.Length || t[i + 1] == ' ')) return i;
        }
        return -1;
    }

    private static string Unquote(string s, int lineNo)
    {
        s = s.Trim();
        if (s.StartsWith('\''))
        {
            if (!s.EndsWith('\'') || s.Length < 2) throw new YamlException("apostrophe non fermée.", lineNo);
            return s[1..^1].Replace("''", "'");
        }
        if (s.StartsWith('"'))
        {
            if (!s.EndsWith('"') || s.Length < 2) throw new YamlException("guillemet non fermé.", lineNo);
            var inner = s[1..^1];
            var sb = new StringBuilder();
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (c != '\\') { sb.Append(c); continue; }
                if (++i >= inner.Length) break;
                switch (inner[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case ' ': sb.Append(' '); break;
                    case 'u' when i + 4 < inner.Length:
                        sb.Append((char)Convert.ToInt32(inner.Substring(i + 1, 4), 16)); i += 4; break;
                    case 'x' when i + 2 < inner.Length:
                        sb.Append((char)Convert.ToInt32(inner.Substring(i + 1, 2), 16)); i += 2; break;
                    default: sb.Append('\\').Append(inner[i]); break;
                }
            }
            return sb.ToString();
        }
        return s;
    }

    private static readonly Regex IntRe = new(@"^[-+]?[0-9]+$");
    private static readonly Regex FloatRe = new(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$");

    private static JsonNode? Scalar(string raw, int lineNo)
    {
        var s = raw.Trim();
        if (s.StartsWith('"') || s.StartsWith('\'')) return JsonValue.Create(Unquote(s, lineNo));
        if (s.StartsWith('&') || s.StartsWith('*')) throw new YamlException("ancres et alias non pris en charge.", lineNo);
        if (s is "" or "~" or "null" or "Null" or "NULL") return null;
        if (s is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (s is "false" or "False" or "FALSE") return JsonValue.Create(false);
        if (IntRe.IsMatch(s) && !(s.TrimStart('-', '+').Length > 1 && s.TrimStart('-', '+').StartsWith('0')) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            return JsonValue.Create(l);
        if (s.StartsWith("0x") && long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx)) return JsonValue.Create(hx);
        if (s.StartsWith("0o") && s.Length > 2 && s[2..].All(c => c is >= '0' and <= '7')) return JsonValue.Create(Convert.ToInt64(s[2..], 8));
        if (FloatRe.IsMatch(s) && s.Any(char.IsDigit) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return JsonValue.Create(d);
        if (s is ".inf" or "+.inf" or ".Inf") return JsonValue.Create("Infinity");
        if (s is "-.inf" or "-.Inf") return JsonValue.Create("-Infinity");
        if (s is ".nan" or ".NaN") return JsonValue.Create("NaN");
        return JsonValue.Create(s);
    }

    private static string Trunc(string s) => s.Length > 40 ? s[..40] + "…" : s;

    // ------------------------------------------------------------------ ecriture

    public static string Write(JsonNode? node)
    {
        var sb = new StringBuilder();
        if (node is JsonObject o && o.Count > 0) WriteObject(sb, o, 0);
        else if (node is JsonArray a && a.Count > 0) WriteArray(sb, a, 0);
        else sb.Append(ScalarText(node)).Append('\n');
        return sb.ToString();
    }

    private static void WriteObject(StringBuilder sb, JsonObject o, int indent)
    {
        foreach (var (k, v) in o)
        {
            sb.Append(' ', indent).Append(Key(k)).Append(':');
            WriteValueAfterKey(sb, v, indent);
        }
    }

    private static void WriteValueAfterKey(StringBuilder sb, JsonNode? v, int indent)
    {
        switch (v)
        {
            case JsonObject co when co.Count > 0:
                sb.Append('\n');
                WriteObject(sb, co, indent + 2);
                break;
            case JsonArray ca when ca.Count > 0:
                sb.Append('\n');
                WriteArray(sb, ca, indent + 2);
                break;
            default:
                sb.Append(' ').Append(ScalarText(v)).Append('\n');
                break;
        }
    }

    private static void WriteArray(StringBuilder sb, JsonArray a, int indent)
    {
        foreach (var item in a)
        {
            sb.Append(' ', indent).Append('-');
            switch (item)
            {
                case JsonObject co when co.Count > 0:
                {
                    bool first = true;
                    foreach (var (k, v) in co)
                    {
                        if (first) { sb.Append(' '); first = false; }
                        else sb.Append(' ', indent + 2);
                        sb.Append(Key(k)).Append(':');
                        WriteValueAfterKey(sb, v, indent + 2);
                    }
                    break;
                }
                case JsonArray ca when ca.Count > 0:
                    sb.Append('\n');
                    WriteArray(sb, ca, indent + 2);
                    break;
                default:
                    sb.Append(' ').Append(ScalarText(item)).Append('\n');
                    break;
            }
        }
    }

    private static readonly Regex PlainSafe = new(@"^[A-Za-z_À-ÿ][A-Za-z0-9_ .\-/À-ÿ()']*$");

    private static string Key(string k) => NeedsQuotes(k) ? JsonSerializer.Serialize(k) : k;

    private static bool NeedsQuotes(string s)
    {
        if (s.Length == 0 || s != s.Trim()) return true;
        if (!PlainSafe.IsMatch(s)) return true;
        if (s.Contains(": ") || s.Contains(" #")) return true;
        // Une chaine qui serait relue comme autre chose (nombre, booleen, null)
        return Scalar(s, 0) is not JsonValue jv || jv.GetValueKind() != JsonValueKind.String;
    }

    private static string ScalarText(JsonNode? v)
    {
        if (v is null) return "null";
        if (v is JsonObject) return "{}";
        if (v is JsonArray) return "[]";
        var jv = (JsonValue)v;
        switch (jv.GetValueKind())
        {
            case JsonValueKind.String:
                var s = jv.GetValue<string>();
                return NeedsQuotes(s) ? JsonSerializer.Serialize(s, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) : s;
            case JsonValueKind.True: return "true";
            case JsonValueKind.False: return "false";
            case JsonValueKind.Number: return jv.ToJsonString();
            default: return "null";
        }
    }
}
