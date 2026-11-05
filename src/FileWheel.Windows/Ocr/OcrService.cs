using System.Text;
#if !FW_TYPECHECK
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
#endif

namespace FileWheel.Windows.Ocr;

/// <summary>Langue de reconnaissance disponible sur le PC.</summary>
public sealed record OcrLanguage(string Tag, string DisplayName);

/// <summary>
/// Reconnaissance de texte integree a Windows (Windows.Media.Ocr) : gratuite, locale, sans telechargement.
/// Les langues disponibles sont celles installees dans Windows avec la « reconnaissance optique de caracteres »
/// (Parametres › Heure et langue › Langue).
/// </summary>
public static class OcrService
{
#if !FW_TYPECHECK
    /// <summary>Langues de reconnaissance installees.</summary>
    public static IReadOnlyList<OcrLanguage> Languages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages.Select(l => new OcrLanguage(l.LanguageTag, l.NativeName)).ToList();
        }
        catch { return []; }
    }

    /// <summary>Taille maximale (en pixels) d'un cote d'image acceptee par le moteur.</summary>
    public static int MaxDimension
    {
        get { try { return (int)OcrEngine.MaxImageDimension; } catch { return 4096; } }
    }

    /// <summary>
    /// Reconnait le texte d'une image BGRA 8 bits (largeur × hauteur × 4 octets).
    /// <paramref name="languageTag"/> null = langues du profil Windows.
    /// </summary>
    public static async Task<string> RecognizeAsync(byte[] bgra, int width, int height, string? languageTag, CancellationToken ct = default)
    {
        var engine = languageTag is null
            ? OcrEngine.TryCreateFromUserProfileLanguages()
            : OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language(languageTag));
        if (engine is null)
            throw new InvalidOperationException("Aucune langue de reconnaissance de texte n'est installée dans Windows. Ajoutez-en une dans Paramètres › Heure et langue › Langue (option « Reconnaissance optique de caractères »).");
        var buffer = CryptographicBuffer.CreateFromByteArray(bgra);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        return Assemble(result.Lines.Select(l => (l.Text,
            l.Words.Count > 0 ? l.Words.Min(w => w.BoundingRect.Y) : 0.0,
            l.Words.Count > 0 ? l.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height) : 0.0)).ToList());
    }
#else
    public static IReadOnlyList<OcrLanguage> Languages() => [];
    public static int MaxDimension => 4096;
    public static Task<string> RecognizeAsync(byte[] bgra, int width, int height, string? languageTag, CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("Reconnaissance de texte : Windows uniquement.");
#endif

    /// <summary>Lignes reconnues → texte ; une ligne vide la ou l'ecart vertical marque un nouveau paragraphe.</summary>
    public static string Assemble(IReadOnlyList<(string Text, double Top, double Bottom)> lines)
    {
        var sb = new StringBuilder();
        double prevBottom = double.NaN, prevHeight = 0;
        foreach (var (text, top, bottom) in lines)
        {
            double h = Math.Max(1, bottom - top);
            if (!double.IsNaN(prevBottom) && top - prevBottom > Math.Max(h, prevHeight) * 0.9) sb.AppendLine();
            sb.AppendLine(text);
            prevBottom = bottom;
            prevHeight = h;
        }
        return sb.ToString().TrimEnd();
    }
}
