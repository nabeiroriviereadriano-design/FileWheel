using FileWheel.Core.Formats;

namespace FileWheel.Conversion.Core;

/// <summary>
/// Verification des fichiers produits avant de les presenter comme reussis :
/// existence, taille non nulle, et signature coherente avec le format annonce.
/// Un moteur qui rend « 0 » mais produit un fichier vide ou d'un autre type est un echec.
/// </summary>
public static class OutputValidator
{
    private static readonly HashSet<string> TextLike = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "html", "csv", "tsv", "json", "xml", "yaml", "svg", "obj", "ply", "stl", "gltf", "eml", "vcf", "ics", "rtf", "dxf",
    };

    public static void ValidateFile(string path, string expectedFormatId)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists)
            throw new ConversionException(ConversionErrorKind.WriteError, "Le moteur n'a produit aucun fichier.");
        if (fi.Length == 0)
            throw new ConversionException(ConversionErrorKind.WriteError, "Le fichier produit est vide : conversion considérée comme échouée.");

        var detected = FormatDetector.Detect(path);
        var got = detected.Format?.Id;
        if (got is null)
        {
            if (TextLike.Contains(expectedFormatId)) return;
            throw new ConversionException(ConversionErrorKind.WriteError, $"Le fichier produit n'est pas reconnu comme {FormatCatalog.Get(expectedFormatId)?.Label ?? expectedFormatId}.");
        }
        if (Compatible(expectedFormatId, got)) return;
        throw new ConversionException(ConversionErrorKind.WriteError,
            $"Le fichier produit est de type {detected.Format!.Label} au lieu de {FormatCatalog.Get(expectedFormatId)?.Label ?? expectedFormatId}.");
    }

    public static void ValidateFolder(string dir)
    {
        if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any())
            throw new ConversionException(ConversionErrorKind.WriteError, "Aucun fichier n'a été produit.");
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            if (new FileInfo(f).Length == 0)
                throw new ConversionException(ConversionErrorKind.WriteError, $"Fichier vide produit : {Path.GetFileName(f)}.");
    }

    public static bool Compatible(string expected, string got)
    {
        if (string.Equals(expected, got, StringComparison.OrdinalIgnoreCase)) return true;
        if (TextLike.Contains(expected) && (TextLike.Contains(got) || got == "txt")) return true;
        return (expected, got) switch
        {
            ("m4a", "mp4") or ("mp4", "m4a") or ("m4v", "mp4") or ("mp4", "m4v") or ("mov", "mp4") or ("3gp", "mp4") => true,
            ("mkv", "webm") or ("webm", "mkv") => true,
            ("opus", "ogg") or ("ogg", "opus") => true,
            ("wma", "wmv") or ("wmv", "wma") => true,
            ("tgz", "gz") or ("gz", "tgz") => true,
            ("aac", "mp3") or ("mp3", "aac") => true,
            ("ts", "mpeg") or ("mpeg", "ts") => true,
            ("azw3", "mobi") or ("mobi", "azw3") => true,
            ("otf", "ttf") or ("ttf", "otf") => true,
            ("heic", "avif") or ("avif", "heic") => true,
            ("epub", "zip") => true,
            ("3mf", "zip") => true,
            _ => false,
        };
    }
}
