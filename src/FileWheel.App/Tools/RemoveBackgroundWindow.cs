using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileWheel.App.Ui;
using FileWheel.Conversion.Ai;
using FileWheel.Core.Localization;
using FileWheel.Core.Logging;

namespace FileWheel.App.Tools;

/// <summary>
/// Detourer : le modele IS-Net (moteur optionnel « Détourage IA ») calcule le masque du sujet sur ce PC,
/// puis l'utilisateur regle la douceur des contours et le fond (transparent, blanc, noir).
/// Resultat : « photo détourée.png » a cote de l'original.
/// </summary>
public sealed class RemoveBackgroundWindow : ToolWindow
{
    private static BackgroundRemover? _remover; // modele garde en memoire entre deux utilisations
    private static readonly object RemoverGate = new();

    private readonly ImageIO.Loaded _src;
    private readonly ImageSurface _surface = new();
    private byte[]? _mask;
    private double _softness = 0.35;
    private Color? _fill;

    public RemoveBackgroundWindow(ImageIO.Loaded src) : base("Détourer", Path.GetFileName(src.Path))
    {
        _src = src;
        PreviewHost.Child = _surface;
        _surface.SetImage(src.Bitmap);

        Section("Contours");
        Add(Ui.Ui.Slider(0, 1, _softness, v => { _softness = v; Render(); }, 0.05));
        Add(Ui.Ui.Hint("À gauche : contours nets. À droite : contours doux (cheveux, fourrure)."));
        Section("Fond");
        Add(Ui.Ui.Segmented<string>([("none", "Transparent"), ("white", "Blanc"), ("black", "Noir")], "none", v =>
        {
            _fill = v switch { "white" => Colors.White, "black" => Colors.Black, _ => null };
            Render();
        }));
        Add(Ui.Ui.Hint("Le détourage est calculé par un modèle d'IA sur ce PC : l'image n'est envoyée nulle part."));
        SetPrimary("Enregistrer en PNG", Save);
        if (Primary is not null) Primary.IsEnabled = false;
        Loaded += async (_, _) => await ComputeAsync();
    }

    private async Task ComputeAsync()
    {
        Status.Text = Loc.T("Analyse de l'image par le modèle d'IA…");
        try
        {
            var deps = Services.Engine.Dependencies;
            var (bgra, w, h) = Pixels(_src.Bitmap);
            _mask = await Task.Run(() =>
            {
                lock (RemoverGate)
                {
                    _remover ??= BackgroundRemover.Open(deps);
                    return _remover.ComputeMask(bgra, w, h, bgra: true);
                }
            });
            Render();
            Status.Text = Loc.F("{0} × {1} px · sujet détouré", w, h);
            if (Primary is not null) Primary.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error("Détourage", ex);
            Status.Text = Loc.T("Détourage impossible.");
            Ui.Ui.Message(FileWheel.Conversion.Core.ConversionManager.Translate(ex).Message, image: MessageBoxImage.Warning, owner: this);
        }
    }

    private BitmapSource Compose()
    {
        var (bgra, w, h) = Pixels(_src.Bitmap);
        BackgroundRemover.ApplyMask(bgra, _mask!, _softness);
        if (_fill is { } c)
        {
            for (int i = 0; i < bgra.Length; i += 4)
            {
                int a = bgra[i + 3];
                bgra[i] = (byte)((bgra[i] * a + c.B * (255 - a)) / 255);
                bgra[i + 1] = (byte)((bgra[i + 1] * a + c.G * (255 - a)) / 255);
                bgra[i + 2] = (byte)((bgra[i + 2] * a + c.R * (255 - a)) / 255);
                bgra[i + 3] = 255;
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        return bmp;
    }

    private void Render()
    {
        if (_mask is null) return;
        _surface.SetImage(Compose());
    }

    private void Save()
    {
        if (_mask is null) return;
        var data = ImageIO.Encode(Compose(), ".png");
        Done(ImageIO.SaveNew(data, ImageIO.OutputPath(_src.Path, "détourée", ".png")));
    }

    private static (byte[] Bgra, int W, int H) Pixels(BitmapSource bmp)
    {
        BitmapSource src = bmp.Format == PixelFormats.Bgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);
        return (px, w, h);
    }
}
