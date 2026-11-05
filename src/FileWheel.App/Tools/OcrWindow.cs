using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileWheel.App.Ui;
using FileWheel.Conversion.Providers.Pdf;
using FileWheel.Core.Localization;
using FileWheel.Core.Logging;
using FileWheel.Windows.Ocr;

namespace FileWheel.App.Tools;

/// <summary>
/// Reconnaissance de texte (OCR integre a Windows) d'une image ou d'un PDF scanne :
/// le texte s'affiche, se copie en un clic ou s'enregistre en .txt a cote de l'original.
/// </summary>
public sealed class OcrWindow : ToolWindow
{
    private readonly string _path;
    private readonly bool _isPdf;
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 14, Padding = new Thickness(12), BorderThickness = new Thickness(0),
    };
    private string? _language;
    private CancellationTokenSource? _cts;

    public OcrWindow(string path) : base("Reconnaître le texte", Path.GetFileName(path), 980, 680)
    {
        _path = path;
        _isPdf = Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        PreviewHost.Background = null;
        PreviewHost.Child = _text;
        _text.SetResourceReference(TextBox.BackgroundProperty, Theme.Surface);

        var langs = OcrService.Languages();
        Section("Langue du texte");
        if (langs.Count == 0)
        {
            Add(Ui.Ui.Text("Aucune langue de reconnaissance installée.", Ui.Ui.Warning));
            Add(Ui.Ui.Hint("Paramètres Windows › Heure et langue › Langue : ajoutez une langue avec l'option « Reconnaissance optique de caractères »."));
        }
        else
        {
            var items = new List<(string?, string)> { (null, Loc.T("Langues de Windows")) };
            items.AddRange(langs.Select(l => ((string?)l.Tag, l.DisplayName)));
            Add(Ui.Ui.Combo(items, null, v => { _language = v; _ = RunAsync(); }));
        }
        Add(Ui.Ui.Hint("Reconnaissance faite par Windows, sur ce PC : rien n'est envoyé sur Internet."));

        Section("Résultat");
        Add(Ui.Ui.Button("Copier le texte", Copy, glyph: ""));
        Add(Ui.Ui.Hint("Vous pouvez corriger le texte avant de le copier ou de l'enregistrer."));
        SetPrimary("Enregistrer en .txt", Save);
        Loaded += async (_, _) => await RunAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task RunAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        if (Primary is not null) Primary.IsEnabled = false;
        _text.Text = "";
        Status.Text = Loc.T("Reconnaissance du texte…");
        try
        {
            var text = _isPdf ? await RecognizePdfAsync(cts.Token) : await RecognizeImageAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            _text.Text = text;
            Status.Text = text.Length == 0
                ? Loc.T("Aucun texte reconnu (image trop floue, trop petite, ou langue différente).")
                : Loc.F("{0} caractères reconnus", text.Length);
            if (Primary is not null) Primary.IsEnabled = text.Length > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn("OCR : " + ex.Message);
            Status.Text = Loc.T(ex.Message);
        }
    }

    private async Task<string> RecognizeImageAsync(CancellationToken ct)
    {
        var img = ImageIO.Load(_path);
        var (bgra, w, h) = Pixels(ImageIO.Fit(img.Bitmap, OcrService.MaxDimension));
        return await OcrService.RecognizeAsync(bgra, w, h, _language, ct);
    }

    private async Task<string> RecognizePdfAsync(CancellationToken ct)
    {
        var deps = Services.Engine.Dependencies;
        PdfTools.EnsureEngine(deps);
        var pages = new List<string>();
        using var doc = Pdfium.Open(_path, null);
        int n = doc.PageCount;
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            Status.Text = Loc.F("Page {0} / {1}…", i + 1, n);
            int index = i;
            // 200 ppp : bon compromis lisibilite / limite de taille du moteur
            var (bgra, w, h) = await Task.Run(() =>
            {
                var (pw, ph) = doc.PageSize(index);
                double dpi = Math.Min(200, OcrService.MaxDimension / (Math.Max(pw, ph) / 72.0));
                var (rw, rh, rgba) = doc.Render(index, dpi);
                for (int k = 0; k < rgba.Length; k += 4) (rgba[k], rgba[k + 2]) = (rgba[k + 2], rgba[k]);
                return (rgba, rw, rh);
            }, ct);
            var text = await OcrService.RecognizeAsync(bgra, w, h, _language, ct);
            pages.Add(n > 1 ? $"— {Loc.F("Page {0}", i + 1)} —\n{text}" : text);
        }
        return string.Join("\n\n", pages).Trim();
    }

    private static (byte[] Bgra, int W, int H) Pixels(BitmapSource bmp)
    {
        BitmapSource src = bmp.Format == PixelFormats.Bgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);
        return (px, w, h);
    }

    private void Copy()
    {
        if (_text.Text.Length == 0) return;
        Clipboard.SetText(_text.Text);
        Status.Text = Loc.T("Texte copié dans le presse-papiers.");
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        var target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(_path)} {Loc.T("texte")}.txt");
        var bytes = new System.Text.UTF8Encoding(true).GetBytes(_text.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Done(ImageIO.SaveNew(bytes, target));
    }
}
