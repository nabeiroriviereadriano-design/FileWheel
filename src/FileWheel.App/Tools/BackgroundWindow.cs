using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FileWheel.App.Ui;

namespace FileWheel.App.Tools;

/// <summary>
/// Ajouter un fond (facon « capture d'ecran soignee ») : degrade ou couleur unie, marge, coins arrondis,
/// ombre et format du cadre. Resultat PNG « nom fond.png » a cote de l'original.
/// Tous les reglages sont relatifs a la taille de l'image : l'apercu (image reduite) et l'export
/// (pleine resolution) donnent exactement le meme rendu.
/// </summary>
public sealed class BackgroundWindow : ToolWindow
{
    private sealed record Swatch(string Name, Brush Brush, bool Transparent = false);

    private readonly ImageIO.Loaded _src;
    private readonly BitmapSource _preview;
    private readonly Viewbox _view = new() { Stretch = Stretch.Uniform, Margin = new Thickness(16) };
    private readonly List<Swatch> _swatches;
    private Swatch _bg;
    private double _padding = 0.08, _corners = 0.03, _shadow = 0.5;
    private double? _aspect; // null = suit l'image

    public BackgroundWindow(ImageIO.Loaded src) : base("Ajouter un fond", System.IO.Path.GetFileName(src.Path))
    {
        _src = src;
        _preview = ImageIO.Fit(src.Bitmap, 1400);
        PreviewHost.Child = _view;

        var accent = Theme.AccentColor;
        _swatches =
        [
            new("Coucher de soleil", Diagonal("#FF8A3D", "#FF3D77")),
            new("Pêche", Diagonal("#FFC48C", "#E8677C")),
            new("Océan", Diagonal("#2E8BFF", "#7F5CFF")),
            new("Menthe", Diagonal("#3DDC97", "#2BC0E4")),
            new("Aurore", Diagonal("#B89CFF", "#FFC2E2")),
            new("Accent", Diagonal(Theme.Mix(accent, Colors.White, 0.25), Theme.Mix(accent, Colors.Black, 0.25))),
            new("Nuit", Diagonal("#2B2D33", "#4A4E57")),
            new("Blanc", Solid("#FFFFFF")),
            new("Gris clair", Solid("#F2F2F5")),
            new("Noir", Solid("#111113")),
            new("Transparent", Brushes.Transparent, Transparent: true),
        ];
        _bg = _swatches[0];

        Section("Fond");
        var grid = new WrapPanel();
        foreach (var sw in _swatches)
        {
            var b = new Button
            {
                Width = 40, Height = 40, Margin = new Thickness(0, 0, 8, 8), ToolTip = sw.Name, Padding = new Thickness(0),
                Content = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                    Background = sw.Transparent ? new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)) : sw.Brush,
                    Child = sw.Transparent ? new TextBlock { Text = "∅", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
                },
            };
            if (Application.Current?.TryFindResource(Theme.SubtleButtonStyle) is Style st) b.Style = st;
            b.Click += (_, _) => { _bg = sw; Refresh(); };
            grid.Children.Add(b);
        }
        Add(grid);

        Section("Mise en forme");
        Add(Slider("Marge", 0, 0.25, _padding, v => _padding = v));
        Add(Slider("Coins arrondis", 0, 0.12, _corners, v => _corners = v));
        Add(Slider("Ombre", 0, 1, _shadow, v => _shadow = v));

        Section("Format du cadre");
        Add(Ui.Ui.Segmented([("img", "Image"), ("1:1", "1:1"), ("4:3", "4:3"), ("16:9", "16:9")], "img", v =>
        {
            _aspect = v switch { "1:1" => 1.0, "4:3" => 4.0 / 3, "16:9" => 16.0 / 9, _ => null };
            Refresh();
        }));

        SetPrimary("Enregistrer en PNG", Save);
        Refresh();
    }

    private UIElement Slider(string label, double min, double max, double value, Action<double> set)
    {
        var s = Ui.Ui.Slider(min, max, value, v => { set(v); Refresh(); }, (max - min) / 100);
        s.Width = 260;
        return Ui.Ui.Column(Ui.Ui.Text(label, null, 12), s);
    }

    private static Brush Diagonal(string a, string b) => Diagonal((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b));

    private static Brush Diagonal(Color a, Color b)
    {
        var g = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    private static Brush Solid(string c)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
        b.Freeze();
        return b;
    }

    /// <summary>Compose la scene (fond + image arrondie + ombre) pour une image donnee ; renvoie aussi sa taille.</summary>
    private FrameworkElement Compose(BitmapSource img, out int width, out int height)
    {
        double iw = img.PixelWidth, ih = img.PixelHeight;
        double longest = Math.Max(iw, ih), shortest = Math.Min(iw, ih);
        double pad = _padding * longest;
        double w = iw + 2 * pad, h = ih + 2 * pad;
        if (_aspect is { } a)
        {
            if (w / h < a) w = h * a; else h = w / a;
        }
        width = (int)Math.Round(w);
        height = (int)Math.Round(h);
        double radius = _corners * shortest;

        var picture = new Image { Source = img, Width = iw, Height = ih, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        picture.Clip = new RectangleGeometry(new Rect(0, 0, iw, ih), radius, radius);
        var holder = new Grid { Width = iw, Height = ih, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(picture);
        if (_shadow > 0.01)
            holder.Effect = new DropShadowEffect
            {
                BlurRadius = Math.Min(200, longest * 0.06 * _shadow + 4), ShadowDepth = longest * 0.012 * _shadow, Direction = 270,
                Opacity = 0.25 + 0.35 * _shadow, Color = Colors.Black, RenderingBias = RenderingBias.Quality,
            };
        var scene = new Grid { Width = width, Height = height, Background = _bg.Brush, SnapsToDevicePixels = true };
        scene.Children.Add(holder);
        return scene;
    }

    private void Refresh()
    {
        var scene = Compose(_preview, out _, out _);
        _view.Child = scene;
        Compose(_src.Bitmap, out int fw, out int fh);
        Status.Text = $"Résultat : {fw} × {fh} px (PNG)";
    }

    private void Save()
    {
        var img = _src.Bitmap;
        Compose(img, out int w, out int h);
        // Garde-fou memoire : 8 000 px au plus sur le grand cote
        if (Math.Max(w, h) > 8000)
        {
            img = ImageIO.Fit(img, (int)(Math.Max(img.PixelWidth, img.PixelHeight) * 8000.0 / Math.Max(w, h)));
        }
        var scene = Compose(img, out w, out h);
        var bmp = ImageIO.RenderElement(scene, w, h);
        var data = ImageIO.Encode(bmp, ".png");
        Done(ImageIO.SaveNew(data, ImageIO.OutputPath(_src.Path, "fond", ".png")));
    }
}
