using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FileWheel.Conversion.Providers.Text;

/// <summary>
/// Moteur Markdown integre (sous-ensemble CommonMark + tables, listes de taches et barre GFM).
/// Le HTML brut present dans la source est ECHAPPE (jamais recopie) et les liens dont le
/// protocole n'est pas http(s), mailto ou relatif sont neutralises : le resultat peut etre
/// ouvert sans risque d'execution de script.
/// </summary>
public static class Markdown
{
    public static string ToHtmlDocument(string markdown, string title)
    {
        var body = ToHtml(markdown);
        var t = Regex.Match(body, "<h1[^>]*>(.*?)</h1>") is { Success: true } m ? Regex.Replace(m.Groups[1].Value, "<[^>]+>", "") : WebUtility.HtmlEncode(title);
        return HtmlDocument(t, body);
    }

    public static string HtmlDocument(string title, string body) =>
        "<!DOCTYPE html>\n<html lang=\"fr\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
        $"<title>{title}</title>\n<style>\n" +
        "body{font-family:'Segoe UI',system-ui,sans-serif;line-height:1.6;max-width:820px;margin:2rem auto;padding:0 1rem;color:#1b1b1f}\n" +
        "pre,code{font-family:Consolas,'Cascadia Mono',monospace;background:#f3f3f5;border-radius:4px}\n" +
        "code{padding:.1em .3em}pre{padding:.8em;overflow:auto}pre code{padding:0;background:none}\n" +
        "blockquote{border-left:4px solid #c8c8d0;margin:0;padding:.2em 1em;color:#555}\n" +
        "table{border-collapse:collapse}th,td{border:1px solid #c8c8d0;padding:.3em .6em}th{background:#f3f3f5}\n" +
        "img{max-width:100%}hr{border:0;border-top:1px solid #ddd}li.task{list-style:none}\n" +
        "</style>\n</head>\n<body>\n" + body + "\n</body>\n</html>\n";

    public static string ToHtml(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n').ToList();
        var sb = new StringBuilder();
        RenderBlocks(lines, sb);
        return sb.ToString().TrimEnd();
    }

    private static readonly Regex Atx = new(@"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$");
    private static readonly Regex Hr = new(@"^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$");
    private static readonly Regex Fence = new(@"^( {0,3})(`{3,}|~{3,})\s*([^`\s]*)");
    private static readonly Regex Bullet = new(@"^( {0,3})([-*+])( +|$)(.*)$");
    private static readonly Regex Ordered = new(@"^( {0,3})(\d{1,9})([.)])( +|$)(.*)$");
    private static readonly Regex TableSep = new(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$");

    private static void RenderBlocks(List<string> lines, StringBuilder sb)
    {
        int i = 0;
        var para = new List<string>();
        void FlushPara()
        {
            if (para.Count == 0) return;
            sb.Append("<p>").Append(Inline(string.Join("\n", para).Trim())).Append("</p>\n");
            para.Clear();
        }

        while (i < lines.Count)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) { FlushPara(); i++; continue; }

            // Bloc de code delimite
            var f = Fence.Match(line);
            if (f.Success)
            {
                FlushPara();
                var fence = f.Groups[2].Value;
                var lang = f.Groups[3].Value;
                var code = new List<string>();
                i++;
                while (i < lines.Count && !lines[i].TrimStart().StartsWith(fence)) { code.Add(lines[i]); i++; }
                i++;
                var cls = lang.Length > 0 ? $" class=\"language-{WebUtility.HtmlEncode(lang)}\"" : "";
                sb.Append($"<pre><code{cls}>").Append(WebUtility.HtmlEncode(string.Join("\n", code))).Append("</code></pre>\n");
                continue;
            }
            // Code indente (hors paragraphe)
            if (para.Count == 0 && line.StartsWith("    "))
            {
                var code = new List<string>();
                while (i < lines.Count && (lines[i].StartsWith("    ") || lines[i].Trim().Length == 0)) { code.Add(lines[i].Length >= 4 ? lines[i][4..] : ""); i++; }
                while (code.Count > 0 && code[^1].Length == 0) code.RemoveAt(code.Count - 1);
                sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(string.Join("\n", code))).Append("</code></pre>\n");
                continue;
            }
            var h = Atx.Match(line);
            if (h.Success)
            {
                FlushPara();
                int level = h.Groups[1].Length;
                sb.Append($"<h{level}>").Append(Inline(h.Groups[2].Value.Trim())).Append($"</h{level}>\n");
                i++;
                continue;
            }
            // Titre souligne (setext)
            if (para.Count > 0 && Regex.IsMatch(line, @"^ {0,3}(=+|-+)\s*$"))
            {
                int level = line.Trim()[0] == '=' ? 1 : 2;
                var text = string.Join(" ", para).Trim();
                para.Clear();
                sb.Append($"<h{level}>").Append(Inline(text)).Append($"</h{level}>\n");
                i++;
                continue;
            }
            if (Hr.IsMatch(line)) { FlushPara(); sb.Append("<hr>\n"); i++; continue; }
            if (line.TrimStart().StartsWith('>'))
            {
                FlushPara();
                var inner = new List<string>();
                while (i < lines.Count && lines[i].Trim().Length > 0)
                {
                    var l = lines[i].TrimStart();
                    if (l.StartsWith('>')) l = l.Length > 1 && l[1] == ' ' ? l[2..] : l[1..];
                    inner.Add(l);
                    i++;
                }
                sb.Append("<blockquote>\n");
                RenderBlocks(inner, sb);
                sb.Append("\n</blockquote>\n");
                continue;
            }
            // Table GFM
            if (line.Contains('|') && i + 1 < lines.Count && TableSep.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushPara();
                var headers = SplitRow(line);
                var aligns = SplitRow(lines[i + 1]).Select(c => c.StartsWith(':') && c.EndsWith(':') ? "center" : c.EndsWith(':') ? "right" : c.StartsWith(':') ? "left" : "").ToList();
                sb.Append("<table>\n<thead><tr>");
                for (int c = 0; c < headers.Count; c++) sb.Append(Cell("th", headers[c], c < aligns.Count ? aligns[c] : ""));
                sb.Append("</tr></thead>\n<tbody>\n");
                i += 2;
                while (i < lines.Count && lines[i].Contains('|') && lines[i].Trim().Length > 0)
                {
                    var cells = SplitRow(lines[i]);
                    sb.Append("<tr>");
                    for (int c = 0; c < headers.Count; c++) sb.Append(Cell("td", c < cells.Count ? cells[c] : "", c < aligns.Count ? aligns[c] : ""));
                    sb.Append("</tr>\n");
                    i++;
                }
                sb.Append("</tbody>\n</table>\n");
                continue;
            }
            // Listes
            var b = Bullet.Match(line);
            var o = Ordered.Match(line);
            if ((b.Success && !(para.Count > 0 && b.Groups[4].Value.Length == 0)) || (o.Success && (para.Count == 0 || o.Groups[2].Value == "1")))
            {
                FlushPara();
                i = RenderList(lines, i, sb);
                continue;
            }
            para.Add(line);
            i++;
        }
        FlushPara();
    }

    private static string Cell(string tag, string content, string align) =>
        $"<{tag}{(align.Length > 0 ? $" style=\"text-align:{align}\"" : "")}>{Inline(content.Trim())}</{tag}>";

    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|")) t = t[..^1];
        var cells = new List<string>();
        var cur = new StringBuilder();
        for (int k = 0; k < t.Length; k++)
        {
            if (t[k] == '\\' && k + 1 < t.Length && t[k + 1] == '|') { cur.Append('|'); k++; continue; }
            if (t[k] == '|') { cells.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(t[k]);
        }
        cells.Add(cur.ToString());
        return cells.Select(c => c.Trim()).ToList();
    }

    private static int RenderList(List<string> lines, int i, StringBuilder sb)
    {
        var first = lines[i];
        var ob = Ordered.Match(first);
        bool ordered = ob.Success;
        int start = ordered ? int.Parse(ob.Groups[2].Value) : 1;
        sb.Append(ordered ? (start != 1 ? $"<ol start=\"{start}\">\n" : "<ol>\n") : "<ul>\n");
        bool loose = false;
        var items = new List<List<string>>();
        while (i < lines.Count)
        {
            var line = lines[i];
            var m = ordered ? Ordered.Match(line) : Bullet.Match(line);
            if (!m.Success) break;
            int markerIndent = m.Groups[1].Length;
            string rest = ordered ? m.Groups[5].Value : m.Groups[4].Value;
            int contentIndent = ordered ? markerIndent + m.Groups[2].Length + 1 + Math.Max(1, m.Groups[4].Length) : markerIndent + 1 + Math.Max(1, m.Groups[3].Length);
            var item = new List<string> { rest };
            i++;
            bool sawBlank = false;
            while (i < lines.Count)
            {
                var l = lines[i];
                if (l.Trim().Length == 0)
                {
                    sawBlank = true;
                    item.Add("");
                    i++;
                    continue;
                }
                int ind = l.Length - l.TrimStart().Length;
                if (ind >= contentIndent) { item.Add(l[Math.Min(contentIndent, l.Length)..]); i++; sawBlank = false; continue; }
                bool newItem = (ordered ? Ordered.IsMatch(l) : Bullet.IsMatch(l)) && ind <= markerIndent + 1;
                if (newItem) { if (sawBlank) loose = true; break; }
                if (sawBlank) break; // fin de liste
                // Continuation paresseuse du paragraphe
                if (!Hr.IsMatch(l) && !Atx.IsMatch(l) && !Fence.IsMatch(l) && !l.TrimStart().StartsWith('>')) { item.Add(l.Trim()); i++; continue; }
                break;
            }
            while (item.Count > 0 && item[^1].Length == 0) item.RemoveAt(item.Count - 1);
            items.Add(item);
            if (i < lines.Count && lines[i].Trim().Length == 0) break;
        }
        foreach (var item in items)
        {
            var text = item[0];
            string cls = "";
            string checkbox = "";
            var task = Regex.Match(text, @"^\[([ xX])\]\s+(.*)$");
            if (task.Success)
            {
                cls = " class=\"task\"";
                checkbox = task.Groups[1].Value.Trim().Length > 0 ? "<input type=\"checkbox\" checked disabled> " : "<input type=\"checkbox\" disabled> ";
                item[0] = task.Groups[2].Value;
            }
            var inner = new StringBuilder();
            RenderBlocks(item, inner);
            var html = inner.ToString().Trim();
            if (!loose && html.StartsWith("<p>"))
            {
                // Liste serree : le premier paragraphe n'est pas encapsule
                int end = html.IndexOf("</p>", StringComparison.Ordinal);
                html = html[3..end] + html[(end + 4)..];
            }
            sb.Append($"<li{cls}>").Append(checkbox).Append(html.Trim()).Append("</li>\n");
        }
        sb.Append(ordered ? "</ol>\n" : "</ul>\n");
        return i;
    }

    // ------------------------------------------------------------------ en ligne

    private const string Punct = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    public static string Inline(string s)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                if (s[i + 1] == '\n') { sb.Append("<br>\n"); i += 2; continue; }
                if (Punct.Contains(s[i + 1])) { sb.Append(WebUtility.HtmlEncode(s[i + 1].ToString())); i += 2; continue; }
            }
            if (c == '`')
            {
                int run = 0;
                while (i + run < s.Length && s[i + run] == '`') run++;
                var closer = new string('`', run);
                int end = s.IndexOf(closer, i + run, StringComparison.Ordinal);
                if (end > 0)
                {
                    var code = s[(i + run)..end];
                    if (code.Length > 1 && code.StartsWith(' ') && code.EndsWith(' ')) code = code[1..^1];
                    sb.Append("<code>").Append(WebUtility.HtmlEncode(code)).Append("</code>");
                    i = end + run;
                    continue;
                }
                sb.Append(closer);
                i += run;
                continue;
            }
            if ((c == '!' && i + 1 < s.Length && s[i + 1] == '[') || c == '[')
            {
                bool image = c == '!';
                int open = image ? i + 1 : i;
                int close = FindBracket(s, open);
                if (close > 0 && close + 1 < s.Length && s[close + 1] == '(')
                {
                    int paren = FindParen(s, close + 1);
                    if (paren > 0)
                    {
                        var text = s[(open + 1)..close];
                        var target = s[(close + 2)..paren].Trim();
                        string? title = null;
                        var tm = Regex.Match(target, "^(\\S+)\\s+\"(.*)\"$");
                        if (tm.Success) { target = tm.Groups[1].Value; title = tm.Groups[2].Value; }
                        if (target.StartsWith('<') && target.EndsWith('>')) target = target[1..^1];
                        var url = SafeUrl(target, image);
                        var titleAttr = title is null ? "" : $" title=\"{WebUtility.HtmlEncode(title)}\"";
                        if (url is null) sb.Append(Inline(text));
                        else if (image) sb.Append($"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"{WebUtility.HtmlEncode(StripMd(text))}\"{titleAttr}>");
                        else sb.Append($"<a href=\"{WebUtility.HtmlEncode(url)}\"{titleAttr}>").Append(Inline(text)).Append("</a>");
                        i = paren + 1;
                        continue;
                    }
                }
            }
            if (c == '<')
            {
                var auto = Regex.Match(s[i..], @"^<((?:https?://|mailto:)[^\s<>]+)>");
                if (auto.Success)
                {
                    var u = auto.Groups[1].Value;
                    sb.Append($"<a href=\"{WebUtility.HtmlEncode(u)}\">{WebUtility.HtmlEncode(u)}</a>");
                    i += auto.Length;
                    continue;
                }
            }
            if (c is '*' or '_' or '~')
            {
                var (html, consumed) = Emphasis(s, i);
                if (consumed > 0) { sb.Append(html); i += consumed; continue; }
            }
            if (c == '\n')
            {
                // Saut de ligne force : deux espaces en fin de ligne
                if (sb.Length >= 2 && sb[^1] == ' ' && sb[^2] == ' ') { sb.Length -= 2; sb.Append("<br>\n"); }
                else sb.Append('\n');
                i++;
                continue;
            }
            // Adresse web nue
            if ((c == 'h' || c == 'w') && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))
            {
                var bare = Regex.Match(s[i..], @"^(?:https?://|www\.)[^\s<>()]+[^\s<>().,;:!?]");
                if (bare.Success)
                {
                    var u = bare.Value.StartsWith("www.") ? "https://" + bare.Value : bare.Value;
                    sb.Append($"<a href=\"{WebUtility.HtmlEncode(u)}\">{WebUtility.HtmlEncode(bare.Value)}</a>");
                    i += bare.Length;
                    continue;
                }
            }
            sb.Append(WebUtility.HtmlEncode(c.ToString()));
            i++;
        }
        return sb.ToString();
    }

    private static (string html, int consumed) Emphasis(string s, int i)
    {
        foreach (var (delim, tag) in new[] { ("**", "strong"), ("__", "strong"), ("~~", "del"), ("*", "em"), ("_", "em") })
        {
            if (!s.AsSpan(i).StartsWith(delim)) continue;
            int contentStart = i + delim.Length;
            if (contentStart >= s.Length || char.IsWhiteSpace(s[contentStart])) continue;
            if (delim[0] == '_' && i > 0 && char.IsLetterOrDigit(s[i - 1])) continue; // intra-mot
            int search = contentStart;
            while (true)
            {
                int end = s.IndexOf(delim, search, StringComparison.Ordinal);
                if (end < 0) break;
                if (end == contentStart) { search = end + 1; continue; }
                bool okEnd = !char.IsWhiteSpace(s[end - 1]);
                if (delim.Length == 1 && end + 1 < s.Length && s[end + 1] == delim[0]) { search = end + 2; continue; } // fait partie de « ** »
                if (delim[0] == '_' && end + delim.Length < s.Length && char.IsLetterOrDigit(s[end + delim.Length])) { search = end + 1; continue; }
                if (okEnd)
                {
                    var inner = s[contentStart..end];
                    return ($"<{tag}>{Inline(inner)}</{tag}>", end + delim.Length - i);
                }
                search = end + 1;
            }
        }
        return ("", 0);
    }

    private static int FindBracket(string s, int open)
    {
        int depth = 0;
        for (int k = open; k < s.Length; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '[') depth++;
            else if (s[k] == ']' && --depth == 0) return k;
        }
        return -1;
    }

    private static int FindParen(string s, int open)
    {
        int depth = 0;
        for (int k = open; k < s.Length; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '(') depth++;
            else if (s[k] == ')' && --depth == 0) return k;
            else if (s[k] == '\n') return -1;
        }
        return -1;
    }

    /// <summary>N'autorise que http(s), mailto, ancres et chemins relatifs (images : aussi data:image).</summary>
    public static string? SafeUrl(string url, bool image)
    {
        var u = url.Trim();
        var lower = u.ToLowerInvariant();
        if (lower.StartsWith("http://") || lower.StartsWith("https://") || lower.StartsWith("mailto:") || lower.StartsWith('#')) return u;
        if (image && lower.StartsWith("data:image/") && !lower.StartsWith("data:image/svg")) return u;
        if (Regex.IsMatch(lower, @"^[a-z][a-z0-9+.\-]*:")) return null; // javascript:, file:, vbscript:...
        return u; // relatif
    }

    private static string StripMd(string s) => Regex.Replace(s, @"[*_`\[\]]", "");
}
