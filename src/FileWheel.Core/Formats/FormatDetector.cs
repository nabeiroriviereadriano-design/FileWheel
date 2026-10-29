using System.IO.Compression;
using System.Text;

namespace FileWheel.Core.Formats;

public sealed record DetectionResult
{
    /// <summary>Format retenu (null = non reconnu).</summary>
    public FormatInfo? Format { get; init; }
    /// <summary>Format suggere par l'extension seule.</summary>
    public FormatInfo? ExtensionFormat { get; init; }
    /// <summary>Comment le format a ete determine : signature, contenu, extension.</summary>
    public string Method { get; init; } = "";
    /// <summary>Message a afficher a l'utilisateur (contradiction, fichier endommage...).</summary>
    public string? Warning { get; init; }
    /// <summary>Le contenu ne correspond pas a l'extension.</summary>
    public bool Mismatch { get; init; }
    /// <summary>Le fichier semble endommage ou tronque.</summary>
    public bool SuspectedCorrupt { get; init; }
    /// <summary>Le contenu est un programme (refuse pour la conversion, confirme a l'ouverture).</summary>
    public bool IsExecutable { get; init; }
    public long Size { get; init; }

    public string? FormatId => Format?.Id;
}

/// <summary>
/// Identification du type reel d'un fichier : signature binaire, analyse du conteneur
/// (ZIP Office/ODF/EPUB/3MF, ISO BMFF, Matroska, RIFF, OLE2...), puis extension.
///
/// Le detecteur ne lit que l'en-tete (64 Kio) et, pour les ZIP, l'annuaire central :
/// un fichier de plusieurs Gio n'est jamais charge en memoire. Il n'interprete ni
/// n'execute aucun contenu.
/// </summary>
public static class FormatDetector
{
    private const int HeadSize = 64 * 1024;

    // Familles dont les membres partagent une signature : l'extension departage sans alerte.
    private static readonly string[][] Families =
    [
        ["mp4", "m4v", "m4a", "mov", "3gp", "heic", "avif"],
        ["mkv", "webm"],
        ["tiff", "raw", "dng"],
        ["ogg", "opus"],
        ["wmv", "wma"],
        ["mpeg", "ts"],
        ["txt", "csv", "tsv", "md", "yaml", "json", "xml", "html", "svg", "ics", "vcf", "eml", "mbox", "gltf", "obj", "stl", "ply", "dxf", "step", "iges", "rtf", "script"],
        ["mobi", "azw3"],
        ["ttf", "otf"],
        ["gz", "tgz"],
        ["bz2"],
        ["xz"],
        ["zip", "docx", "xlsx", "pptx", "odt", "ods", "odp", "epub", "3mf"],
        ["doc", "xls", "ppt"],
        ["aac", "mp3"],
    ];

    public static DetectionResult Detect(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("Fichier introuvable.", path);
        var ext = FormatCatalog.FromFileName(path);
        byte[] head;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            int len = (int)Math.Min(HeadSize, fs.Length);
            head = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = fs.Read(head, read, len - read);
                if (n == 0) break;
                read += n;
            }
            if (read < len) Array.Resize(ref head, read);
        }
        catch (IOException ex)
        {
            return new DetectionResult { ExtensionFormat = ext, Format = ext, Method = "extension", Size = fi.Length, Warning = $"Fichier illisible ou verrouillé : {ex.Message}" };
        }

        if (head.Length == 0)
            return new DetectionResult { ExtensionFormat = ext, Format = null, Method = "vide", Size = 0, Warning = "Le fichier est vide.", SuspectedCorrupt = true };

        var (sig, corrupt, note) = Sniff(path, head, fi.Length, ext);
        return Combine(sig, ext, fi.Length, corrupt, note);
    }

    /// <summary>Detection sur un tampon (utilisee par les tests et la validation des sorties).</summary>
    public static DetectionResult DetectBytes(byte[] head, string fileNameHint, long size = -1)
    {
        var ext = FormatCatalog.FromFileName(fileNameHint);
        var (sig, corrupt, note) = Sniff(null, head, size < 0 ? head.Length : size, ext);
        return Combine(sig, ext, size < 0 ? head.Length : size, corrupt, note);
    }

    private static DetectionResult Combine(FormatInfo? sig, FormatInfo? ext, long size, bool corrupt, string? note)
    {
        if (sig?.Category == FormatCategory.Executable && sig.Id == "exe")
        {
            return new DetectionResult
            {
                Format = sig, ExtensionFormat = ext, Method = "signature", Size = size, IsExecutable = true,
                Mismatch = ext is not null && ext.Category != FormatCategory.Executable,
                Warning = ext is not null && ext.Category != FormatCategory.Executable
                    ? $"Attention : ce fichier porte l'extension {ext.Label} mais contient un programme Windows. Il ne sera ni converti ni ouvert sans confirmation."
                    : null,
            };
        }

        if (sig is null)
        {
            return new DetectionResult
            {
                Format = ext, ExtensionFormat = ext, Method = ext is null ? "inconnu" : "extension", Size = size,
                SuspectedCorrupt = corrupt, IsExecutable = ext?.Category == FormatCategory.Executable,
                Warning = note ?? (ext is null ? "Format non reconnu." : null),
            };
        }

        if (ext is null)
            return new DetectionResult { Format = sig, ExtensionFormat = null, Method = "signature", Size = size, SuspectedCorrupt = corrupt, Warning = note ?? (sig is not null ? $"Fichier sans extension reconnue : identifié comme {sig.Label}." : null) };

        if (string.Equals(sig.Id, ext.Id, StringComparison.OrdinalIgnoreCase))
            return new DetectionResult { Format = sig, ExtensionFormat = ext, Method = "signature", Size = size, SuspectedCorrupt = corrupt, Warning = note };

        if (SameFamily(sig.Id, ext.Id))
        {
            // Le contenu et l'extension sont compatibles : on garde la precision de l'extension
            // sauf si l'analyse du conteneur est plus precise (ex. .zip qui est un DOCX).
            var chosen = IsMoreSpecific(sig.Id, ext.Id) ? sig : ext;
            return new DetectionResult { Format = chosen, ExtensionFormat = ext, Method = chosen == sig ? "contenu" : "extension+signature", Size = size, SuspectedCorrupt = corrupt, Warning = note };
        }

        return new DetectionResult
        {
            Format = sig, ExtensionFormat = ext, Method = "signature", Size = size, Mismatch = true, SuspectedCorrupt = corrupt,
            Warning = $"L'extension indique {ext.Label} mais le contenu est {sig.Label}. Le contenu réel est utilisé.",
        };
    }

    private static bool SameFamily(string a, string b)
    {
        foreach (var fam in Families)
            if (fam.Contains(a) && fam.Contains(b)) return true;
        return false;
    }

    private static bool IsMoreSpecific(string sigId, string extId) =>
        (extId == "zip" && sigId != "zip") ||
        (extId == "gz" && sigId == "tgz") ||
        (extId == "txt" && sigId is not "txt") ||
        (extId is "doc" or "xls" or "ppt" && sigId is "doc" or "xls" or "ppt");

    // ------------------------------------------------------------------ signatures

    private static (FormatInfo? fmt, bool corrupt, string? note) Sniff(string? path, byte[] h, long size, FormatInfo? ext)
    {
        FormatInfo F(string id) => FormatCatalog.Require(id);
        bool At(int off, params byte[] sig)
        {
            if (h.Length < off + sig.Length) return false;
            for (int i = 0; i < sig.Length; i++) if (h[off + i] != sig[i]) return false;
            return true;
        }
        bool Ascii(int off, string s) => At(off, Encoding.ASCII.GetBytes(s));

        if (At(0, 0x4D, 0x5A) && LooksLikePe(h)) return (F("exe"), false, null);
        if (At(0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return (F("png"), false, null);
        if (At(0, 0xFF, 0xD8, 0xFF)) return (F("jpg"), false, null);
        if (Ascii(0, "GIF87a") || Ascii(0, "GIF89a")) return (F("gif"), false, null);
        if (Ascii(0, "BM") && h.Length > 14 && BitConverter.ToUInt32(h, 2) is var bmSize && bmSize > 14) return (F("bmp"), bmSize > size + 2, null);
        if (Ascii(0, "%PDF-")) return (F("pdf"), !HasPdfEof(path, h, size), HasPdfEof(path, h, size) ? null : "Le PDF semble tronqué (marqueur de fin absent).");
        if (Ascii(0, "8BPS")) return (F("psd"), false, null);
        if (At(0, 0x76, 0x2F, 0x31, 0x01)) return (F("exr"), false, null);
        if (Ascii(0, "#?RADIANCE") || Ascii(0, "#?RGBE")) return (F("hdr"), false, null);
        if (Ascii(0, "DDS ")) return (F("dds"), false, null);
        if (At(0, 0x00, 0x00, 0x01, 0x00) && h.Length > 6 && h[4] > 0 && ext?.Id == "ico") return (F("ico"), false, null);
        if (At(0, 0x00, 0x00, 0x01, 0x00) && h.Length > 22 && h[4] is > 0 and < 64 && h[5] == 0) return (F("ico"), false, null);

        // TIFF et RAW bases sur TIFF
        if (At(0, 0x49, 0x49, 0x2A, 0x00) || At(0, 0x4D, 0x4D, 0x00, 0x2A))
        {
            if (Ascii(8, "CR")) return (F("raw"), false, null); // Canon CR2
            if (ext?.Id == "raw") return (F("raw"), false, null);
            return (F("tiff"), false, null);
        }
        if (At(0, 0x49, 0x49, 0x52, 0x4F) || At(0, 0x49, 0x49, 0x55, 0x00)) return (F("raw"), false, null); // ORF, RW2
        if (Ascii(0, "FUJIFILMCCD-RAW")) return (F("raw"), false, null);

        // RIFF
        if (Ascii(0, "RIFF") && h.Length >= 12)
        {
            if (Ascii(8, "WEBP")) return (F("webp"), false, null);
            if (Ascii(8, "WAVE")) return (F("wav"), false, null);
            if (Ascii(8, "AVI ")) return (F("avi"), false, null);
        }
        if (Ascii(0, "FORM") && (Ascii(8, "AIFF") || Ascii(8, "AIFC"))) return (F("aiff"), false, null);

        // ISO Base Media (MP4, MOV, M4A, HEIC, AVIF, 3GP)
        if (h.Length >= 12 && Ascii(4, "ftyp"))
        {
            var brand = Encoding.ASCII.GetString(h, 8, 4);
            var compat = h.Length >= 32 ? Encoding.ASCII.GetString(h, 8, Math.Min(32, h.Length - 8)) : brand;
            if (brand is "avif" or "avis") return (F("avif"), false, null);
            if (brand is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1") return (F(compat.Contains("avif") ? "avif" : "heic"), false, null);
            if (brand.StartsWith("M4A") || brand.StartsWith("M4B")) return (F("m4a"), false, null);
            if (brand.StartsWith("M4V")) return (F("m4v"), false, null);
            if (brand == "qt  ") return (F("mov"), false, null);
            if (brand.StartsWith("3g")) return (F("3gp"), false, null);
            if (brand == "crx ") return (F("raw"), false, null); // Canon CR3
            return (F(ext?.Id is "m4a" or "m4v" or "mov" ? ext.Id : "mp4"), false, null);
        }
        if (h.Length >= 8 && (Ascii(4, "moov") || Ascii(4, "mdat") || Ascii(4, "wide"))) return (F(ext?.Id == "mov" ? "mov" : "mp4"), false, null);

        // Matroska / WebM
        if (At(0, 0x1A, 0x45, 0xDF, 0xA3))
        {
            var s = Encoding.ASCII.GetString(h, 0, Math.Min(h.Length, 64));
            return (F(s.Contains("webm") ? "webm" : "mkv"), false, null);
        }
        if (Ascii(0, "OggS"))
        {
            var s = Encoding.ASCII.GetString(h, 0, Math.Min(h.Length, 256));
            return (F(s.Contains("OpusHead") ? "opus" : "ogg"), false, null);
        }
        if (Ascii(0, "fLaC")) return (F("flac"), false, null);
        if (Ascii(0, "ID3")) return (F(ext?.Id == "aac" ? "aac" : "mp3"), false, null);
        if (Ascii(0, "#!AMR")) return (F("amr"), false, null);
        if (At(0, 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11)) return (F(ext?.Id == "wma" ? "wma" : "wmv"), false, null);
        if (Ascii(0, "FLV")) return (F("flv"), false, null);
        if (At(0, 0x00, 0x00, 0x01, 0xBA) || At(0, 0x00, 0x00, 0x01, 0xB3)) return (F("mpeg"), false, null);
        if (h.Length >= 188 * 3 && h[0] == 0x47 && h[188] == 0x47 && h[376] == 0x47) return (F("ts"), false, null);
        if (h.Length >= 192 * 3 && h[4] == 0x47 && h[196] == 0x47 && h[388] == 0x47) return (F("ts"), false, null); // M2TS
        if (h.Length > 2 && h[0] == 0xFF && (h[1] & 0xF6) == 0xF0) return (F("aac"), false, null);   // ADTS
        if (h.Length > 2 && h[0] == 0xFF && (h[1] & 0xE0) == 0xE0 && (h[1] & 0x06) != 0) return (F("mp3"), false, null); // trame MPEG audio

        // Archives
        if (At(0, 0x50, 0x4B, 0x03, 0x04) || At(0, 0x50, 0x4B, 0x05, 0x06)) return SniffZip(path, ext);
        if (At(0, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)) return (F("7z"), false, null);
        if (Ascii(0, "Rar!")) return (F("rar"), false, null);
        if (At(0, 0x1F, 0x8B)) return (F(ext?.Id == "tgz" ? "tgz" : "gz"), false, null);
        if (Ascii(0, "BZh")) return (F("bz2"), false, null);
        if (At(0, 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00)) return (F("xz"), false, null);
        if (h.Length > 262 && Ascii(257, "ustar")) return (F("tar"), false, null);

        // OLE2 (DOC/XLS/PPT)
        if (At(0, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1))
        {
            var kind = SniffCfb(path, h);
            if (kind is not null) return (F(kind), false, null);
            return (ext?.Category is FormatCategory.Document or FormatCategory.Spreadsheet or FormatCategory.Presentation ? ext : null, false,
                "Document OLE2 non identifié (peut-être un fichier Outlook, un ancien format ou un fichier protégé).");
        }

        // E-books Mobipocket : "BOOKMOBI" a l'offset 60
        if (h.Length > 68 && Ascii(60, "BOOKMOBI")) return (F(ext?.Id == "azw3" ? "azw3" : "mobi"), false, null);

        // Polices
        if (Ascii(0, "wOFF")) return (F("woff"), false, null);
        if (Ascii(0, "wOF2")) return (F("woff2"), false, null);
        if (Ascii(0, "OTTO")) return (F("otf"), false, null);
        if (At(0, 0x00, 0x01, 0x00, 0x00) && h.Length > 12 && h[4] == 0 && h[5] is > 0 and < 64) return (F("ttf"), false, null);
        if (Ascii(0, "true")) return (F("ttf"), false, null);

        // 3D binaires
        if (Ascii(0, "glTF")) return (F("glb"), false, null);
        if (Ascii(0, "Kaydara FBX Binary")) return (F("fbx"), false, null);
        if (h.Length >= 84 && ext?.Id == "stl" && !LooksLikeText(h))
        {
            uint tri = BitConverter.ToUInt32(h, 80);
            long expected = 84 + 50L * tri;
            return (F("stl"), expected != size, expected != size ? "STL binaire : taille incohérente (fichier tronqué ?)." : null);
        }

        // Formats texte
        if (LooksLikeText(h)) return SniffText(h, ext);

        return (null, false, null);
    }

    private static bool LooksLikePe(byte[] h)
    {
        if (h.Length < 0x40) return true;
        int peOff = BitConverter.ToInt32(h, 0x3C);
        if (peOff > 0 && peOff + 4 <= h.Length)
            return h[peOff] == 'P' && h[peOff + 1] == 'E' && h[peOff + 2] == 0 && h[peOff + 3] == 0;
        return true;
    }

    private static bool HasPdfEof(string? path, byte[] head, long size)
    {
        try
        {
            byte[] tail;
            if (path is null) tail = head.Length > 2048 ? head[^2048..] : head;
            else
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                int n = (int)Math.Min(2048, fs.Length);
                fs.Seek(-n, SeekOrigin.End);
                tail = new byte[n];
                fs.ReadExactly(tail);
            }
            return Encoding.ASCII.GetString(tail).Contains("%%EOF");
        }
        catch { return true; }
    }

    private static (FormatInfo?, bool, string?) SniffZip(string? path, FormatInfo? ext)
    {
        FormatInfo F(string id) => FormatCatalog.Require(id);
        if (path is null) return (ext?.Id is "docx" or "xlsx" or "pptx" or "odt" or "ods" or "odp" or "epub" or "3mf" ? ext : F("zip"), false, null);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in zip.Entries.Take(5000)) names.Add(e.FullName);
            var mimetype = zip.GetEntry("mimetype");
            if (mimetype is not null && mimetype.Length < 200)
            {
                using var r = new StreamReader(mimetype.Open());
                var mt = r.ReadToEnd().Trim();
                if (mt == "application/epub+zip") return (F("epub"), false, null);
                if (mt == "application/vnd.oasis.opendocument.text") return (F("odt"), false, null);
                if (mt == "application/vnd.oasis.opendocument.spreadsheet") return (F("ods"), false, null);
                if (mt == "application/vnd.oasis.opendocument.presentation") return (F("odp"), false, null);
            }
            if (names.Contains("[Content_Types].xml"))
            {
                if (names.Any(n => n.StartsWith("word/"))) return (F("docx"), false, null);
                if (names.Any(n => n.StartsWith("xl/"))) return (F("xlsx"), false, null);
                if (names.Any(n => n.StartsWith("ppt/"))) return (F("pptx"), false, null);
                if (names.Any(n => n.StartsWith("3D/", StringComparison.OrdinalIgnoreCase))) return (F("3mf"), false, null);
            }
            return (F("zip"), false, null);
        }
        catch (InvalidDataException)
        {
            return (F("zip"), true, "Archive ZIP endommagée ou incomplète (annuaire central illisible).");
        }
        catch (IOException ex)
        {
            return (F("zip"), false, "Archive illisible : " + ex.Message);
        }
    }

    /// <summary>Lit les noms du premier secteur d'annuaire d'un fichier OLE2 (Compound File).</summary>
    private static string? SniffCfb(string? path, byte[] h)
    {
        try
        {
            if (h.Length < 512) return null;
            int sectorShift = BitConverter.ToUInt16(h, 30);
            int sectorSize = 1 << sectorShift;
            int dirStart = BitConverter.ToInt32(h, 48);
            long offset = (long)(dirStart + 1) * sectorSize;
            byte[] dir;
            if (offset + sectorSize <= h.Length) dir = h.AsSpan((int)offset, sectorSize).ToArray();
            else if (path is not null)
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (offset + sectorSize > fs.Length) return null;
                fs.Seek(offset, SeekOrigin.Begin);
                dir = new byte[sectorSize];
                fs.ReadExactly(dir);
            }
            else return null;

            for (int i = 0; i + 128 <= dir.Length; i += 128)
            {
                int nameLen = BitConverter.ToUInt16(dir, i + 64);
                if (nameLen < 2 || nameLen > 64) continue;
                var name = Encoding.Unicode.GetString(dir, i, nameLen - 2);
                if (name == "WordDocument") return "doc";
                if (name is "Workbook" or "Book") return "xls";
                if (name is "PowerPoint Document") return "ppt";
            }
        }
        catch { }
        return null;
    }

    public static bool LooksLikeText(byte[] h)
    {
        int n = Math.Min(h.Length, 8192);
        if (n == 0) return false;
        if (n >= 2 && ((h[0] == 0xFF && h[1] == 0xFE) || (h[0] == 0xFE && h[1] == 0xFF))) return true; // UTF-16 avec BOM
        int control = 0;
        for (int i = 0; i < n; i++)
        {
            byte b = h[i];
            if (b == 0) return false;
            if (b < 0x09 || (b > 0x0D && b < 0x20 && b != 0x1B)) control++;
        }
        if (control > n / 50) return false;
        // UTF-8 valide (on tolere une sequence coupee en fin de tampon)
        var dec = new UTF8Encoding(false, true);
        for (int cut = 0; cut < 4; cut++)
        {
            try { dec.GetString(h, 0, n - cut); return true; }
            catch (DecoderFallbackException) { }
        }
        // Texte 8 bits (Windows-1252) : accepte si peu de caracteres de controle
        return control == 0;
    }

    private static (FormatInfo?, bool, string?) SniffText(byte[] h, FormatInfo? ext)
    {
        FormatInfo F(string id) => FormatCatalog.Require(id);
        string s;
        if (h.Length >= 2 && h[0] == 0xFF && h[1] == 0xFE) s = Encoding.Unicode.GetString(h, 2, Math.Min(h.Length - 2, 8000) & ~1);
        else if (h.Length >= 2 && h[0] == 0xFE && h[1] == 0xFF) s = Encoding.BigEndianUnicode.GetString(h, 2, Math.Min(h.Length - 2, 8000) & ~1);
        else s = Encoding.UTF8.GetString(h, 0, Math.Min(h.Length, 8000));
        var t = s.TrimStart('﻿', ' ', '\t', '\r', '\n');

        if (t.StartsWith(@"{\rtf")) return (F("rtf"), false, null);
        if (t.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase)) return (F("vcf"), false, null);
        if (t.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)) return (F("ics"), false, null);
        if (t.StartsWith("ISO-10303-21")) return (F("step"), false, null);
        if (t.StartsWith("ply\n") || t.StartsWith("ply\r\n")) return (F("ply"), false, null);
        if (t.StartsWith("solid") && s.Contains("facet")) return (F("stl"), false, null);
        if (t.StartsWith("<svg") || (t.StartsWith("<?xml") && s.Contains("<svg"))) return (F("svg"), false, null);
        if (t.StartsWith("From ") && ext?.Id is "mbox" or null) return (F("mbox"), false, null);

        // Les formats texte « libres » sont departages par l'extension si elle est textuelle.
        if (ext is not null && ext.Category is FormatCategory.Text or FormatCategory.Data or FormatCategory.Model3D or FormatCategory.Cad
                or FormatCategory.Email or FormatCategory.Contact or FormatCategory.Calendar or FormatCategory.Executable)
            return (ext, false, null);
        if (ext?.Id == "svg") return (ext, false, null);

        if (t.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || t.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return (F("html"), false, null);
        if (t.StartsWith("<?xml") || (t.StartsWith('<') && t.Contains("</"))) return (F("xml"), false, null);
        if ((t.StartsWith('{') || t.StartsWith('[')) && LooksLikeJson(t)) return (F("json"), false, null);
        if (t.StartsWith("0\n") && s.Contains("SECTION")) return (F("dxf"), false, null);
        if (t.StartsWith("Received:") || t.StartsWith("Return-Path:") || (t.StartsWith("From:") && s.Contains("\nSubject:"))) return (F("eml"), false, null);
        return (F("txt"), false, null);
    }

    private static bool LooksLikeJson(string t)
    {
        try
        {
            var reader = new System.Text.Json.Utf8JsonReader(Encoding.UTF8.GetBytes(t), new System.Text.Json.JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
            int tokens = 0;
            while (tokens < 50 && reader.Read()) tokens++;
            return tokens > 0;
        }
        catch (System.Text.Json.JsonException)
        {
            // Tampon tronque : on accepte si au moins le debut est du JSON
            return t.Length >= 8000;
        }
    }
}
