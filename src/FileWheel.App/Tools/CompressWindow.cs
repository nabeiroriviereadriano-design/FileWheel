using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileWheel.App.Ui;
using FileWheel.Core.Logging;

namespace FileWheel.App.Tools;

/// <summary>
/// Compresser une ou plusieurs images : qualite, taille maximale, format (JPEG / PNG / WebP non : Windows
/// ne sait pas l'ecrire). Estimation du poids en direct sur la premiere image. Les metadonnees ne sont pas
/// recopiees (gain de place et confidentialite). Un resultat plus lourd que l'original n'est pas garde.
/// </summary>
public sealed class CompressWindow : ToolWindow
{
    private readonly IReadOnlyList<string> _files;
    private readonly ImageIO.Loaded _first;
    private readonly Image _picture = new() { Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(16) };
    private readonly TextBlock _estimate = Ui.Ui.Text("", null, 15);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _quality = 75;
    private int _maxSide;
    private string _format;
    private int _estimateRun;

    public CompressWindow(IReadOnlyList<string> files, ImageIO.Loaded first)
        : base("Compresser", files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} images", 980, 640)
    {
        _files = files;
        _first = first;
        _picture.Source = ImageIO.Fit(first.Bitmap, 1600);
        PreviewHost.Child = _picture;
        // Par defaut : JPEG pour les photos, PNG si l'image a de la transparence
        _format = first.HasAlpha && ImageIO.HasTransparency(first.Bitmap) ? ".png" : ".jpg";

        Section("Poids estimé");
        _estimate.FontWeight = FontWeights.SemiBold;
        Add(_estimate);
        Add(Ui.Ui.Hint(files.Count > 1 ? "Estimation sur la première image ; les mêmes réglages s'appliquent à toutes." : "Le fichier d'origine est conservé."));

        Section("Format");
        Add(Ui.Ui.Segmented([(".jpg", "JPEG"), (".png", "PNG")], _format, v => { _format = v; Schedule(); }));
        Add(Ui.Ui.Hint("JPEG : idéal pour les photos (pas de transparence). PNG : sans perte, garde la transparence ; la taille maximale fait l'essentiel du gain."));

        Section("Qualité (JPEG)");
        var qText = Ui.Ui.Text($"{_quality}");
        var q = Ui.Ui.Slider(30, 95, _quality, v => { _quality = (int)v; qText.Text = $"{(int)v}"; Schedule(); }, 1);
        Add(Ui.Ui.Row(q, qText));

        Section("Taille maximale");
        Add(Ui.Ui.Segmented([(0, "Originale"), (3840, "4K"), (2560, "2560"), (1920, "1920"), (1280, "1280")], 0, v => { _maxSide = v; Schedule(); }));

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = EstimateAsync(); };
        SetPrimary(files.Count > 1 ? $"Compresser {files.Count} images" : "Compresser", Run);
        Schedule();
    }

    private void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
        _estimate.Text = "Calcul…";
    }

    private async Task EstimateAsync()
    {
        int run = ++_estimateRun;
        var src = _first.Bitmap;
        int max = _maxSide, quality = _quality;
        string fmt = _format;
        long size;
        try
        {
            size = await Task.Run(() => (long)ImageIO.Encode(ImageIO.Fit(src, max), fmt, quality).Length);
        }
        catch (Exception ex)
        {
            _estimate.Text = "Estimation impossible";
            Log.Debug("Estimation de compression : " + ex.Message);
            return;
        }
        if (run != _estimateRun) return;
        double gain = 1 - (double)size / Math.Max(1, _first.Bytes);
        _estimate.Text = $"{ImageIO.Size(_first.Bytes)} → {ImageIO.Size(size)}" + (gain > 0 ? $"  (−{gain:P0})" : "  (pas de gain)");
        var fitted = ImageIO.Fit(src, max);
        Status.Text = $"{fitted.PixelWidth} × {fitted.PixelHeight} px";
    }

    private void Run()
    {
        if (Primary is not null) Primary.IsEnabled = false;
        var created = new List<string>();
        var skipped = new List<string>();
        foreach (var f in _files)
        {
            Status.Text = $"Compression de {Path.GetFileName(f)}…";
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background); // laisse l'interface se redessiner
            ImageIO.Loaded img;
            try { img = f == _first.Path ? _first : ImageIO.Load(f); }
            catch (Exception ex) { skipped.Add($"{Path.GetFileName(f)} : {ex.Message}"); continue; }
            var data = ImageIO.Encode(ImageIO.Fit(img.Bitmap, _maxSide), _format, _quality);
            if (data.Length >= img.Bytes && _maxSide == 0 && Path.GetExtension(f).Equals(_format, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add($"{Path.GetFileName(f)} : déjà optimisée (le résultat serait plus lourd)");
                continue;
            }
            created.Add(ImageIO.SaveNew(data, ImageIO.OutputPath(f, "compressée", _format)));
        }
        if (skipped.Count > 0)
            Ui.Ui.Message((created.Count > 0 ? $"{created.Count} image(s) compressée(s).\n\n" : "") + "Non traitées :\n" + string.Join("\n", skipped.Take(8)),
                "Compresser", image: MessageBoxImage.Information, owner: this);
        if (created.Count > 0) Done(created[^1]);
        else Close();
    }
}
