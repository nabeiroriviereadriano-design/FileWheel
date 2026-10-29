using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Data;

namespace FileWheel.Conversion.Providers.Text;

/// <summary>
/// Lecture des livres EPUB (sans DRM) : ordre de lecture (spine), contenu XHTML, images.
/// Un EPUB chiffre (META-INF/encryption.xml portant sur autre chose que des polices) est refuse :
/// FileWheel ne contourne aucune protection.
/// </summary>
public static class Epub
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Cont = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Enc = "http://www.w3.org/2001/04/xmlenc#";

    // Algorithmes d'obfuscation de polices (autorises : ce n'est pas un DRM de contenu)
    private static readonly string[] FontObfuscation = ["http://www.idpf.org/2008/embedding", "http://ns.adobe.com/pdf/enc#RC"];

    public sealed record Book(string Title, string? Author, List<(string Href, string Html)> Chapters, Dictionary<string, (string Mime, byte[] Data)> Images);

    public static bool IsDrmProtected(ZipArchive zip, out string reason)
    {
        reason = "";
        var enc = zip.GetEntry("META-INF/encryption.xml");
        if (enc is not null)
        {
            var doc = Load(enc);
            var algos = doc.Descendants(Enc + "EncryptionMethod").Select(e => (string?)e.Attribute("Algorithm") ?? "").ToList();
            if (algos.Any(a => !FontObfuscation.Contains(a)))
            {
                reason = "Ce livre est protégé par un DRM : FileWheel ne contourne pas les protections.";
                return true;
            }
        }
        if (zip.GetEntry("META-INF/rights.xml") is not null)
        {
            reason = "Ce livre contient une licence DRM (rights.xml) : conversion impossible.";
            return true;
        }
        return false;
    }

    public static Book Read(string path, bool loadImages, long maxImageBytes = 64L << 20)
    {
        using var zip = ZipFile.OpenRead(path);
        if (IsDrmProtected(zip, out var reason)) throw new ConversionException(ConversionErrorKind.Protected, reason);
        var container = zip.GetEntry("META-INF/container.xml") ?? throw new ConversionException(ConversionErrorKind.Corrupt, "EPUB invalide : container.xml absent.");
        var opfPath = (string?)Load(container).Descendants(Cont + "rootfile").FirstOrDefault()?.Attribute("full-path")
                      ?? throw new ConversionException(ConversionErrorKind.Corrupt, "EPUB invalide : fichier OPF introuvable.");
        var opfEntry = zip.GetEntry(opfPath) ?? throw new ConversionException(ConversionErrorKind.Corrupt, "EPUB invalide : OPF manquant.");
        var opf = Load(opfEntry);
        var baseDir = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";
        var title = opf.Descendants(Dc + "title").FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(path);
        var author = opf.Descendants(Dc + "creator").FirstOrDefault()?.Value;
        var manifest = opf.Descendants(Opf + "item").ToDictionary(
            i => (string?)i.Attribute("id") ?? "",
            i => (Href: (string?)i.Attribute("href") ?? "", Mime: (string?)i.Attribute("media-type") ?? ""));
        var chapters = new List<(string, string)>();
        foreach (var itemref in opf.Descendants(Opf + "itemref"))
        {
            var idref = (string?)itemref.Attribute("idref");
            if (idref is null || !manifest.TryGetValue(idref, out var item)) continue;
            var href = Resolve(baseDir, item.Href);
            var e = zip.GetEntry(href);
            if (e is null) continue;
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            chapters.Add((href, r.ReadToEnd()));
        }
        var images = new Dictionary<string, (string, byte[])>(StringComparer.OrdinalIgnoreCase);
        if (loadImages)
        {
            long total = 0;
            foreach (var (_, it) in manifest)
            {
                if (!it.Mime.StartsWith("image/") || it.Mime.Contains("svg")) continue;
                var href = Resolve(baseDir, it.Href);
                var e = zip.GetEntry(href);
                if (e is null || e.Length > 16L << 20) continue;
                total += e.Length;
                if (total > maxImageBytes) break;
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                images[href] = (it.Mime, ms.ToArray());
            }
        }
        return new Book(title, author, chapters, images);
    }

    private static string Resolve(string baseDir, string href)
    {
        href = Uri.UnescapeDataString(href.Split('#')[0]);
        var parts = (baseDir + href).Split('/').ToList();
        var stack = new List<string>();
        foreach (var p in parts)
        {
            if (p == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
            else if (p != "." && p.Length > 0) stack.Add(p);
        }
        return string.Join('/', stack);
    }

    private static XDocument Load(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var xr = XmlReader.Create(s, XmlJson.SafeSettings);
        return XDocument.Load(xr);
    }

    /// <summary>Un seul document HTML autonome (images incluses en data: URI).</summary>
    public static string ToHtml(Book book)
    {
        var body = new StringBuilder();
        body.Append("<header><h1>").Append(System.Net.WebUtility.HtmlEncode(book.Title)).Append("</h1>");
        if (book.Author is not null) body.Append("<p><em>").Append(System.Net.WebUtility.HtmlEncode(book.Author)).Append("</em></p>");
        body.Append("</header>\n");
        foreach (var (href, html) in book.Chapters)
        {
            var m = System.Text.RegularExpressions.Regex.Match(html, @"<body[^>]*>(.*)</body>", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var content = m.Success ? m.Groups[1].Value : html;
            // Scripts et gestionnaires d'evenements retires
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<script\b.*?</script\s*>", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\son\w+\s*=\s*(""[^""]*""|'[^']*')", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var dir = href.Contains('/') ? href[..(href.LastIndexOf('/') + 1)] : "";
            content = System.Text.RegularExpressions.Regex.Replace(content, @"(src|xlink:href)\s*=\s*""([^""]+)""", mm =>
            {
                var target = Resolve(dir, mm.Groups[2].Value);
                return book.Images.TryGetValue(target, out var img)
                    ? $"{mm.Groups[1].Value}=\"data:{img.Mime};base64,{Convert.ToBase64String(img.Data)}\""
                    : mm.Value;
            });
            body.Append("<section>\n").Append(content).Append("\n</section>\n");
        }
        return Markdown.HtmlDocument(System.Net.WebUtility.HtmlEncode(book.Title), body.ToString());
    }

    public static string ToText(Book book)
    {
        var sb = new StringBuilder();
        sb.Append(book.Title).Append('\n');
        if (book.Author is not null) sb.Append(book.Author).Append('\n');
        sb.Append('\n');
        foreach (var (_, html) in book.Chapters) sb.Append(HtmlText.ToText(html)).Append('\n');
        return sb.ToString();
    }
}
