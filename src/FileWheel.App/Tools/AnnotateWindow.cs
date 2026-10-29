using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FileWheel.App.Ui;

namespace FileWheel.App.Tools;

/// <summary>
/// Annoter / masquer : stylo, fleche, cadre, texte, zone noire (masquer une info sensible) et pixellisation.
/// Annuler (Ctrl+Z) retire le dernier ajout. Resultat « nom annotée.ext » a cote de l'original ;
/// les zones masquees le sont dans les pixels (impossible de « retirer » le masque du fichier produit).
/// </summary>
public sealed class AnnotateWindow : ToolWindow
{
    private enum Tool { Pen, Arrow, Box, Text, Redact, Pixelate }

    private readonly ImageIO.Loaded _src;
    private readonly ImageSurface _surface = new();
    private readonly List<UIElement> _undo = new();
    private Tool _tool = Tool.Arrow;
    private Color _color = Color.FromRgb(0xFF, 0x3B, 0x30);
    private double _thickness;
    private Point _start;
    private UIElement? _current;
    private Polyline? _pen;
    private bool _drawing;

    public AnnotateWindow(ImageIO.Loaded src) : base("Annoter / masquer", System.IO.Path.GetFileName(src.Path))
    {
        _src = src;
        double longest = Math.Max(src.Bitmap.PixelWidth, src.Bitmap.PixelHeight);
        _thickness = Math.Max(2, longest * 0.005);
        _surface.SetImage(src.Bitmap);
        _surface.Overlay.Cursor = Cursors.Cross;
        PreviewHost.Child = _surface;
        _surface.Overlay.MouseLeftButtonDown += OnDown;
        _surface.Overlay.MouseMove += OnMove;
        _surface.Overlay.MouseLeftButtonUp += OnUp;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Undo(); e.Handled = true; } };

        Section("Outil");
        Add(Ui.Ui.Segmented([(Tool.Arrow, "Flèche"), (Tool.Box, "Cadre"), (Tool.Pen, "Stylo")], _tool, v => _tool = v, "outil"));
        var row2 = Ui.Ui.Segmented([(Tool.Text, "Texte"), (Tool.Redact, "Masquer"), (Tool.Pixelate, "Flouter")], _tool, v => _tool = v, "outil");
        row2.Margin = new Thickness(0, 8, 0, 0);
        Add(row2);
        Add(Ui.Ui.Hint("Masquer pose un rectangle noir ; Flouter pixellise la zone. Les deux sont définitifs dans le fichier créé."));

        Section("Couleur");
        var colors = new WrapPanel();
        foreach (var hex in new[] { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#AF52DE", "#FFFFFF", "#1D1D1F" })
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new Button
            {
                Width = 34, Height = 34, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0), ToolTip = hex,
                Content = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(c), Stroke = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)) },
            };
            if (Application.Current?.TryFindResource(Theme.SubtleButtonStyle) is Style st) b.Style = st;
            b.Click += (_, _) => _color = c;
            colors.Children.Add(b);
        }
        Add(colors);

        Section("Épaisseur");
        var s = Ui.Ui.Slider(1, Math.Max(4, longest * 0.03), _thickness, v => _thickness = v, 1);
        s.Width = 260;
        Add(s);

        Section("Modifier");
        Add(Ui.Ui.Row(Ui.Ui.Button("Annuler l'ajout", Undo), Ui.Ui.Button("Tout effacer", () => { foreach (var e in _undo) _surface.Overlay.Children.Remove(e); _undo.Clear(); })));
        Add(Ui.Ui.Hint("Ctrl+Z annule le dernier ajout."));

        SetPrimary("Enregistrer", Save);
        Status.Text = $"{src.Bitmap.PixelWidth} × {src.Bitmap.PixelHeight} px";
    }

    private Brush Stroke => new SolidColorBrush(_color);

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_surface.Overlay);
        if (_tool == Tool.Text)
        {
            var text = TextPromptDialog.Ask("Texte", "Texte à ajouter :", "", t => t.Trim().Length == 0 ? "Texte vide." : null);
            if (text is null) return;
            double size = Math.Max(14, Math.Max(_src.Bitmap.PixelWidth, _src.Bitmap.PixelHeight) * 0.035);
            var tb = new TextBlock
            {
                Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Stroke, FontFamily = Ui.Ui.TextFont,
                Effect = new DropShadowEffect { BlurRadius = size * 0.25, ShadowDepth = 0, Opacity = 0.6, Color = _color.R + _color.G + _color.B > 600 ? Colors.Black : Colors.White },
            };
            Canvas.SetLeft(tb, _start.X);
            Canvas.SetTop(tb, _start.Y - size * 0.7);
            Push(tb);
            return;
        }
        _drawing = true;
        _surface.Overlay.CaptureMouse();
        switch (_tool)
        {
            case Tool.Pen:
                _pen = new Polyline { Stroke = Stroke, StrokeThickness = _thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                _pen.Points.Add(_start);
                _current = _pen;
                break;
            case Tool.Arrow:
                _current = new System.Windows.Shapes.Path { Fill = Stroke, Stroke = Stroke, StrokeThickness = _thickness, StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round };
                break;
            case Tool.Box:
                _current = new Rectangle { Stroke = Stroke, StrokeThickness = _thickness, RadiusX = _thickness, RadiusY = _thickness };
                break;
            case Tool.Redact:
                _current = new Rectangle { Fill = Brushes.Black };
                break;
            case Tool.Pixelate:
                _current = new Rectangle { Stroke = Brushes.White, StrokeThickness = Math.Max(1, _thickness / 2), StrokeDashArray = [4, 3], Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
                break;
        }
        if (_current is not null) _surface.Overlay.Children.Add(_current);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _current is null) return;
        var p = e.GetPosition(_surface.Overlay);
        switch (_current)
        {
            case Polyline pl:
                pl.Points.Add(p);
                break;
            case System.Windows.Shapes.Path path:
                path.Data = Arrow(_start, p, _thickness);
                break;
            case Rectangle r:
                var rect = new Rect(_start, p);
                Canvas.SetLeft(r, rect.X); Canvas.SetTop(r, rect.Y);
                r.Width = rect.Width; r.Height = rect.Height;
                break;
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        _surface.Overlay.ReleaseMouseCapture();
        var el = _current;
        _current = null;
        _pen = null;
        if (el is null) return;
        _surface.Overlay.Children.Remove(el);
        var end = e.GetPosition(_surface.Overlay);
        if ((end - _start).Length < 3 && el is not Polyline) return; // simple clic : rien
        if (_tool == Tool.Pixelate && el is Rectangle box)
        {
            var px = Pixelated(new Rect(Canvas.GetLeft(box), Canvas.GetTop(box), box.Width, box.Height));
            if (px is not null) Push(px);
            return;
        }
        Push(el);
    }

    private void Push(UIElement el)
    {
        if (!_surface.Overlay.Children.Contains(el)) _surface.Overlay.Children.Add(el);
        _undo.Add(el);
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _surface.Overlay.Children.Remove(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    /// <summary>Fleche : trait + pointe pleine, proportionnee a l'epaisseur.</summary>
    private static Geometry Arrow(Point a, Point b, double t)
    {
        var v = b - a;
        double len = v.Length;
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        if (len < 1) return g;
        v /= len;
        var n = new Vector(-v.Y, v.X);
        double head = Math.Min(len * 0.6, t * 4.5);
        var basePt = b - v * head;
        g.Children.Add(new LineGeometry(a, basePt));
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(b, true, true);
            ctx.LineTo(basePt + n * head * 0.55, true, true);
            ctx.LineTo(basePt - n * head * 0.55, true, true);
        }
        g.Children.Add(tri);
        return g;
    }

    /// <summary>Zone pixellisee (blocs d'environ 1/60e de l'image) posee a l'emplacement choisi.</summary>
    private Image? Pixelated(Rect r)
    {
        var bmp = _src.Bitmap;
        r.Intersect(new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
        if (r.IsEmpty || r.Width < 2 || r.Height < 2) return null;
        var ir = new Int32Rect((int)r.X, (int)r.Y, Math.Max(1, (int)r.Width), Math.Max(1, (int)r.Height));
        double block = Math.Max(6, Math.Max(bmp.PixelWidth, bmp.PixelHeight) / 60.0);
        var crop = new CroppedBitmap(bmp, ir);
        var small = new TransformedBitmap(crop, new ScaleTransform(Math.Max(1 / block, 2.0 / ir.Width), Math.Max(1 / block, 2.0 / ir.Height)));
        var img = new Image { Source = small, Width = ir.Width, Height = ir.Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(img, ir.X);
        Canvas.SetTop(img, ir.Y);
        return img;
    }

    private void Save()
    {
        if (_undo.Count == 0) { Status.Text = "Rien à enregistrer : ajoutez au moins une annotation."; return; }
        var bmp = _src.Bitmap;
        var layer = new Canvas { Width = bmp.PixelWidth, Height = bmp.PixelHeight };
        foreach (var el in _surface.Overlay.Children.Cast<UIElement>().ToList())
        {
            _surface.Overlay.Children.Remove(el);
            layer.Children.Add(el);
        }
        var scene = new Grid { Width = bmp.PixelWidth, Height = bmp.PixelHeight };
        var picture = new Image { Source = bmp, Width = bmp.PixelWidth, Height = bmp.PixelHeight, Stretch = Stretch.Fill };
        scene.Children.Add(picture);
        scene.Children.Add(layer);
        var result = ImageIO.RenderElement(scene, bmp.PixelWidth, bmp.PixelHeight);
        var ext = ImageIO.OutputExtension(_src.Path);
        Done(ImageIO.SaveNew(ImageIO.Encode(result, ext, 95), ImageIO.OutputPath(_src.Path, "annotée", ext)));
    }
}
