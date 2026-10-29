using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FileWheel.Conversion.Core;
using FileWheel.Core.FileOperations;

namespace FileWheel.Conversion.Providers.Data;

/// <summary>
/// Lecture et ecriture XLSX minimales (Office Open XML), sans dependance.
/// Ecriture : une feuille, en-tete en gras, nombres types, textes en cellules « inlineStr ».
/// Lecture : valeurs des cellules (texte partage, nombres, booleens, dates selon le format),
/// sans formules recalculees (la derniere valeur enregistree est utilisee).
/// </summary>
public static class Xlsx
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void Write(string path, Table table, string sheetName = "Feuille1", bool inferNumbers = true)
    {
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Entry(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }

        Entry("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>");
        Entry("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        var safeSheet = SafeSheetName(sheetName);
        Entry("xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            $"<sheets><sheet name=\"{Esc(safeSheet)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Entry("xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>");
        Entry("xl/styles.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
            "</styleSheet>");

        var sheet = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var sw = new StreamWriter(sheet.Open(), new UTF8Encoding(false));
        sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sw.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetData>");
        int r = 1;
        WriteRow(sw, r++, table.Headers, header: true, inferNumbers: false);
        foreach (var row in table.Rows) WriteRow(sw, r++, row, header: false, inferNumbers);
        sw.Write("</sheetData></worksheet>");
    }

    private static void WriteRow(StreamWriter w, int rowNum, IReadOnlyList<string> cells, bool header, bool inferNumbers)
    {
        w.Write($"<row r=\"{rowNum}\">");
        for (int c = 0; c < cells.Count; c++)
        {
            var v = cells[c] ?? "";
            if (v.Length == 0) continue;
            var refName = ColumnName(c) + rowNum;
            var style = header ? " s=\"1\"" : "";
            if (inferNumbers && IsPlainNumber(v))
                w.Write($"<c r=\"{refName}\"{style}><v>{v}</v></c>");
            else
            {
                if (v.Length > 32_767) v = v[..32_767]; // limite Excel
                w.Write($"<c r=\"{refName}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Esc(CleanXml(v))}</t></is></c>");
            }
        }
        w.Write("</row>");
    }

    private static bool IsPlainNumber(string v)
    {
        if (v.Length > 15) return false;
        if (v.Length > 1 && v[0] == '0' && v[1] != '.') return false; // codes postaux, references
        return double.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d)
               && !double.IsInfinity(d) && !v.Contains(' ');
    }

    public static string ColumnName(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0)
        {
            int m = (index - 1) % 26;
            sb.Insert(0, (char)('A' + m));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }

    private static int ColumnIndex(string cellRef)
    {
        int idx = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            idx = idx * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return idx - 1;
    }

    private static string SafeSheetName(string s)
    {
        var bad = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim('\'');
        if (clean.Length == 0) clean = "Feuille1";
        return clean.Length > 31 ? clean[..31] : clean;
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string CleanXml(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (c is '\t' or '\n' or '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF)) sb.Append(c);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ lecture

    public sealed record SheetInfo(string Name, string Part);

    public static List<SheetInfo> Sheets(ZipArchive zip)
    {
        var wb = LoadPart(zip, "xl/workbook.xml") ?? throw new ConversionException(ConversionErrorKind.Corrupt, "Classeur XLSX invalide (workbook.xml absent).");
        var rels = LoadPart(zip, "xl/_rels/workbook.xml.rels");
        var map = rels?.Root?.Elements(PR + "Relationship").ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!) ?? new();
        var list = new List<SheetInfo>();
        foreach (var s in wb.Root!.Element(S + "sheets")?.Elements(S + "sheet") ?? [])
        {
            var rid = (string?)s.Attribute(R + "id");
            if (rid is null || !map.TryGetValue(rid, out var target)) continue;
            var part = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            list.Add(new SheetInfo((string?)s.Attribute("name") ?? "Feuille", part));
        }
        return list;
    }

    public static (Table table, int sheetCount, string sheetName) Read(string path, int sheetIndex, bool header)
    {
        using var zip = ZipFile.OpenRead(path);
        var sheets = Sheets(zip);
        if (sheets.Count == 0) throw new ConversionException(ConversionErrorKind.Corrupt, "Aucune feuille dans ce classeur.");
        if (sheetIndex < 1 || sheetIndex > sheets.Count)
            throw new ConversionException(ConversionErrorKind.InvalidOptions, $"Le classeur contient {sheets.Count} feuille(s) : numéro {sheetIndex} invalide.");
        var sheet = sheets[sheetIndex - 1];

        var shared = new List<string>();
        var sst = LoadPart(zip, "xl/sharedStrings.xml");
        if (sst?.Root is not null)
            foreach (var si in sst.Root.Elements(S + "si"))
                shared.Add(string.Concat(si.Descendants(S + "t").Select(t => t.Value)));

        var dateStyles = DateStyles(zip);
        var doc = LoadPart(zip, sheet.Part) ?? throw new ConversionException(ConversionErrorKind.Corrupt, "Feuille introuvable dans le classeur.");
        var rows = new List<List<string>>();
        foreach (var row in doc.Root!.Element(S + "sheetData")?.Elements(S + "row") ?? [])
        {
            int rIndex = int.TryParse((string?)row.Attribute("r"), out var rn) ? rn - 1 : rows.Count;
            while (rows.Count < rIndex) rows.Add(new List<string>());
            var cells = new List<string>();
            foreach (var c in row.Elements(S + "c"))
            {
                var cref = (string?)c.Attribute("r");
                int col = cref is null ? cells.Count : ColumnIndex(cref);
                while (cells.Count < col) cells.Add("");
                cells.Add(CellText(c, shared, dateStyles));
            }
            rows.Add(cells);
        }
        while (rows.Count > 0 && rows[^1].All(x => x.Length == 0)) rows.RemoveAt(rows.Count - 1);
        return (Tabular.FromRows(rows, header), sheets.Count, sheet.Name);
    }

    private static string CellText(XElement c, List<string> shared, HashSet<int> dateStyles)
    {
        var t = (string?)c.Attribute("t");
        var v = c.Element(S + "v")?.Value;
        switch (t)
        {
            case "s": return int.TryParse(v, out var i) && i >= 0 && i < shared.Count ? shared[i] : "";
            case "inlineStr": return string.Concat(c.Descendants(S + "t").Select(x => x.Value));
            case "b": return v == "1" ? "true" : "false";
            case "str": return v ?? "";
            case "e": return v ?? "#ERREUR";
        }
        if (v is null) return "";
        int style = int.TryParse((string?)c.Attribute("s"), out var s) ? s : 0;
        if (dateStyles.Contains(style) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 2958466)
        {
            var dt = DateTime.FromOADate(serial);
            return dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        return v;
    }

    private static HashSet<int> DateStyles(ZipArchive zip)
    {
        var result = new HashSet<int>();
        var styles = LoadPart(zip, "xl/styles.xml");
        if (styles?.Root is null) return result;
        var custom = new Dictionary<int, string>();
        foreach (var nf in styles.Root.Element(S + "numFmts")?.Elements(S + "numFmt") ?? [])
            if (int.TryParse((string?)nf.Attribute("numFmtId"), out var id)) custom[id] = (string?)nf.Attribute("formatCode") ?? "";
        int idx = 0;
        foreach (var xf in styles.Root.Element(S + "cellXfs")?.Elements(S + "xf") ?? [])
        {
            if (int.TryParse((string?)xf.Attribute("numFmtId"), out var fid))
            {
                bool builtinDate = fid is >= 14 and <= 22 or >= 45 and <= 47;
                bool customDate = custom.TryGetValue(fid, out var code) && IsDateFormat(code);
                if (builtinDate || customDate) result.Add(idx);
            }
            idx++;
        }
        return result;
    }

    private static bool IsDateFormat(string code)
    {
        // Retire les parties entre guillemets et crochets, puis cherche j/m/a/h/s
        var sb = new StringBuilder();
        bool q = false, b = false;
        foreach (var ch in code)
        {
            if (ch == '"') { q = !q; continue; }
            if (ch == '[') { b = true; continue; }
            if (ch == ']') { b = false; continue; }
            if (!q && !b) sb.Append(char.ToLowerInvariant(ch));
        }
        var s = sb.ToString();
        return s.Contains('y') || s.Contains('d') || (s.Contains('m') && (s.Contains('d') || s.Contains('y'))) || s.Contains("hh") || s.Contains("ss");
    }

    private static XDocument? LoadPart(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(x => x.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (e is null) return null;
        if (e.Length > 1L << 30) throw new ConversionException(ConversionErrorKind.TooLarge, "Feuille de calcul trop volumineuse.");
        using var s = e.Open();
        using var xr = XmlReader.Create(s, XmlJson.SafeSettings);
        return XDocument.Load(xr);
    }

    /// <summary>Nom de fichier CSV par feuille (« Classeur - Feuille.csv »).</summary>
    public static string SheetFileName(string stem, string sheet, string ext) =>
        PathSafety.SanitizeFileName($"{stem} - {sheet}") + "." + ext;
}
