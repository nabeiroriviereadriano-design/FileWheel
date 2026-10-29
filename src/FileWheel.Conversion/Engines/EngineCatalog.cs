namespace FileWheel.Conversion.Engines;

public enum InstallMethod
{
    /// <summary>Archive portable publiee sur GitHub, verifiee par son empreinte SHA-256 publiee par GitHub.</summary>
    GitHubRelease,
    /// <summary>Installation par winget (gestionnaire de paquets de Windows, empreintes verifiees par winget).</summary>
    Winget,
    /// <summary>Installation manuelle par l'utilisateur ; FileWheel detecte l'executable.</summary>
    Manual,
}

/// <summary>Description d'un moteur externe et de la maniere legale de l'obtenir.</summary>
public sealed record EngineDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Purpose { get; init; }
    public required string License { get; init; }
    public required string Homepage { get; init; }
    /// <summary>Noms de l'executable (Windows d'abord). Le premier trouve gagne.</summary>
    public required string[] ExeNames { get; init; }
    /// <summary>Emplacements d'installation habituels sous Windows (variables d'environnement autorisees).</summary>
    public string[] KnownPaths { get; init; } = [];
    public InstallMethod Method { get; init; } = InstallMethod.Manual;
    public string? GitHubRepo { get; init; }
    /// <summary>Expression reguliere selectionnant l'asset de la release.</summary>
    public string? AssetPattern { get; init; }
    /// <summary>Asset supplementaire necessaire a l'extraction (ex. 7zr.exe pour les .7z).</summary>
    public string? HelperAssetPattern { get; init; }
    public string? WingetId { get; init; }
    public int ApproxMiB { get; init; }
    /// <summary>Bibliotheque chargee dans le processus (pdfium) plutot qu'un executable.</summary>
    public bool IsLibrary { get; init; }
    /// <summary>Ne jamais rechercher dans le PATH sous Windows (ex. « convert.exe » est un outil systeme).</summary>
    public bool NoPathLookupOnWindows { get; init; }
}

public static class EngineIds
{
    public const string FFmpeg = "ffmpeg";
    public const string FFprobe = "ffprobe";
    public const string ImageMagick = "magick";
    public const string SevenZip = "7zip";
    public const string Pdfium = "pdfium";
    public const string LibreOffice = "libreoffice";
    public const string Calibre = "calibre";
}

public static class EngineCatalog
{
    public static readonly IReadOnlyList<EngineDefinition> All =
    [
        new()
        {
            Id = EngineIds.FFmpeg, Name = "FFmpeg",
            Purpose = "Vidéo et audio : conversion, remuxage, extraction audio, GIF, miniatures.",
            License = "GPL v3 (version « essentials » de gyan.dev). Exécuté comme programme séparé, non lié à FileWheel.",
            Homepage = "https://www.gyan.dev/ffmpeg/builds/",
            ExeNames = ["ffmpeg.exe", "ffmpeg"],
            KnownPaths = [@"%ProgramFiles%\ffmpeg\bin\ffmpeg.exe", @"%LOCALAPPDATA%\Microsoft\WinGet\Links\ffmpeg.exe"],
            Method = InstallMethod.GitHubRelease, GitHubRepo = "GyanD/codexffmpeg",
            AssetPattern = @"^ffmpeg-[0-9][0-9.]*-essentials_build\.zip$", WingetId = "Gyan.FFmpeg", ApproxMiB = 100,
        },
        new()
        {
            Id = EngineIds.FFprobe, Name = "FFprobe",
            Purpose = "Analyse des fichiers audio/vidéo (durée, pistes, codecs). Fourni avec FFmpeg.",
            License = "GPL v3 (fourni avec FFmpeg).", Homepage = "https://ffmpeg.org/ffprobe.html",
            ExeNames = ["ffprobe.exe", "ffprobe"],
            KnownPaths = [@"%ProgramFiles%\ffmpeg\bin\ffprobe.exe", @"%LOCALAPPDATA%\Microsoft\WinGet\Links\ffprobe.exe"],
            Method = InstallMethod.Manual,
        },
        new()
        {
            Id = EngineIds.ImageMagick, Name = "ImageMagick",
            Purpose = "Images : WebP, AVIF, HEIC, SVG, TGA, DDS, EXR, HDR, PSD, RAW, ICO, redimensionnement.",
            License = "Licence ImageMagick (type Apache 2.0).", Homepage = "https://imagemagick.org/",
            ExeNames = ["magick.exe", "magick"],
            KnownPaths = [@"%ProgramFiles%\ImageMagick*\magick.exe"],
            Method = InstallMethod.GitHubRelease, GitHubRepo = "ImageMagick/ImageMagick",
            AssetPattern = @"portable-Q16(-HDRI)?-x64\.(zip|7z)$", HelperAssetPattern = null, WingetId = "ImageMagick.ImageMagick", ApproxMiB = 60,
        },
        new()
        {
            Id = EngineIds.SevenZip, Name = "7-Zip",
            Purpose = "Archives 7Z, XZ, BZ2, ZIP chiffrés, vérification d'intégrité. Extraction RAR si 7-Zip est installé.",
            License = "GNU LGPL 2.1 (+ restriction unRAR pour le module RAR de la version installée).", Homepage = "https://www.7-zip.org/",
            ExeNames = ["7z.exe", "7za.exe", "7zz", "7z", "7za"],
            KnownPaths = [@"%ProgramFiles%\7-Zip\7z.exe", @"%ProgramFiles(x86)%\7-Zip\7z.exe"],
            Method = InstallMethod.GitHubRelease, GitHubRepo = "ip7z/7zip",
            AssetPattern = @"^7z[0-9]+-extra\.7z$", HelperAssetPattern = @"^7zr\.exe$", WingetId = "7zip.7zip", ApproxMiB = 2,
        },
        new()
        {
            Id = EngineIds.Pdfium, Name = "PDFium",
            Purpose = "PDF : pages en images, extraction du texte, nombre de pages.",
            License = "BSD 3 clauses / Apache 2.0 (binaires de bblanchon/pdfium-binaries).", Homepage = "https://github.com/bblanchon/pdfium-binaries",
            ExeNames = ["pdfium.dll", "libpdfium.so", "libpdfium.dylib"],
            Method = InstallMethod.GitHubRelease, GitHubRepo = "bblanchon/pdfium-binaries",
            AssetPattern = @"^pdfium-win-x64\.tgz$", ApproxMiB = 5, IsLibrary = true,
        },
        new()
        {
            Id = EngineIds.LibreOffice, Name = "LibreOffice",
            Purpose = "Documents, classeurs et présentations : DOCX, ODT, RTF, XLSX, ODS, PPTX, ODP vers PDF et entre eux.",
            License = "MPL 2.0.", Homepage = "https://www.libreoffice.org/",
            ExeNames = ["soffice.com", "soffice.exe", "soffice", "libreoffice"],
            KnownPaths = [@"%ProgramFiles%\LibreOffice\program\soffice.com", @"%ProgramFiles%\LibreOffice\program\soffice.exe", @"%ProgramFiles(x86)%\LibreOffice\program\soffice.exe"],
            Method = InstallMethod.Winget, WingetId = "TheDocumentFoundation.LibreOffice", ApproxMiB = 350,
        },
        new()
        {
            Id = EngineIds.Calibre, Name = "Calibre (ebook-convert)",
            Purpose = "Livres numériques : MOBI, AZW3, FB2, EPUB, PDF (sans DRM).",
            License = "GPL v3.", Homepage = "https://calibre-ebook.com/",
            ExeNames = ["ebook-convert.exe", "ebook-convert"],
            KnownPaths = [@"%ProgramFiles%\Calibre2\ebook-convert.exe", @"%ProgramFiles(x86)%\Calibre2\ebook-convert.exe"],
            Method = InstallMethod.Winget, WingetId = "calibre.calibre", ApproxMiB = 200,
        },
    ];

    public static EngineDefinition Get(string id) => All.First(e => e.Id == id);
}
