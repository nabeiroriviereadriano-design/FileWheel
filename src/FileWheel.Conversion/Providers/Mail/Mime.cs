using System.Text;
using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Data;

namespace FileWheel.Conversion.Providers.Mail;

/// <summary>Analyse MIME minimale (RFC 2045-2047) pour lire un e-mail sans l'executer.</summary>
public static class Mime
{
    public sealed class Part
    {
        public List<(string Name, string Value)> Headers { get; } = new();
        public byte[] Body { get; set; } = [];
        public List<Part> Children { get; } = new();

        public string? Header(string name) => Headers.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

        public string ContentType => (Header("Content-Type") ?? "text/plain").Split(';')[0].Trim().ToLowerInvariant();

        public string? Param(string header, string param)
        {
            var h = Header(header);
            if (h is null) return null;
            var m = Regex.Match(h, $@"(?:^|;)\s*{Regex.Escape(param)}\*?\s*=\s*(""(?<v>[^""]*)""|(?<v>[^;\s]+))", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value : null;
        }
    }

    private const int MaxDepth = 20;

    public static Part Parse(byte[] data) => ParsePart(data, 0);

    private static Part ParsePart(byte[] data, int depth)
    {
        if (depth > MaxDepth) throw new ConversionException(ConversionErrorKind.Corrupt, "Message MIME trop imbriqué.");
        var text = Encoding.Latin1.GetString(data);
        int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int sepLen = 4;
        int lf = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (split < 0 || (lf >= 0 && lf < split)) { split = lf; sepLen = 2; }
        var part = new Part();
        var headerText = split >= 0 ? text[..split] : text;
        var bodyText = split >= 0 ? text[(split + sepLen)..] : "";
        var unfolded = Regex.Replace(headerText.Replace("\r\n", "\n"), @"\n[ \t]+", " ");
        foreach (var line in unfolded.Split('\n'))
        {
            int c = line.IndexOf(':');
            if (c > 0) part.Headers.Add((line[..c].Trim(), line[(c + 1)..].Trim()));
        }
        part.Body = Encoding.Latin1.GetBytes(bodyText);
        if (part.ContentType.StartsWith("multipart/") && part.Param("Content-Type", "boundary") is { Length: > 0 } boundary)
        {
            var delim = "--" + boundary;
            var pieces = bodyText.Split(delim);
            foreach (var piece in pieces.Skip(1))
            {
                if (piece.StartsWith("--")) break; // fin
                var content = piece.TrimStart('\r', '\n');
                part.Children.Add(ParsePart(Encoding.Latin1.GetBytes(content), depth + 1));
                if (part.Children.Count > 500) break;
            }
        }
        else if (part.ContentType == "message/rfc822")
        {
            part.Children.Add(ParsePart(DecodeBody(part), depth + 1));
        }
        return part;
    }

    public static byte[] DecodeBody(Part p)
    {
        var cte = (p.Header("Content-Transfer-Encoding") ?? "7bit").Trim().ToLowerInvariant();
        var raw = Encoding.Latin1.GetString(p.Body);
        switch (cte)
        {
            case "base64":
                try { return Convert.FromBase64String(Regex.Replace(raw, @"[^A-Za-z0-9+/=]", "")); }
                catch (FormatException) { return p.Body; }
            case "quoted-printable":
                return QpBytes(raw, header: false);
            default:
                return p.Body;
        }
    }

    public static string BodyText(Part p)
    {
        var bytes = DecodeBody(p);
        var charset = p.Param("Content-Type", "charset") ?? "utf-8";
        return GetEncoding(charset).GetString(bytes);
    }

    public static (string? plain, string? html) Bodies(Part root)
    {
        string? plain = null, html = null;
        void Walk(Part p)
        {
            if (IsAttachment(p)) return;
            if (p.Children.Count > 0) { foreach (var c in p.Children) Walk(c); return; }
            if (p.ContentType == "text/plain" && plain is null) plain = BodyText(p);
            else if (p.ContentType == "text/html" && html is null) html = BodyText(p);
        }
        Walk(root);
        return (plain, html);
    }

    public static List<string> Attachments(Part root)
    {
        var list = new List<string>();
        void Walk(Part p)
        {
            if (p.Children.Count > 0) { foreach (var c in p.Children) Walk(c); return; }
            if (IsAttachment(p))
            {
                var name = DecodeWords(p.Param("Content-Disposition", "filename") ?? p.Param("Content-Type", "name") ?? "(sans nom)");
                list.Add($"{name} ({p.ContentType}, environ {FileWheel.Core.FileOperations.SafeFileOps.FormatSize(p.Body.Length * 3L / 4)})");
            }
        }
        Walk(root);
        return list;
    }

    private static bool IsAttachment(Part p)
    {
        var disp = (p.Header("Content-Disposition") ?? "").ToLowerInvariant();
        if (disp.StartsWith("attachment")) return true;
        return p.Children.Count == 0 && !p.ContentType.StartsWith("text/") && !p.ContentType.StartsWith("multipart/");
    }

    /// <summary>Decode les mots encodes des en-tetes (=?utf-8?B?...?= / =?iso-8859-1?Q?...?=).</summary>
    public static string DecodeWords(string s)
    {
        var r = Regex.Replace(s, @"=\?([^?]+)\?([bBqQ])\?([^?]*)\?=\s*(?==\?)", "=?$1?$2?$3?=");
        return Regex.Replace(r, @"=\?([^?]+)\?([bBqQ])\?([^?]*)\?=", m =>
        {
            var enc = GetEncoding(m.Groups[1].Value);
            try
            {
                return m.Groups[2].Value.ToUpperInvariant() == "B"
                    ? enc.GetString(Convert.FromBase64String(m.Groups[3].Value))
                    : enc.GetString(QpBytes(m.Groups[3].Value, header: true));
            }
            catch { return m.Value; }
        });
    }

    public static string QuotedPrintable(string s, string charset, bool header) => GetEncoding(charset).GetString(QpBytes(s, header));

    private static byte[] QpBytes(string s, bool header)
    {
        var ms = new MemoryStream();
        s = Regex.Replace(s, "=\r?\n", "");
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '=' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
            {
                ms.WriteByte(Convert.ToByte(s.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (header && c == '_') ms.WriteByte(0x20);
            else ms.WriteByte((byte)c);
        }
        return ms.ToArray();
    }

    public static Encoding GetEncoding(string charset)
    {
        _ = TextIO.Windows1252; // enregistre les pages de code
        try { return Encoding.GetEncoding(charset.Trim().Trim('"')); }
        catch { return Encoding.UTF8; }
    }
}
