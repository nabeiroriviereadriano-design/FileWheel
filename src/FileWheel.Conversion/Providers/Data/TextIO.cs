using System.Text;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Data;

/// <summary>Lecture de fichiers texte avec detection d'encodage (BOM, UTF-8 valide, sinon Windows-1252).</summary>
public static class TextIO
{
    /// <summary>Taille maximale d'un fichier texte traite en memoire par les convertisseurs integres.</summary>
    public const long MaxTextBytes = 512L << 20;

    static TextIO()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Windows1252 => Encoding.GetEncoding(1252);

    public static (string text, Encoding encoding) Read(string path)
    {
        var fi = new FileInfo(path);
        if (fi.Length > MaxTextBytes)
            throw new ConversionException(ConversionErrorKind.TooLarge, $"Fichier texte trop volumineux pour le convertisseur intégré (limite {MaxTextBytes >> 20} Mio).");
        var bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    public static (string text, Encoding encoding) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);
        try
        {
            return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            return (Windows1252.GetString(bytes), Windows1252);
        }
    }

    public static void WriteUtf8(string path, string text, bool bom = false) =>
        File.WriteAllText(path, text, new UTF8Encoding(bom));
}
