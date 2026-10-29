using System.Globalization;
using System.IO.Compression;
using System.Text;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Images;

namespace FileWheel.Conversion.Providers.Pdf;

/// <summary>
/// Ecriture de PDF simples, sans dependance : pages de texte (polices standard Helvetica /
/// Courier, encodage WinAnsi) et pages d'images (JPEG integre tel quel, PNG decode avec
/// transparence en masque doux). Produit des PDF 1.4 valides avec table de references croisees.
/// </summary>
public sealed class PdfWriter
{
    private readonly List<byte[]> _objects = new();
    private readonly List<int> _pages = new();
    private int _fontRegular = -1, _fontBold = -1, _fontMono = -1;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Title { get; set; } = "";
    public int UnmappableCharacters { get; private set; }

    private int Reserve()
    {
        _objects.Add([]);
        return _objects.Count; // numeros d'objet a partir de 1
    }

    private void Set(int id, string content) => _objects[id - 1] = Encoding.Latin1.GetBytes(content);

    private void SetStream(int id, string dict, byte[] data)
    {
        using var ms = new MemoryStream();
        var head = Encoding.Latin1.GetBytes($"<< {dict} /Length {data.Length} >>\nstream\n");
        ms.Write(head);
        ms.Write(data);
        ms.Write("\nendstream"u8);
        _objects[id - 1] = ms.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }

    private int Font(string baseFont)
    {
        int id = Reserve();
        Set(id, $"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} /Encoding /WinAnsiEncoding >>");
        return id;
    }

    // ------------------------------------------------------------------ texte

    public sealed record TextOptions(double FontSize = 11, bool Monospace = false, double PageWidth = 595.28, double PageHeight = 841.89, double Margin = 56);

    /// <summary>Ajoute un texte, mis en page sur autant de pages que necessaire.</summary>
    public void AddText(string text, TextOptions o, CancellationToken ct)
    {
        if (_fontRegular < 0) _fontRegular = Font("Helvetica");
        if (_fontMono < 0) _fontMono = Font("Courier");
        int font = o.Monospace ? _fontMono : _fontRegular;
        double lineHeight = o.FontSize * 1.35;
        double maxWidth = o.PageWidth - 2 * o.Margin;
        int linesPerPage = Math.Max(1, (int)((o.PageHeight - 2 * o.Margin) / lineHeight));

        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            var expanded = raw.Replace("\t", "    ");
            lines.AddRange(Wrap(expanded, maxWidth, o.FontSize, o.Monospace));
        }
        if (lines.Count == 0) lines.Add("");

        for (int start = 0; start < lines.Count; start += linesPerPage)
        {
            var sb = new StringBuilder();
            sb.Append("BT\n").Append($"/F1 {F(o.FontSize)} Tf\n{F(lineHeight)} TL\n");
            sb.Append($"{F(o.Margin)} {F(o.PageHeight - o.Margin - o.FontSize)} Td\n");
            foreach (var line in lines.Skip(start).Take(linesPerPage))
                sb.Append('(').Append(EscapeText(line)).Append(") Tj T*\n");
            sb.Append("ET\n");
            AddPage(o.PageWidth, o.PageHeight, Encoding.Latin1.GetBytes(sb.ToString()), $"/Font << /F1 {font} 0 R >>");
        }
    }

    private IEnumerable<string> Wrap(string line, double maxWidth, double size, bool mono)
    {
        if (line.Length == 0) { yield return ""; yield break; }
        var words = line.Split(' ');
        var cur = new StringBuilder();
        double curW = 0;
        double space = CharWidth(' ', mono) * size / 1000;
        foreach (var w in words)
        {
            double ww = TextWidth(w, size, mono);
            if (cur.Length > 0 && curW + space + ww > maxWidth)
            {
                yield return cur.ToString();
                cur.Clear();
                curW = 0;
            }
            if (ww > maxWidth)
            {
                // Mot plus long que la ligne : coupe dure
                foreach (var ch in w)
                {
                    double cw = CharWidth(ch, mono) * size / 1000;
                    if (curW + cw > maxWidth && cur.Length > 0) { yield return cur.ToString(); cur.Clear(); curW = 0; }
                    cur.Append(ch);
                    curW += cw;
                }
                continue;
            }
            if (cur.Length > 0) { cur.Append(' '); curW += space; }
            cur.Append(w);
            curW += ww;
        }
        yield return cur.ToString();
    }

    private static double TextWidth(string s, double size, bool mono)
    {
        double w = 0;
        foreach (var c in s) w += CharWidth(c, mono);
        return w * size / 1000;
    }

    // Largeurs Helvetica (metriques AFM des polices standard), caracteres 32 a 126
    private static readonly short[] HelveticaAscii =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static int CharWidth(char c, bool mono)
    {
        if (mono) return 600;
        if (c >= 32 && c <= 126) return HelveticaAscii[c - 32];
        return c switch
        {
            >= 'À' and <= 'Å' => 667, 'Æ' => 1000, 'Ç' => 722, >= 'È' and <= 'Ë' => 667, >= 'Ì' and <= 'Ï' => 278,
            'Ñ' => 722, >= 'Ò' and <= 'Ö' => 778, >= 'Ù' and <= 'Ü' => 722, 'ß' => 611,
            >= 'à' and <= 'å' => 556, 'æ' => 889, 'ç' => 500, >= 'è' and <= 'ë' => 556, >= 'ì' and <= 'ï' => 278,
            'ñ' => 556, >= 'ò' and <= 'ö' => 556, >= 'ù' and <= 'ü' => 556, 'ÿ' or 'ý' => 500,
            '—' or '…' or '‰' => 1000, '•' => 350, '°' => 400, '«' or '»' => 556, '€' => 556,
            '‘' or '’' => 222, '“' or '”' => 333, '–' => 556, 'Œ' => 1000, 'œ' => 944,
            _ => 556,
        };
    }

    private static readonly Encoding WinAnsi = CreateWinAnsi();

    private static Encoding CreateWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));
    }

    private string EscapeText(string s)
    {
        var bytes = WinAnsi.GetBytes(s);
        // Comptage des caracteres non representables (remplaces par « ? »)
        var back = WinAnsi.GetString(bytes);
        for (int i = 0; i < Math.Min(s.Length, back.Length); i++)
            if (back[i] == '?' && s[i] != '?') UnmappableCharacters++;
        var sb = new StringBuilder(bytes.Length + 8);
        foreach (var b in bytes)
        {
            char c = (char)b;
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ images

    public enum PageMode { Image, A4, Letter }

    /// <summary>Ajoute une page contenant l'image (JPEG ou PNG).</summary>
    public void AddImage(string path, PageMode mode, double margin, int dpi)
    {
        var bytes = File.ReadAllBytes(path);
        int imgId;
        int w, h;
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            var info = JpegInfo.Read(bytes);
            w = info.Width; h = info.Height;
            imgId = Reserve();
            var cs = info.Components switch { 1 => "/DeviceGray", 4 => "/DeviceCMYK", _ => "/DeviceRGB" };
            var decode = info.AdobeInverted ? " /Decode [1 0 1 0 1 0 1 0]" : "";
            SetStream(imgId, $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {cs} /BitsPerComponent 8 /Filter /DCTDecode{decode}", bytes);
        }
        else
        {
            RgbaImage img;
            using (var ms = new MemoryStream(bytes)) img = PngCodec.Decode(ms);
            w = img.Width; h = img.Height;
            var rgb = new byte[(long)w * h * 3];
            var alpha = img.HasAlpha ? new byte[(long)w * h] : null;
            for (long i = 0, j = 0; i < (long)w * h; i++)
            {
                rgb[j++] = img.Pixels[i * 4]; rgb[j++] = img.Pixels[i * 4 + 1]; rgb[j++] = img.Pixels[i * 4 + 2];
                if (alpha is not null) alpha[i] = img.Pixels[i * 4 + 3];
            }
            int smask = -1;
            if (alpha is not null && alpha.Any(a => a != 255))
            {
                smask = Reserve();
                SetStream(smask, $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", Deflate(alpha));
            }
            imgId = Reserve();
            SetStream(imgId, $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode{(smask > 0 ? $" /SMask {smask} 0 R" : "")}", Deflate(rgb));
        }

        double imgW = w * 72.0 / dpi, imgH = h * 72.0 / dpi;
        double pw, ph;
        switch (mode)
        {
            case PageMode.A4: (pw, ph) = imgW > imgH ? (841.89, 595.28) : (595.28, 841.89); break;
            case PageMode.Letter: (pw, ph) = imgW > imgH ? (792.0, 612.0) : (612.0, 792.0); break;
            default: (pw, ph) = (imgW + 2 * margin, imgH + 2 * margin); break;
        }
        double availW = pw - 2 * margin, availH = ph - 2 * margin;
        double scale = Math.Min(availW / imgW, availH / imgH);
        if (mode == PageMode.Image) scale = 1;
        double dw = imgW * scale, dh = imgH * scale;
        double x = (pw - dw) / 2, y = (ph - dh) / 2;
        var content = $"q {F(dw)} 0 0 {F(dh)} {F(x)} {F(y)} cm /Im0 Do Q\n";
        AddPage(pw, ph, Encoding.Latin1.GetBytes(content), $"/XObject << /Im0 {imgId} 0 R >>");
    }

    // ------------------------------------------------------------------ pages et sortie

    private void AddPage(double w, double h, byte[] content, string resources)
    {
        int contentId = Reserve();
        SetStream(contentId, "/Filter /FlateDecode", Deflate(content));
        int pageId = Reserve();
        // Le parent (/Pages) est complete a l'ecriture finale
        Set(pageId, $"<< /Type /Page /Parent §PAGES§ 0 R /MediaBox [0 0 {F(w)} {F(h)}] /Contents {contentId} 0 R /Resources << {resources} >> >>");
        _pages.Add(pageId);
    }

    public int PageCount => _pages.Count;

    public void Save(string path)
    {
        if (_pages.Count == 0) throw new ConversionException(ConversionErrorKind.WriteError, "Aucune page à écrire.");
        int pagesId = Reserve();
        int catalogId = Reserve();
        int infoId = Reserve();
        Set(pagesId, $"<< /Type /Pages /Kids [{string.Join(" ", _pages.Select(p => p + " 0 R"))}] /Count {_pages.Count} >>");
        Set(catalogId, $"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        Set(infoId, $"<< /Producer (FileWheel) /Title ({EscapeText(Title)}) /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>");
        foreach (var p in _pages)
            _objects[p - 1] = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(_objects[p - 1]).Replace("§PAGES§", pagesId.ToString()));

        using var fs = new FileStream(path, FileMode.CreateNew);
        var offsets = new long[_objects.Count + 1];
        void W(string s) => fs.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.4\n%âãÏÓ\n");
        for (int i = 0; i < _objects.Count; i++)
        {
            offsets[i + 1] = fs.Position;
            W($"{i + 1} 0 obj\n");
            fs.Write(_objects[i]);
            W("\nendobj\n");
        }
        long xref = fs.Position;
        W($"xref\n0 {_objects.Count + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= _objects.Count; i++) W($"{offsets[i]:D10} 00000 n \n");
        W($"trailer\n<< /Size {_objects.Count + 1} /Root {catalogId} 0 R /Info {infoId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static string F(double v) => v.ToString("0.###", Inv);
}
