namespace FileWheel.Conversion.Engines;

public enum InstallMethod
{
    /// <summary>Archive portable publiee sur GitHub, verifiee par son empreinte SHA-256 publiee par GitHub.</summary>
    GitHubRelease,
    /// <summary>Installation par winget (gestionnaire de paquets de Windows, empreintes verifiees par winget).</summary>
    Winget,
    /// <summary>Installation manuelle par l'utilisateur ; FileWheel detecte l'executable.</summary>
    Manual,
    /// <summary>Fichiers de versions precises, adresses et empreintes SHA-256 fixees dans le code.</summary>
    Pinned,
}

/// <summary>Fichier d'une installation « Pinned » : adresse officielle, taille et empreinte connues d'avance.</summary>
/// <param name="ExtractEntry">Pour une archive ZIP : seul ce fichier (chemin dans l'archive) est extrait.</param>
public sealed record PinnedFile(string Url, string Sha256, long Size, string? ExtractEntry = null);

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
    /// <summary>Methode Pinned : version et fichiers (Windows x64).</summary>
    public string? PinnedVersion { get; init; }
    public IReadOnlyList<PinnedFile> PinnedFiles { get; init; } = [];
    /// <summary>Autres moteurs necessaires (affiches dans les parametres).</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];
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
    public const string BackgroundRemoval = "detourage";
    public const string VcRedist = "vcredist";
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
        new()
        {
            Id = EngineIds.BackgroundRemoval, Name = "Détourage IA (IS-Net + ONNX Runtime)",
            Purpose = "Retire l'arrière-plan des photos (outil « Détourer »). Le modèle d'IA s'exécute sur votre PC : l'image n'est envoyée nulle part.",
            License = "Modèle IS-Net « general use » (DIS) : Apache 2.0, distribué par le projet rembg (MIT). ONNX Runtime : MIT (Microsoft).",
            Homepage = "https://github.com/xuebinqin/DIS",
            ExeNames = ["isnet-general-use.onnx"],
            Method = InstallMethod.Pinned, PinnedVersion = "IS-Net general use + ONNX Runtime 1.31.1", ApproxMiB = 190, IsLibrary = true,
            PinnedFiles =
            [
                new("https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx",
                    "60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a", 178_648_008),
                new("https://github.com/microsoft/onnxruntime/releases/download/v1.31.1/onnxruntime-win-x64-1.31.1.zip",
                    "5869e26bb25314375f2d5ef1877e3ea06b1543d6c710e6e4a1ec2f7177ab4b2c", 82_929_944,
                    ExtractEntry: "onnxruntime-win-x64-1.31.1/lib/onnxruntime.dll"),
            ],
            Requires = [EngineIds.VcRedist],
        },
        new()
        {
            Id = EngineIds.VcRedist, Name = "Microsoft Visual C++ 2015-2022 (x64)",
            Purpose = "Bibliothèques Microsoft nécessaires à ONNX Runtime (détourage IA). Souvent déjà présentes sur le PC.",
            License = "Licence logicielle Microsoft (redistribuable gratuit).", Homepage = "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist",
            ExeNames = ["msvcp140_1.dll"],
            KnownPaths = [@"%SystemRoot%\System32\msvcp140_1.dll"],
            Method = InstallMethod.Winget, WingetId = "Microsoft.VCRedist.2015+.x64", ApproxMiB = 25, IsLibrary = true,
        },
    ];

    public static EngineDefinition Get(string id) => All.First(e => e.Id == id);
}
