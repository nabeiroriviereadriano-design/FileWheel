using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Providers.Pdf;

namespace FileWheel.Conversion.Ai;

/// <summary>
/// Detourage automatique (suppression de l'arriere-plan) avec le modele IS-Net « general use »
/// (DIS, licence Apache 2.0), execute localement par ONNX Runtime : l'image ne quitte jamais le PC.
/// Pretraitement identique a celui de rembg : image RVB redimensionnee en 1024 × 1024,
/// valeurs divisees par le maximum puis centrees (−0,5) ; sortie normalisee entre son minimum et son maximum.
/// </summary>
public sealed class BackgroundRemover : IDisposable
{
    public const int ModelSize = 1024;
    public const string ModelFile = "isnet-general-use.onnx";
    private readonly OnnxRuntime.Session _session;

    private BackgroundRemover(OnnxRuntime.Session s) => _session = s;

    /// <summary>Charge ONNX Runtime et le modele installes avec le moteur « Détourage IA ».</summary>
    public static BackgroundRemover Open(DependencyManager deps)
    {
        var model = deps.Require(EngineIds.BackgroundRemoval);
        var dir = Path.GetDirectoryName(model)!;
        var lib = FindRuntime(dir) ?? throw new ConversionException(ConversionErrorKind.EngineMissing,
            "ONNX Runtime est absent du moteur « Détourage IA » : réinstallez-le depuis Paramètres › Moteurs.");
        return Open(lib, model);
    }

    public static BackgroundRemover Open(string runtimeLibrary, string modelPath)
    {
        OnnxRuntime.EnsureLoaded(runtimeLibrary);
        return new BackgroundRemover(new OnnxRuntime.Session(modelPath));
    }

    public static string? FindRuntime(string dir)
    {
        foreach (var name in new[] { "onnxruntime.dll", "libonnxruntime.so", "libonnxruntime.dylib" })
        {
            var hit = Directory.EnumerateFiles(dir, name + "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".pdb") && !f.EndsWith(".lib")).OrderBy(f => f.Length).FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>
    /// Masque de premier plan (0 = fond, 255 = sujet) aux dimensions de l'image.
    /// <paramref name="rgba"/> : pixels RVBA 8 bits (ou BVRA si <paramref name="bgra"/>).
    /// </summary>
    public byte[] ComputeMask(byte[] rgba, int width, int height, bool bgra = false, CancellationToken ct = default)
    {
        if (width < 1 || height < 1) throw new ConversionException(ConversionErrorKind.InvalidInput, "Image vide.");
        // 1. RVB en 1024 × 1024
        var rgb = new byte[(long)width * height * 3];
        for (long i = 0, j = 0; i < (long)width * height; i++, j += 4)
        {
            rgb[i * 3] = rgba[j + (bgra ? 2 : 0)];
            rgb[i * 3 + 1] = rgba[j + 1];
            rgb[i * 3 + 2] = rgba[j + (bgra ? 0 : 2)];
        }
        var small = ResizeRgb(rgb, width, height, ModelSize, ModelSize);
        ct.ThrowIfCancellationRequested();

        // 2. Normalisation (comme rembg) et passage en CHW
        int max = 1;
        foreach (var b in small) if (b > max) max = b;
        const int plane = ModelSize * ModelSize;
        var input = new float[3 * plane];
        for (int i = 0; i < plane; i++)
        {
            input[i] = small[i * 3] / (float)max - 0.5f;
            input[plane + i] = small[i * 3 + 1] / (float)max - 0.5f;
            input[2 * plane + i] = small[i * 3 + 2] / (float)max - 0.5f;
        }

        // 3. Inference
        var (output, shape) = _session.Run(input, [1, 3, ModelSize, ModelSize]);
        ct.ThrowIfCancellationRequested();
        int oh = (int)shape[^2], ow = (int)shape[^1];
        var pred = new float[ow * oh];
        Array.Copy(output, pred, pred.Length);
        float mi = float.MaxValue, ma = float.MinValue;
        foreach (var v in pred) { if (v < mi) mi = v; if (v > ma) ma = v; }
        float range = Math.Max(1e-6f, ma - mi);
        for (int i = 0; i < pred.Length; i++) pred[i] = (pred[i] - mi) / range;

        // 4. Retour aux dimensions d'origine
        return Resample.BilinearGray(pred, ow, oh, width, height);
    }

    /// <summary>
    /// Applique le masque a l'image : alpha = masque, ajuste par une courbe de contour
    /// (<paramref name="softness"/> 0 = contours nets, 1 = contours tres doux).
    /// </summary>
    public static void ApplyMask(byte[] rgba, byte[] mask, double softness)
    {
        softness = Math.Clamp(softness, 0, 1);
        // Nets : seuil serre autour de 50 % ; doux : masque tel quel
        double lo = 0.5 * (1 - softness) * 0.9, hi = 1 - lo;
        for (int i = 0; i < mask.Length; i++)
        {
            double a = mask[i] / 255.0;
            a = Math.Clamp((a - lo) / Math.Max(1e-6, hi - lo), 0, 1);
            int alpha = (int)Math.Round(a * rgba[i * 4 + 3]);
            rgba[i * 4 + 3] = (byte)alpha;
        }
    }

    private static byte[] ResizeRgb(byte[] rgb, int w, int h, int nw, int nh)
    {
        if (w == nw && h == nh) return rgb;
        if (w >= nw && h >= nh) return Resample.AreaRgb(rgb, w, h, nw, nh);
        // Agrandissement (petite image) : bilineaire
        var dst = new byte[(long)nw * nh * 3];
        double sx = (double)w / nw, sy = (double)h / nh;
        for (int y = 0; y < nh; y++)
        {
            double fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, h - 1);
            int y0 = (int)fy, y1 = Math.Min(h - 1, y0 + 1);
            double ty = fy - y0;
            for (int x = 0; x < nw; x++)
            {
                double fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, w - 1);
                int x0 = (int)fx, x1 = Math.Min(w - 1, x0 + 1);
                double tx = fx - x0;
                for (int c = 0; c < 3; c++)
                {
                    double v = (rgb[(y0 * w + x0) * 3 + c] * (1 - tx) + rgb[(y0 * w + x1) * 3 + c] * tx) * (1 - ty)
                             + (rgb[(y1 * w + x0) * 3 + c] * (1 - tx) + rgb[(y1 * w + x1) * 3 + c] * tx) * ty;
                    dst[((long)y * nw + x) * 3 + c] = (byte)Math.Clamp(Math.Round(v), 0, 255);
                }
            }
        }
        return dst;
    }

    public void Dispose() => _session.Dispose();
}
