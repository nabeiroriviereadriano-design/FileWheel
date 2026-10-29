using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FileWheel.Conversion.Providers.Text;

/// <summary>Extraction du texte lisible d'un document HTML (sans moteur de rendu).</summary>
public static class HtmlText
{
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "blockquote", "pre", "table", "ul", "ol",
        "h1", "h2", "h3", "h4", "h5", "h6", "form", "fieldset", "address", "figure", "figcaption", "dl", "dt", "dd", "hr", "tr",
    };

    public static string ToText(string html)
    {
        // Contenu jamais affiche : scripts, styles, en-tete, commentaires
        html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<(script|style|head|template|noscript|svg)\b.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var sb = new StringBuilder();
        bool inPre = false;
        int i = 0;
        var olCounters = new Stack<int>();
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                int end = html.IndexOf('>', i);
                if (end < 0) break;
                var tag = html[(i + 1)..end].Trim();
                bool closing = tag.StartsWith('/');
                var name = Regex.Match(tag.TrimStart('/'), @"^[a-zA-Z0-9]+").Value.ToLowerInvariant();
                i = end + 1;
                switch (name)
                {
                    case "br": sb.Append('\n'); continue;
                    case "pre": inPre = !closing; sb.Append("\n\n"); continue;
                    case "li":
                        if (!closing)
                        {
                            sb.Append('\n');
                            if (olCounters.Count > 0 && olCounters.Peek() > 0) { int n = olCounters.Pop(); sb.Append(n).Append(". "); olCounters.Push(n + 1); }
                            else sb.Append("• ");
                        }
                        continue;
                    case "ol": if (!closing) olCounters.Push(1); else if (olCounters.Count > 0) olCounters.Pop(); sb.Append('\n'); continue;
                    case "ul": if (!closing) olCounters.Push(0); else if (olCounters.Count > 0) olCounters.Pop(); sb.Append('\n'); continue;
                    case "td": case "th": if (!closing) sb.Append('\t'); continue;
                    case "hr": sb.Append("\n----------\n"); continue;
                    case "img":
                        var alt = Regex.Match(tag, "alt\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        if (alt.Success && alt.Groups[1].Value.Length > 0) sb.Append('[').Append(WebUtility.HtmlDecode(alt.Groups[1].Value)).Append(']');
                        continue;
                }
                if (Blocks.Contains(name)) sb.Append(name.StartsWith('h') && name.Length == 2 ? "\n\n" : "\n");
                continue;
            }
            int next = html.IndexOf('<', i);
            if (next < 0) next = html.Length;
            var text = WebUtility.HtmlDecode(html[i..next]);
            if (!inPre) text = Regex.Replace(text, @"\s+", " ");
            sb.Append(text);
            i = next;
        }
        // Nettoyage : espaces en bout de ligne, 2 lignes vides maximum
        var lines = sb.ToString().Replace("\r", "").Split('\n').Select(l => l.TrimEnd().TrimStart(' ')).ToList();
        var outSb = new StringBuilder();
        int blank = 0;
        foreach (var l in lines)
        {
            if (l.Trim().Length == 0) { if (++blank <= 1 && outSb.Length > 0) outSb.Append('\n'); continue; }
            blank = 0;
            outSb.Append(l).Append('\n');
        }
        return outSb.ToString().Trim() + "\n";
    }

    /// <summary>Texte brut -&gt; HTML (paragraphes et sauts de ligne, contenu echappe).</summary>
    public static string FromText(string text, string title)
    {
        var paras = Regex.Split(text.Replace("\r\n", "\n").Trim(), @"\n\s*\n");
        var body = new StringBuilder();
        foreach (var p in paras)
            body.Append("<p>").Append(WebUtility.HtmlEncode(p).Replace("\n", "<br>\n")).Append("</p>\n");
        return Markdown.HtmlDocument(WebUtility.HtmlEncode(title), body.ToString());
    }
}
