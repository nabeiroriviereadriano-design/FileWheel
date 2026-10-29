namespace FileWheel.Core.Formats;

public enum FormatCategory
{
    Unknown, Document, Spreadsheet, Presentation, Pdf, Image, Video, Audio, Archive, Ebook, Data, Text,
    Model3D, Cad, Font, Email, Contact, Calendar, Executable,
}

/// <summary>Description d'un format connu.</summary>
public sealed record FormatInfo(
    string Id,
    string Label,
    FormatCategory Category,
    string[] Extensions,
    string Mime = "application/octet-stream")
{
    /// <summary>Le format peut contenir de la transparence (canal alpha).</summary>
    public bool Alpha { get; init; }
    /// <summary>Le format peut contenir une animation (plusieurs images).</summary>
    public bool Animation { get; init; }
    /// <summary>Le format compresse avec perte par nature.</summary>
    public bool Lossy { get; init; }
    /// <summary>Le format est sans perte (audio, image).</summary>
    public bool Lossless { get; init; }
    /// <summary>Peut contenir plusieurs pages (PDF, TIFF).</summary>
    public bool MultiPage { get; init; }

    public string PrimaryExtension => Extensions[0];
    public override string ToString() => Label;
}

/// <summary>Catalogue central des formats. Les identifiants sont stables (minuscules).</summary>
public static class FormatCatalog
{
    private static readonly Dictionary<string, FormatInfo> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, FormatInfo> ByExt = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<FormatInfo> All => ById.Values;

    static FormatCatalog()
    {
        // Documents
        Add(new("doc", "Word 97-2003 (DOC)", FormatCategory.Document, ["doc"], "application/msword"));
        Add(new("docx", "Word (DOCX)", FormatCategory.Document, ["docx", "docm"], "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
        Add(new("odt", "OpenDocument Texte (ODT)", FormatCategory.Document, ["odt"], "application/vnd.oasis.opendocument.text"));
        Add(new("rtf", "Texte enrichi (RTF)", FormatCategory.Document, ["rtf"], "application/rtf"));
        Add(new("pdf", "PDF", FormatCategory.Pdf, ["pdf"], "application/pdf") { MultiPage = true });
        Add(new("ppt", "PowerPoint 97-2003 (PPT)", FormatCategory.Presentation, ["ppt"], "application/vnd.ms-powerpoint"));
        Add(new("pptx", "PowerPoint (PPTX)", FormatCategory.Presentation, ["pptx", "pptm"], "application/vnd.openxmlformats-officedocument.presentationml.presentation"));
        Add(new("odp", "OpenDocument Présentation (ODP)", FormatCategory.Presentation, ["odp"], "application/vnd.oasis.opendocument.presentation"));
        Add(new("xls", "Excel 97-2003 (XLS)", FormatCategory.Spreadsheet, ["xls"], "application/vnd.ms-excel"));
        Add(new("xlsx", "Excel (XLSX)", FormatCategory.Spreadsheet, ["xlsx", "xlsm"], "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        Add(new("ods", "OpenDocument Classeur (ODS)", FormatCategory.Spreadsheet, ["ods"], "application/vnd.oasis.opendocument.spreadsheet"));

        // Texte et donnees
        Add(new("txt", "Texte brut (TXT)", FormatCategory.Text, ["txt", "text", "log"], "text/plain"));
        Add(new("md", "Markdown", FormatCategory.Text, ["md", "markdown"], "text/markdown"));
        Add(new("html", "HTML", FormatCategory.Text, ["html", "htm", "xhtml"], "text/html"));
        Add(new("csv", "CSV", FormatCategory.Data, ["csv"], "text/csv"));
        Add(new("tsv", "TSV", FormatCategory.Data, ["tsv", "tab"], "text/tab-separated-values"));
        Add(new("json", "JSON", FormatCategory.Data, ["json"], "application/json"));
        Add(new("xml", "XML", FormatCategory.Data, ["xml"], "application/xml"));
        Add(new("yaml", "YAML", FormatCategory.Data, ["yaml", "yml"], "application/yaml"));

        // Images
        Add(new("png", "PNG", FormatCategory.Image, ["png"], "image/png") { Alpha = true, Lossless = true });
        Add(new("jpg", "JPEG", FormatCategory.Image, ["jpg", "jpeg", "jpe", "jfif"], "image/jpeg") { Lossy = true });
        Add(new("webp", "WebP", FormatCategory.Image, ["webp"], "image/webp") { Alpha = true, Animation = true });
        Add(new("bmp", "BMP", FormatCategory.Image, ["bmp", "dib"], "image/bmp") { Lossless = true });
        Add(new("tiff", "TIFF", FormatCategory.Image, ["tif", "tiff"], "image/tiff") { Alpha = true, Lossless = true, MultiPage = true });
        Add(new("gif", "GIF", FormatCategory.Image, ["gif"], "image/gif") { Alpha = true, Animation = true, Lossless = true });
        Add(new("avif", "AVIF", FormatCategory.Image, ["avif"], "image/avif") { Alpha = true, Lossy = true });
        Add(new("heic", "HEIC/HEIF", FormatCategory.Image, ["heic", "heif", "hif"], "image/heic") { Alpha = true, Lossy = true });
        Add(new("ico", "Icône Windows (ICO)", FormatCategory.Image, ["ico"], "image/x-icon") { Alpha = true, Lossless = true });
        Add(new("svg", "SVG (vectoriel)", FormatCategory.Image, ["svg", "svgz"], "image/svg+xml") { Alpha = true });
        Add(new("tga", "TGA", FormatCategory.Image, ["tga", "icb", "vda", "vst"], "image/x-tga") { Alpha = true, Lossless = true });
        Add(new("dds", "DirectDraw Surface (DDS)", FormatCategory.Image, ["dds"], "image/vnd-ms.dds") { Alpha = true });
        Add(new("exr", "OpenEXR", FormatCategory.Image, ["exr"], "image/x-exr") { Alpha = true });
        Add(new("hdr", "Radiance HDR", FormatCategory.Image, ["hdr"], "image/vnd.radiance"));
        Add(new("psd", "Photoshop (PSD)", FormatCategory.Image, ["psd", "psb"], "image/vnd.adobe.photoshop") { Alpha = true });
        Add(new("raw", "Photo RAW", FormatCategory.Image, ["cr2", "cr3", "nef", "nrw", "arw", "srf", "sr2", "dng", "orf", "rw2", "raf", "pef", "srw", "3fr", "erf", "kdc", "mrw", "x3f", "iiq", "rwl"], "image/x-raw"));

        // Video
        Add(new("mp4", "MP4", FormatCategory.Video, ["mp4"], "video/mp4"));
        Add(new("m4v", "M4V", FormatCategory.Video, ["m4v"], "video/x-m4v"));
        Add(new("mkv", "Matroska (MKV)", FormatCategory.Video, ["mkv"], "video/x-matroska"));
        Add(new("webm", "WebM", FormatCategory.Video, ["webm"], "video/webm"));
        Add(new("mov", "QuickTime (MOV)", FormatCategory.Video, ["mov", "qt"], "video/quicktime"));
        Add(new("avi", "AVI", FormatCategory.Video, ["avi"], "video/x-msvideo"));
        Add(new("wmv", "Windows Media Video (WMV)", FormatCategory.Video, ["wmv", "asf"], "video/x-ms-wmv"));
        Add(new("mpeg", "MPEG-1/2 (MPG)", FormatCategory.Video, ["mpg", "mpeg", "vob", "m2v"], "video/mpeg"));
        Add(new("ts", "MPEG-TS", FormatCategory.Video, ["ts", "mts", "m2ts"], "video/mp2t"));
        Add(new("flv", "Flash Video (FLV)", FormatCategory.Video, ["flv"], "video/x-flv"));
        Add(new("3gp", "3GP", FormatCategory.Video, ["3gp", "3g2"], "video/3gpp"));

        // Audio
        Add(new("mp3", "MP3", FormatCategory.Audio, ["mp3"], "audio/mpeg") { Lossy = true });
        Add(new("wav", "WAV", FormatCategory.Audio, ["wav", "wave"], "audio/wav") { Lossless = true });
        Add(new("flac", "FLAC", FormatCategory.Audio, ["flac"], "audio/flac") { Lossless = true });
        Add(new("aac", "AAC (ADTS)", FormatCategory.Audio, ["aac"], "audio/aac") { Lossy = true });
        Add(new("m4a", "M4A (AAC/ALAC)", FormatCategory.Audio, ["m4a", "m4b"], "audio/mp4"));
        Add(new("ogg", "Ogg Vorbis", FormatCategory.Audio, ["ogg", "oga"], "audio/ogg") { Lossy = true });
        Add(new("opus", "Opus", FormatCategory.Audio, ["opus"], "audio/opus") { Lossy = true });
        Add(new("wma", "Windows Media Audio (WMA)", FormatCategory.Audio, ["wma"], "audio/x-ms-wma") { Lossy = true });
        Add(new("aiff", "AIFF", FormatCategory.Audio, ["aif", "aiff", "aifc"], "audio/aiff") { Lossless = true });
        Add(new("amr", "AMR", FormatCategory.Audio, ["amr"], "audio/amr") { Lossy = true });

        // Archives
        Add(new("zip", "ZIP", FormatCategory.Archive, ["zip"], "application/zip"));
        Add(new("7z", "7-Zip (7Z)", FormatCategory.Archive, ["7z"], "application/x-7z-compressed"));
        Add(new("tar", "TAR", FormatCategory.Archive, ["tar"], "application/x-tar"));
        Add(new("tgz", "TAR + GZip (TGZ)", FormatCategory.Archive, ["tgz", "tar.gz"], "application/gzip"));
        Add(new("gz", "GZip (GZ)", FormatCategory.Archive, ["gz"], "application/gzip"));
        Add(new("bz2", "BZip2 (BZ2)", FormatCategory.Archive, ["bz2", "tbz2", "tar.bz2"], "application/x-bzip2"));
        Add(new("xz", "XZ", FormatCategory.Archive, ["xz", "txz", "tar.xz"], "application/x-xz"));
        Add(new("rar", "RAR", FormatCategory.Archive, ["rar"], "application/vnd.rar"));

        // Livres numeriques
        Add(new("epub", "EPUB", FormatCategory.Ebook, ["epub"], "application/epub+zip"));
        Add(new("mobi", "Mobipocket (MOBI)", FormatCategory.Ebook, ["mobi", "prc"], "application/x-mobipocket-ebook"));
        Add(new("azw3", "Kindle (AZW3)", FormatCategory.Ebook, ["azw3", "azw", "kf8"], "application/vnd.amazon.ebook"));
        Add(new("fb2", "FictionBook (FB2)", FormatCategory.Ebook, ["fb2"], "application/x-fictionbook+xml"));

        // 3D et CAO
        Add(new("stl", "STL", FormatCategory.Model3D, ["stl"], "model/stl"));
        Add(new("obj", "Wavefront OBJ", FormatCategory.Model3D, ["obj"], "model/obj"));
        Add(new("ply", "PLY", FormatCategory.Model3D, ["ply"], "model/ply"));
        Add(new("gltf", "glTF (JSON)", FormatCategory.Model3D, ["gltf"], "model/gltf+json"));
        Add(new("glb", "glTF binaire (GLB)", FormatCategory.Model3D, ["glb"], "model/gltf-binary"));
        Add(new("3mf", "3MF", FormatCategory.Model3D, ["3mf"], "model/3mf"));
        Add(new("fbx", "FBX", FormatCategory.Model3D, ["fbx"]));
        Add(new("step", "STEP", FormatCategory.Cad, ["step", "stp"], "model/step"));
        Add(new("iges", "IGES", FormatCategory.Cad, ["iges", "igs"], "model/iges"));
        Add(new("dxf", "DXF", FormatCategory.Cad, ["dxf"], "image/vnd.dxf"));

        // Polices, e-mails, contacts, agendas
        Add(new("ttf", "Police TrueType (TTF)", FormatCategory.Font, ["ttf"], "font/ttf"));
        Add(new("otf", "Police OpenType (OTF)", FormatCategory.Font, ["otf"], "font/otf"));
        Add(new("woff", "WOFF", FormatCategory.Font, ["woff"], "font/woff"));
        Add(new("woff2", "WOFF2", FormatCategory.Font, ["woff2"], "font/woff2"));
        Add(new("eml", "E-mail (EML)", FormatCategory.Email, ["eml"], "message/rfc822"));
        Add(new("mbox", "Boîte mail (MBOX)", FormatCategory.Email, ["mbox", "mbx"], "application/mbox"));
        Add(new("vcf", "Contacts (VCF)", FormatCategory.Contact, ["vcf", "vcard"], "text/vcard"));
        Add(new("ics", "Agenda (ICS)", FormatCategory.Calendar, ["ics", "ical"], "text/calendar"));

        // Executables : reconnus pour etre EXCLUS des conversions et signales a l'ouverture.
        Add(new("exe", "Programme Windows", FormatCategory.Executable, ["exe", "dll", "scr", "com", "sys", "msi", "cpl"]));
        Add(new("script", "Script", FormatCategory.Executable, ["bat", "cmd", "ps1", "vbs", "vbe", "js", "jse", "wsf", "wsh", "hta", "lnk", "reg", "psm1"]));
    }

    private static void Add(FormatInfo f)
    {
        ById[f.Id] = f;
        foreach (var e in f.Extensions)
            ByExt.TryAdd(e, f);
    }

    public static FormatInfo? Get(string? id) => id is not null && ById.TryGetValue(id, out var f) ? f : null;

    public static FormatInfo Require(string id) =>
        Get(id) ?? throw new KeyNotFoundException($"Format inconnu : {id}");

    /// <summary>Format suggere par l'extension (gere les doubles extensions .tar.gz).</summary>
    public static FormatInfo? FromFileName(string path)
    {
        var name = Path.GetFileName(path);
        var lower = name.ToLowerInvariant();
        foreach (var dbl in new[] { "tar.gz", "tar.bz2", "tar.xz" })
            if (lower.EndsWith("." + dbl) && ByExt.TryGetValue(dbl, out var d)) return d;
        var ext = Path.GetExtension(name).TrimStart('.');
        return ext.Length > 0 && ByExt.TryGetValue(ext, out var f) ? f : null;
    }

    /// <summary>Extension utilisee pour un fichier produit dans ce format.</summary>
    public static string OutputExtension(string formatId) => formatId switch
    {
        "tgz" => "tar.gz",
        _ => Require(formatId).PrimaryExtension,
    };

    public static bool IsExecutable(string path)
    {
        var f = FromFileName(path);
        return f?.Category == FormatCategory.Executable;
    }

    public static string CategoryLabel(FormatCategory c) => c switch
    {
        FormatCategory.Document => "Documents",
        FormatCategory.Spreadsheet => "Classeurs",
        FormatCategory.Presentation => "Présentations",
        FormatCategory.Pdf => "PDF",
        FormatCategory.Image => "Images",
        FormatCategory.Video => "Vidéos",
        FormatCategory.Audio => "Audio",
        FormatCategory.Archive => "Archives",
        FormatCategory.Ebook => "Livres numériques",
        FormatCategory.Data => "Données",
        FormatCategory.Text => "Texte",
        FormatCategory.Model3D => "3D",
        FormatCategory.Cad => "CAO",
        FormatCategory.Font => "Polices",
        FormatCategory.Email => "E-mails",
        FormatCategory.Contact => "Contacts",
        FormatCategory.Calendar => "Agendas",
        FormatCategory.Executable => "Programmes",
        _ => "Autres",
    };
}
