using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Logging;

namespace FileWheel.App.Tools;

/// <summary>
/// Lecture et ecriture d'images pour les outils (codecs Windows / WIC).
/// - lecture : l'orientation EXIF est appliquee (une photo de telephone s'affiche droite) ;
/// - ecriture : fichier temporaire dans le dossier cible, puis renommage vers un nom libre :
///   l'original n'est jamais modifie ni ecrase.
/// </summary>
public static class ImageIO
{
    /// <summary>Formats que Windows sait ecrire ; les autres sont enregistres en PNG.</summary>
    private static readonly HashSet<string> Writable = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public sealed record Loaded(BitmapSource Bitmap, BitmapMetadata? Metadata, string Path, long Bytes)
    {
        public bool HasAlpha => Bitmap.Format.Masks.Count == 4 || Bitmap.Format == PixelFormats.Bgra32 || Bitmap.Format == PixelFormats.Pbgra32
                                || Bitmap.Format == PixelFormats.Prgba64 || Bitmap.Format == PixelFormats.Rgba64 || Bitmap.Format == PixelFormats.Rgba128Float;
    }

    public static Loaded Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        BitmapDecoder dec;
        try
        {
            dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException("Windows ne sait pas lire ce format d'image. Convertissez-le d'abord en PNG (Convertir › PNG).", ex);
        }
        var frame = dec.Frames[0];
        BitmapMetadata? meta = null;
        // Copie en memoire : les metadonnees restent lisibles apres la fermeture du fichier.
        try { meta = (frame.Metadata as BitmapMetadata)?.Clone(); } catch { }
        BitmapSource bmp = frame;
        int orientation = ReadOrientation(meta);
        if (orientation > 1) bmp = ApplyOrientation(bmp, orientation);
        if (bmp.Format != PixelFormats.Bgra32 && bmp.Format != PixelFormats.Pbgra32)
            bmp = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        bmp = Normalize96(bmp);
        bmp.Freeze();
        return new Loaded(bmp, meta, path, new FileInfo(path).Length);
    }

    /// <summary>Ramene la resolution a 96 ppp : les rendus WPF se font alors pixel pour pixel.</summary>
    private static BitmapSource Normalize96(BitmapSource b)
    {
        if (Math.Abs(b.DpiX - 96) < 0.5 && Math.Abs(b.DpiY - 96) < 0.5) return b;
        int stride = b.PixelWidth * 4;
        var px = new byte[stride * b.PixelHeight];
        b.CopyPixels(px, stride, 0);
        return BitmapSource.Create(b.PixelWidth, b.PixelHeight, 96, 96, b.Format, null, px, stride);
    }

    private static int ReadOrientation(BitmapMetadata? meta)
    {
        if (meta is null) return 1;
        foreach (var q in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try { if (meta.ContainsQuery(q) && meta.GetQuery(q) is ushort o) return o; } catch { }
        }
        return 1;
    }

    private static BitmapSource ApplyOrientation(BitmapSource b, int o)
    {
        Transform t = o switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => Transform.Identity,
        };
        return new TransformedBitmap(b, t);
    }

    /// <summary>Rotation par pas de 90 degres et miroir horizontal.</summary>
    public static BitmapSource Rotate(BitmapSource b, int quarterTurns, bool mirror)
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;
        if (quarterTurns == 0 && !mirror) return b;
        var g = new TransformGroup();
        if (mirror) g.Children.Add(new ScaleTransform(-1, 1));
        if (quarterTurns != 0) g.Children.Add(new RotateTransform(90 * quarterTurns));
        var r = new TransformedBitmap(b, g);
        r.Freeze();
        return r;
    }

    /// <summary>Extension de sortie : celle de l'original si Windows sait l'ecrire, sinon .png (ou .jpg si demande).</summary>
    public static string OutputExtension(string sourcePath, string? forced = null)
    {
        if (forced is not null) return forced;
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        return Writable.Contains(ext) ? (ext == ".jpeg" ? ".jpg" : ext) : ".png";
    }

    /// <summary>« photo.jpg » + « recadrée » → « photo recadrée.jpg » (ou « photo recadrée (2).jpg »).</summary>
    public static string OutputPath(string sourcePath, string suffix, string extension, string? folder = null)
    {
        var dir = folder ?? Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        return NameConflict.UniquePath(Path.Combine(dir, $"{stem} {suffix}{extension}"));
    }

    /// <summary>Encode l'image selon l'extension (qualite JPEG 1-100).</summary>
    public static byte[] Encode(BitmapSource bmp, string extension, int jpegQuality = 92)
    {
        BitmapEncoder enc = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            _ => new PngBitmapEncoder(),
        };
        BitmapSource src = bmp;
        if (enc is JpegBitmapEncoder or BmpBitmapEncoder)
        {
            // JPEG/BMP n'ont pas de transparence : fond blanc plutot que noir, puis 24 bits.
            if (HasTransparency(bmp)) src = Flatten(bmp, Colors.White);
            src = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        }
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Ecrit sans jamais ecraser : temporaire dans le dossier cible puis renommage. Renvoie le chemin final.</summary>
    public static string SaveNew(byte[] data, string desiredPath)
    {
        var dir = Path.GetDirectoryName(desiredPath)!;
        var tmp = Path.Combine(dir, $".~fw-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tmp, data);
            var final = NameConflict.UniquePath(desiredPath);
            File.Move(tmp, final);
            Log.Info($"Image enregistrée : {Path.GetFileName(final)} ({data.Length / 1024} Kio)");
            return final;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    public static bool HasTransparency(BitmapSource bmp)
    {
        if (bmp.Format != PixelFormats.Bgra32 && bmp.Format != PixelFormats.Pbgra32) return false;
        int stride = bmp.PixelWidth * 4;
        var px = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(px, stride, 0);
        for (int i = 3; i < px.Length; i += 4) if (px[i] < 250) return true;
        return false;
    }

    /// <summary>Pose l'image sur un fond uni (calcul direct sur les pixels : utilisable hors du fil de l'interface).</summary>
    public static BitmapSource Flatten(BitmapSource bmp, Color background)
    {
        var src = bmp.Format == PixelFormats.Bgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a == 255) continue;
            px[i] = (byte)((px[i] * a + background.B * (255 - a)) / 255);
            px[i + 1] = (byte)((px[i + 1] * a + background.G * (255 - a)) / 255);
            px[i + 2] = (byte)((px[i + 2] * a + background.R * (255 - a)) / 255);
            px[i + 3] = 255;
        }
        var r = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        r.Freeze();
        return r;
    }

    /// <summary>Rendu d'un visuel en image (96 ppp, pixels = unites WPF).</summary>
    public static BitmapSource Render(Visual v, int width, int height)
    {
        var rtb = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Rendu d'un element WPF (avec ses effets : ombre...) hors fenetre.</summary>
    public static BitmapSource RenderElement(FrameworkElement e, int width, int height)
    {
        e.Measure(new Size(width, height));
        e.Arrange(new Rect(0, 0, width, height));
        e.UpdateLayout();
        return Render(e, width, height);
    }

    /// <summary>Redimensionne pour que le plus grand cote ne depasse pas <paramref name="maxSide"/> (0 = inchange).</summary>
    public static BitmapSource Fit(BitmapSource b, int maxSide)
    {
        int longest = Math.Max(b.PixelWidth, b.PixelHeight);
        if (maxSide <= 0 || longest <= maxSide) return b;
        double k = (double)maxSide / longest;
        var r = new TransformedBitmap(b, new ScaleTransform(k, k));
        r.Freeze();
        return r;
    }

    public static string Size(long bytes) => SafeFileOps.FormatSize(bytes);
}
