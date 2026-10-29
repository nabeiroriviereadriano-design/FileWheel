using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using FileWheel.Conversion.Core;
using FileWheel.Core.Formats;
using FileWheel.Core.Logging;

namespace FileWheel.Windows.Imaging;

/// <summary>
/// Images avec les codecs integres a Windows (WIC), sans rien installer :
/// lecture PNG, JPEG, BMP, TIFF, GIF, ICO (+ WebP, HEIC, AVIF si les extensions Windows sont presentes),
/// ecriture PNG, JPEG, BMP, TIFF, GIF. ImageMagick reste prefere quand il est installe
/// (plus de formats, metadonnees conservees).
/// </summary>
public sealed class WicImageProvider : ProviderBase
{
    public override string Id => "wic";
    public override string DisplayName => "Images (codecs Windows intégrés)";

    private IReadOnlyList<ConversionCapability>? _caps;

    private static readonly Dictionary<string, string> ExtToId = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "png", [".jpg"] = "jpg", [".jpeg"] = "jpg", [".bmp"] = "bmp", [".tif"] = "tiff", [".tiff"] = "tiff",
        [".gif"] = "gif", [".ico"] = "ico", [".webp"] = "webp", [".heic"] = "heic", [".heif"] = "heic", [".avif"] = "avif",
        [".jxr"] = "jxr", [".dng"] = "raw",
    };

    /// <summary>Formats lisibles d'apres les decodeurs WIC declares dans le registre.</summary>
    public static HashSet<string> InstalledDecoders()
    {
        var set = new HashSet<string> { "png", "jpg", "bmp", "tiff", "gif", "ico" };
        try
        {
            using var inst = Registry.ClassesRoot.OpenSubKey(@"CLSID\{7ED96837-96F0-4812-B211-F13C24117ED3}\Instance");
            if (inst is null) return set;
            foreach (var name in inst.GetSubKeyNames())
            {
                using var k = inst.OpenSubKey(name);
                var clsid = k?.GetValue("CLSID") as string ?? name;
                using var c = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid);
                if (c?.GetValue("FileExtensions") is not string exts) continue;
                foreach (var e in exts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (ExtToId.TryGetValue(e, out var id) && FormatCatalog.Get(id) is not null) set.Add(id);
            }
        }
        catch (Exception ex) { Log.Debug("Décodeurs WIC : " + ex.Message); }
        return set;
    }

    public override IReadOnlyList<ConversionCapability> Capabilities => _caps ??= Build();

    private List<ConversionCapability> Build()
    {
        var sources = InstalledDecoders();
        var caps = new List<ConversionCapability>();
        foreach (var to in new[] { "png", "jpg", "bmp", "tiff", "gif" })
        {
            var opts = new List<OptionDef>();
            if (to == "jpg") opts.Add(OptionDef.Int("quality", "Qualité", 90, 1, 100, "%"));
            opts.Add(OptionDef.Choice("resize", "Redimensionner", "none", ("none", "Taille d'origine"), ("percent", "Pourcentage"), ("width", "Largeur fixe"), ("height", "Hauteur fixe"), ("fit", "Tenir dans un cadre L × H")));
            opts.Add(OptionDef.Int("percent", "Pourcentage", 50, 1, 1000, "%"));
            opts.Add(OptionDef.Int("width", "Largeur", 1920, 1, 30000, "px"));
            opts.Add(OptionDef.Int("height", "Hauteur", 1080, 1, 30000, "px"));
            opts.Add(OptionDef.Bool("enlarge", "Autoriser l'agrandissement", false) with { Advanced = true });
            opts.Add(OptionDef.Choice("rotate", "Rotation", "0", ("0", "Aucune"), ("90", "90° horaire"), ("180", "180°"), ("270", "90° anti-horaire")));
            opts.Add(OptionDef.Bool("autoorient", "Appliquer l'orientation EXIF", true) with { Advanced = true });
            opts.Add(OptionDef.Choice("background", "Fond si la transparence est perdue", "white", ("white", "Blanc"), ("black", "Noir")) with { Advanced = true });
            var dst = FormatCatalog.Require(to);
            caps.Add(Cap(Id, sources, to, ConversionKind.Convert, opts, dst.Lossy ? LossKind.Lossy : LossKind.None,
                "Moteur intégré à Windows : les métadonnées (EXIF, GPS) ne sont pas recopiées.",
                "Images animées : seule la première image est convertie.", priority: 25));
        }
        return caps;
    }

    public override Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        // WPF exige un thread STA pour certains objets d'imagerie : thread dedie
        var tcs = new TaskCompletionSource<ConversionResult>();
        var t = new Thread(() =>
        {
            try { tcs.SetResult(Convert(req, ct)); }
            catch (Exception ex) { tcs.SetException(ex is ConversionException ? ex : ConversionManager.Translate(ex)); }
        }) { IsBackground = true, Name = "FileWheel.WIC" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    private static ConversionResult Convert(ConversionRequest req, CancellationToken ct)
    {
        var warnings = new List<string>();
        BitmapDecoder decoder;
        using var fs = new FileStream(req.Input, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new ConversionException(ConversionErrorKind.Corrupt, "Image illisible par les codecs Windows (endommagée, ou codec non installé).", ex.Message, ex);
        }
        if (decoder.Frames.Count == 0) throw new ConversionException(ConversionErrorKind.Corrupt, "Image vide.");
        if (decoder.Frames.Count > 1 && req.SourceFormat.Animation) warnings.Add("Image animée : seule la première image a été convertie.");
        BitmapSource img = decoder.Frames[0];
        ct.ThrowIfCancellationRequested();
        if ((long)img.PixelWidth * img.PixelHeight > 200_000_000) throw new ConversionException(ConversionErrorKind.TooLarge, "Image trop grande pour le moteur intégré.");

        // Orientation EXIF
        if (req.OptBool("autoorient") && decoder.Frames[0].Metadata is BitmapMetadata md)
        {
            object? o = null;
            try { o = md.GetQuery("/app1/ifd/{ushort=274}"); } catch { }
            try { o ??= md.GetQuery("/ifd/{ushort=274}"); } catch { }
            if (o is ushort orient && orient is >= 2 and <= 8) img = Orient(img, orient);
        }
        // Rotation demandee
        if (int.TryParse(req.Opt("rotate"), out var rot) && rot is 90 or 180 or 270)
            img = new TransformedBitmap(img, new RotateTransform(rot));
        // Redimensionnement
        img = Resize(img, req);

        var target = req.TargetFormat;
        bool alphaTarget = target is "png" or "tiff" or "gif";
        img = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
        if (!alphaTarget) img = Flatten(img, req.Opt("background") == "black" ? (byte)0 : (byte)255, warnings);

        BitmapEncoder encoder = target switch
        {
            "png" => new PngBitmapEncoder(),
            "jpg" => new JpegBitmapEncoder { QualityLevel = req.OptInt("quality", 90) },
            "bmp" => new BmpBitmapEncoder(),
            "tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            "gif" => new GifBitmapEncoder(),
            _ => throw new ConversionException(ConversionErrorKind.NotSupported, "Format de sortie non pris en charge."),
        };
        encoder.Frames.Add(BitmapFrame.Create(img));
        using (var outFs = new FileStream(req.TempOutput, FileMode.CreateNew))
            encoder.Save(outFs);
        return new ConversionResult([req.TempOutput], warnings);
    }

    private static BitmapSource Orient(BitmapSource img, ushort o)
    {
        Transform t = o switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => Transform.Identity,
        };
        return new TransformedBitmap(img, t);
    }

    private static BitmapSource Resize(BitmapSource img, ConversionRequest req)
    {
        double w = img.PixelWidth, h = img.PixelHeight;
        double sx = 1, sy = 1;
        bool enlarge = req.OptBool("enlarge");
        switch (req.Opt("resize"))
        {
            case "percent": sx = sy = req.OptInt("percent", 50) / 100.0; break;
            case "width": sx = sy = req.OptInt("width", 1920) / w; break;
            case "height": sx = sy = req.OptInt("height", 1080) / h; break;
            case "fit": sx = sy = Math.Min(req.OptInt("width", 1920) / w, req.OptInt("height", 1080) / h); break;
            default: return img;
        }
        if (!enlarge && req.Opt("resize") != "percent") { sx = Math.Min(sx, 1); sy = Math.Min(sy, 1); }
        if (Math.Abs(sx - 1) < 1e-9 && Math.Abs(sy - 1) < 1e-9) return img;
        var scaled = new TransformedBitmap(img, new ScaleTransform(sx, sy));
        return scaled;
    }

    private static BitmapSource Flatten(BitmapSource bgra, byte bg, List<string> warnings)
    {
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[(long)stride * h];
        bgra.CopyPixels(px, stride, 0);
        bool any = false;
        var outPx = new byte[(long)w * 3 * h];
        for (long i = 0, j = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a != 255) any = true;
            outPx[j++] = (byte)((px[i] * a + bg * (255 - a)) / 255);
            outPx[j++] = (byte)((px[i + 1] * a + bg * (255 - a)) / 255);
            outPx[j++] = (byte)((px[i + 2] * a + bg * (255 - a)) / 255);
        }
        if (any) warnings.Add($"Transparence supprimée : fond {(bg == 0 ? "noir" : "blanc")}.");
        return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, outPx, w * 3);
    }
}
