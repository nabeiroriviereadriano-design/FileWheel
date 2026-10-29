using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FileWheel.App.Ui;
using FileWheel.Core.Wheel;

namespace FileWheel.App.Tools;

/// <summary>
/// Recadrer : cadre deplacable/redimensionnable (8 poignees), proportions (libre, 1:1, 4:3, 16:9...),
/// rotation par quart de tour et miroir. Resultat : « nom recadrée.ext » a cote de l'original.
/// </summary>
public sealed class CropWindow : ToolWindow
{
    private readonly ImageIO.Loaded _src;
    private BitmapSource _work;
    private int _turns;
    private bool _mirror;
    private double? _ratio; // largeur / hauteur ; null = libre
    private Rect _crop;
    private readonly ImageSurface _surface = new();
    private readonly System.Windows.Shapes.Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _frame = new() { Stroke = Brushes.White, IsHitTestVisible = false };
    private readonly List<Rectangle> _handles = new();
    private readonly List<Line> _thirds = new();
    private readonly TextBlock _sizeText = Ui.Ui.Text("");

    private enum Drag { None, Move, New, N, S, E, W, NE, NW, SE, SW }
    private Drag _drag;
    private Point _dragStart;
    private Rect _cropStart;

    public CropWindow(ImageIO.Loaded src) : base("Recadrer", System.IO.Path.GetFileName(src.Path))
    {
        _src = src;
        _work = src.Bitmap;
        PreviewHost.Child = _surface;
        foreach (var _ in Enumerable.Range(0, 4)) { var l = new Line { Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), IsHitTestVisible = false }; _thirds.Add(l); }
        foreach (var _ in Enumerable.Range(0, 8))
        {
            var h = new Rectangle { Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false, RadiusX = 2, RadiusY = 2 };
            _handles.Add(h);
        }
        _surface.Overlay.Children.Add(_shade);
        foreach (var l in _thirds) _surface.Overlay.Children.Add(l);
        _surface.Overlay.Children.Add(_frame);
        foreach (var h in _handles) _surface.Overlay.Children.Add(h);
        _surface.Overlay.MouseLeftButtonDown += OnDown;
        _surface.Overlay.MouseMove += OnMove;
        _surface.Overlay.MouseLeftButtonUp += (_, _) => { _drag = Drag.None; _surface.Overlay.ReleaseMouseCapture(); };
        _surface.SizeChanged += (_, _) => Redraw();

        Section("Proportions");
        var ratios = new (double? value, string label)[] { (null, "Libre"), (1.0, "1:1"), (4.0 / 3, "4:3"), (3.0 / 4, "3:4"), (16.0 / 9, "16:9"), (9.0 / 16, "9:16") };
        Add(Ui.Ui.Segmented(ratios.Select(r => (r.label, r.label)), "Libre", lbl =>
        {
            _ratio = ratios.First(r => r.label == lbl).value;
            ApplyRatio();
        }));
        Add(Ui.Ui.Hint("Glissez dans l'image pour tracer un cadre, déplacez-le ou tirez sur ses poignées. Maj : proportions conservées."));

        Section("Orientation");
        Add(Ui.Ui.Row(
            Ui.Ui.Button("↺ Gauche", () => Turn(-1)),
            Ui.Ui.Button("↻ Droite", () => Turn(1)),
            Ui.Ui.Button("⇋ Miroir", () => { _mirror = !_mirror; Reset(); })));

        Section("Résultat");
        Add(_sizeText);
        Add(Ui.Ui.Button("Tout sélectionner", () => { _crop = new Rect(0, 0, _work.PixelWidth, _work.PixelHeight); ApplyRatio(); }));

        SetPrimary("Enregistrer", Save);
        Reset();
    }

    private void Turn(int q) { _turns += q; Reset(); }

    private void Reset()
    {
        _work = ImageIO.Rotate(_src.Bitmap, _turns, _mirror);
        _surface.SetImage(_work);
        _crop = new Rect(0, 0, _work.PixelWidth, _work.PixelHeight);
        ApplyRatio();
    }

    /// <summary>Plus grand cadre aux proportions choisies, centre sur le cadre actuel.</summary>
    private void ApplyRatio()
    {
        if (_ratio is { } r)
        {
            double w = _crop.Width, h = _crop.Height;
            if (w / h > r) w = h * r; else h = w / r;
            var c = new Point(_crop.X + _crop.Width / 2, _crop.Y + _crop.Height / 2);
            _crop = new Rect(c.X - w / 2, c.Y - h / 2, w, h);
            Clamp();
        }
        Redraw();
    }

    private void Clamp()
    {
        double W = _work.PixelWidth, H = _work.PixelHeight;
        double w = Math.Min(_crop.Width, W), h = Math.Min(_crop.Height, H);
        double x = Math.Clamp(_crop.X, 0, W - w), y = Math.Clamp(_crop.Y, 0, H - h);
        _crop = new Rect(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    private void Redraw()
    {
        double k = 1 / Math.Max(0.01, _surface.DisplayScale); // 1 px ecran en pixels image
        double W = _work.PixelWidth, H = _work.PixelHeight;
        var outer = new RectangleGeometry(new Rect(0, 0, W, H));
        var inner = new RectangleGeometry(_crop);
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        Canvas.SetLeft(_frame, _crop.X); Canvas.SetTop(_frame, _crop.Y);
        _frame.Width = _crop.Width; _frame.Height = _crop.Height; _frame.StrokeThickness = 2 * k;
        for (int i = 0; i < 2; i++)
        {
            double fx = _crop.X + _crop.Width * (i + 1) / 3, fy = _crop.Y + _crop.Height * (i + 1) / 3;
            Set(_thirds[i], fx, _crop.Y, fx, _crop.Bottom, k);
            Set(_thirds[i + 2], _crop.X, fy, _crop.Right, fy, k);
        }
        double hs = 10 * k;
        var pts = HandlePoints();
        for (int i = 0; i < 8; i++)
        {
            _handles[i].Width = _handles[i].Height = hs;
            _handles[i].StrokeThickness = k;
            Canvas.SetLeft(_handles[i], pts[i].X - hs / 2);
            Canvas.SetTop(_handles[i], pts[i].Y - hs / 2);
        }
        _sizeText.Text = $"{(int)Math.Round(_crop.Width)} × {(int)Math.Round(_crop.Height)} px";
        Status.Text = $"Original : {_src.Bitmap.PixelWidth} × {_src.Bitmap.PixelHeight} px · {ImageIO.Size(_src.Bytes)}";
    }

    private static void Set(Line l, double x1, double y1, double x2, double y2, double k)
    {
        l.X1 = x1; l.Y1 = y1; l.X2 = x2; l.Y2 = y2; l.StrokeThickness = k;
    }

    private Point[] HandlePoints()
    {
        var r = _crop;
        double mx = r.X + r.Width / 2, my = r.Y + r.Height / 2;
        return [new(mx, r.Top), new(mx, r.Bottom), new(r.Right, my), new(r.Left, my), new(r.Right, r.Top), new(r.Left, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom)];
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_surface.Overlay);
        double tol = 14 / Math.Max(0.01, _surface.DisplayScale);
        var pts = HandlePoints();
        Drag[] kinds = [Drag.N, Drag.S, Drag.E, Drag.W, Drag.NE, Drag.NW, Drag.SE, Drag.SW];
        _drag = Drag.None;
        for (int i = 0; i < 8; i++) if ((pts[i] - p).Length <= tol) { _drag = kinds[i]; break; }
        if (_drag == Drag.None) _drag = _crop.Contains(p) ? Drag.Move : Drag.New;
        _dragStart = p;
        _cropStart = _crop;
        _surface.Overlay.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_drag == Drag.None || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_surface.Overlay);
        double W = _work.PixelWidth, H = _work.PixelHeight;
        p = new Point(Math.Clamp(p.X, 0, W), Math.Clamp(p.Y, 0, H));
        var d = p - _dragStart;
        var r = _cropStart;
        double? ratio = _ratio ?? (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? r.Width / r.Height : null);
        switch (_drag)
        {
            case Drag.Move:
                _crop = new Rect(r.X + d.X, r.Y + d.Y, r.Width, r.Height);
                Clamp();
                Redraw();
                return;
            case Drag.New:
                _crop = new Rect(_dragStart, p);
                if (ratio is { } nr && _crop.Width > 0 && _crop.Height > 0)
                {
                    double w = _crop.Width, h = _crop.Height;
                    if (w / h > nr) w = h * nr; else h = w / nr;
                    _crop = new Rect(p.X < _dragStart.X ? _dragStart.X - w : _dragStart.X, p.Y < _dragStart.Y ? _dragStart.Y - h : _dragStart.Y, w, h);
                }
                break;
            default:
            {
                double l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
                if (_drag is Drag.W or Drag.NW or Drag.SW) l = Math.Min(p.X, rr - 4);
                if (_drag is Drag.E or Drag.NE or Drag.SE) rr = Math.Max(p.X, l + 4);
                if (_drag is Drag.N or Drag.NE or Drag.NW) t = Math.Min(p.Y, b - 4);
                if (_drag is Drag.S or Drag.SE or Drag.SW) b = Math.Max(p.Y, t + 4);
                var nrect = new Rect(new Point(l, t), new Point(rr, b));
                if (ratio is { } rt)
                {
                    double w = nrect.Width, h = nrect.Height;
                    if (_drag is Drag.N or Drag.S) w = h * rt;
                    else if (_drag is Drag.E or Drag.W) h = w / rt;
                    else if (w / h > rt) w = h * rt; else h = w / rt;
                    double x = _drag is Drag.W or Drag.NW or Drag.SW ? nrect.Right - w : nrect.Left;
                    double y = _drag is Drag.N or Drag.NE or Drag.NW ? nrect.Bottom - h : nrect.Top;
                    nrect = new Rect(x, y, w, h);
                }
                _crop = nrect;
                break;
            }
        }
        if (_crop.Width >= 1 && _crop.Height >= 1)
        {
            _crop.Intersect(new Rect(0, 0, W, H));
            if (_crop.IsEmpty) _crop = _cropStart;
            Redraw();
        }
    }

    private void Save()
    {
        var r = new Int32Rect((int)Math.Round(_crop.X), (int)Math.Round(_crop.Y), (int)Math.Round(_crop.Width), (int)Math.Round(_crop.Height));
        r.Width = Math.Min(r.Width, _work.PixelWidth - r.X);
        r.Height = Math.Min(r.Height, _work.PixelHeight - r.Y);
        if (r.Width < 1 || r.Height < 1) { Status.Text = "Cadre vide."; return; }
        var cropped = new CroppedBitmap(_work, r);
        var ext = ImageIO.OutputExtension(_src.Path);
        var data = ImageIO.Encode(cropped, ext, 95);
        Done(ImageIO.SaveNew(data, ImageIO.OutputPath(_src.Path, "recadrée", ext)));
    }
}
