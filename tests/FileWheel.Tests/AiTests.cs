using FileWheel.Conversion.Ai;
using FileWheel.Conversion.Engines;
using static FileWheel.Tests.Env;
using static FileWheel.Tests.T;

namespace FileWheel.Tests;

/// <summary>
/// Detourage IA : le meme code C# que sous Windows (liaison ONNX Runtime, pretraitement, masque),
/// execute ici avec la bibliotheque Linux d'ONNX Runtime et le vrai modele IS-Net,
/// puis compare a une reference Python (onnxruntime + Pillow, pretraitement de rembg).
/// </summary>
public static class AiTests
{
    public static async Task RunAll()
    {
        Suite("Détourage IA (IS-Net, ONNX Runtime)");

        await Run("Masque identique à la référence rembg (même modèle) et sujet détouré", () =>
        {
            RequireEngine(EngineIds.BackgroundRemoval);
            var model = Engine.Dependencies.PathOf(EngineIds.BackgroundRemoval)!;
            var lib = BackgroundRemover.FindRuntime(Path.GetDirectoryName(model)!) ?? throw new SkipException("ONNX Runtime absent");
            var png = Path.Combine(Work, "sujet.png");
            Must("convert", "logo:", "-resize", "800x600", png);
            var raw = Path.Combine(Work, "sujet.rgba");
            Must("convert", png, "-depth", "8", "rgba:" + raw);
            var (w, h) = (800, 600);
            var rgba = File.ReadAllBytes(raw);
            Equal(w * h * 4, rgba.Length, "pixels");

            using var remover = BackgroundRemover.Open(lib, model);
            var mask = remover.ComputeMask(rgba, w, h);
            Equal(w * h, mask.Length, "taille du masque");

            var refPgm = Path.Combine(Work, "masque-référence.pgm");
            var py = Sh("python3", Path.Combine(AppContext.BaseDirectory, "bg_reference.py"), model, png, refPgm);
            if (py.code != 0) Skip("référence Python indisponible : " + py.stderr.Trim().Split('\n').Last());
            var reference = ReadPgm(refPgm, w, h);
            double diff = 0;
            int agree = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                diff += Math.Abs(mask[i] - reference[i]);
                if ((mask[i] >= 128) == (reference[i] >= 128)) agree++;
            }
            double mean = diff / mask.Length, ratio = (double)agree / mask.Length;
            Check(ratio > 0.98, $"pixels d'accord avec la référence : {ratio:P2}");
            Check(mean < 8, $"écart moyen : {mean:F2} / 255");
            int fg = mask.Count(v => v >= 128);
            Check(fg > mask.Length * 0.05 && fg < mask.Length * 0.9, $"sujet trouvé ({fg * 100 / mask.Length} % de l'image)");

            BackgroundRemover.ApplyMask(rgba, mask, 0.3);
            Check(rgba.Where((_, i) => i % 4 == 3).Any(a => a == 0), "fond transparent");
            Check(rgba.Where((_, i) => i % 4 == 3).Any(a => a == 255), "sujet opaque");
        });
    }

    private static byte[] ReadPgm(string path, int w, int h)
    {
        var bytes = File.ReadAllBytes(path);
        // En-tete « P5\nW H\n255\n »
        int fields = 0, i = 0;
        while (fields < 4)
        {
            while (char.IsWhiteSpace((char)bytes[i])) i++;
            while (!char.IsWhiteSpace((char)bytes[i])) i++;
            fields++;
        }
        i++;
        return bytes[i..(i + w * h)];
    }
}
