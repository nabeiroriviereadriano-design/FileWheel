using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FileWheel.Core.Input;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;
using FileWheel.Core.Wheel;
using FileWheel.Windows.Display;
using FileWheel.Windows.SystemIntegration;

namespace FileWheel.App.Ui;

/// <summary>
/// La roue : fenetre sans bordure, transparente, toujours au premier plan, qui ne prend jamais le focus.
///
/// Pendant le glisser natif de l'Explorateur, la roue est une CIBLE DE DEPOT OLE ordinaire :
/// - les fichiers sont lus dans les donnees du glisser (CF_HDROP) des que le curseur entre dans la roue ;
/// - deposer sur un secteur declenche l'action ; la roue repond « Copier » (jamais « Deplacer ») pour que
///   l'Explorateur source ne touche pas aux fichiers ;
/// - deposer ailleurs que sur la roue laisse Windows faire son depot habituel.
/// Apres un depot sur une branche (sous-menu), la roue reste ouverte et se pilote au clic.
/// </summary>
public sealed class WheelWindow : Window
{
    private readonly WheelView _view;
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private WheelInteraction? _interaction;
    private IReadOnlyList<string> _files = [];
    private bool _dropDone;
    private bool _pointerInside;
    private DateTime _lastActivity;
    private int _physicalDiameter;
    private PixelRect _rect;

    public Func<IReadOnlyList<string>, WheelItem>? MenuFactory { get; set; }
    public Func<AppSettings>? Settings { get; set; }

    /// <summary>Action choisie (commande + fichiers).</summary>
    public event Action<WheelItem, IReadOnlyList<string>>? ActionChosen;
    /// <summary>La roue a disparu (pour prevenir les crochets).</summary>
    public event Action? Dismissed;

    public bool IsOpen { get; private set; }
    public PixelRect ScreenRect => _rect;

    public WheelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Focusable = false;
        Title = "FileWheel";
        SizeToContent = SizeToContent.Manual;
        _view = new WheelView(this);
        Content = _view;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => OnTimer();

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            WindowStyles.MakeOverlay(h);
        };
        DpiChanged += (_, _) => { if (IsOpen) Reposition(); };

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => { _pointerInside = false; _interaction?.OnPointer(WheelLayout.Outside, Now); _view.InvalidateVisual(); };
        Drop += OnDrop;
        MouseMove += (_, e) => { if (_interaction?.Mode == WheelMode.Click) Pointer(e.GetPosition(_view)); };
        MouseLeftButtonUp += (_, e) => { if (_interaction?.Mode == WheelMode.Click) Release(e.GetPosition(_view)); };
        MouseRightButtonUp += (_, _) => Dismiss("clic droit");
        MouseLeave += (_, _) => { if (_interaction?.Mode == WheelMode.Click) { _interaction.OnPointer(WheelLayout.Outside, Now); _view.InvalidateVisual(); } };
    }

    private long Now => _clock.ElapsedMilliseconds;
    internal long NowMs => Now;
    internal WheelInteraction? Interaction => _interaction;
    internal int FileCount => _files.Count;
    internal string SummaryTitle { get; private set; } = "FileWheel";
    internal string SummarySubtitle { get; private set; } = "";

    /// <summary>Resume affiche au centre au repos : « 1,2 Mo » + nom, ou « 3 éléments » + taille totale.</summary>
    private void ComputeSummary()
    {
        if (_files.Count == 0) { SummaryTitle = "FileWheel"; SummarySubtitle = ""; return; }
        long total = 0; bool anyDir = false;
        foreach (var f in _files)
        {
            try { if (System.IO.File.Exists(f)) total += new System.IO.FileInfo(f).Length; else anyDir = true; }
            catch { }
        }
        if (_files.Count == 1)
        {
            SummaryTitle = anyDir ? "Dossier" : Ui.Size(total);
            SummarySubtitle = System.IO.Path.GetFileName(_files[0].TrimEnd('\\'));
        }
        else
        {
            SummaryTitle = $"{_files.Count} éléments";
            SummarySubtitle = total > 0 ? Ui.Size(total) + (anyDir ? " + dossiers" : "") : "";
        }
    }
    internal AppSettings Cfg => Settings?.Invoke() ?? new AppSettings();

    // ------------------------------------------------------------------ affichage

    /// <summary>Ouvre la roue centree sur un point de l'ecran (pixels physiques).</summary>
    public void ShowAt(int x, int y)
    {
        var cfg = Cfg;
        _files = [];
        _dropDone = false;
        _pointerInside = true;
        _lastActivity = DateTime.UtcNow;
        _interaction = new WheelInteraction(Placeholder("Amenez les fichiers ici"), cfg.Wheel.SubmenuDwellMs);
        Width = Height = cfg.Wheel.DiameterDip;
        _view.Diameter = cfg.Wheel.DiameterDip;
        var monitors = Monitors.All();
        _rect = ScreenGeometry.PlaceWheel(monitors, x, y, cfg.Wheel.DiameterDip, cfg.Wheel.OffsetXDip, cfg.Wheel.OffsetYDip, out _);
        _physicalDiameter = _rect.Width;
        if (!IsVisible) Show();
        Reposition();
        var h = new WindowInteropHelper(this).Handle;
        if (cfg.Wheel.Blur && WindowsTheme.TransparencyEnabled())
        {
            WindowStyles.ClipToCircle(h, _physicalDiameter);
            WindowsTheme.EnableBlur(h, Ui.Dark ? 0x40202020u : 0x40F3F3F3u);
        }
        IsOpen = true;
        _timer.Start();
        Animate(cfg.Wheel.Animations);
        _view.InvalidateVisual();
        Log.Debug($"Roue ouverte en {_rect}");
    }

    private void Reposition()
    {
        var h = new WindowInteropHelper(this).Handle;
        WindowStyles.PlaceTopmost(h, _rect.Left, _rect.Top, _rect.Width, _rect.Height);
    }

    private void Animate(bool enabled)
    {
        if (!enabled) { _view.Opacity = 1; _view.RenderTransform = Transform.Identity; return; }
        var scale = new ScaleTransform(0.88, 0.88);
        _view.RenderTransformOrigin = new Point(0.5, 0.5);
        _view.RenderTransform = scale;
        var dur = new Duration(TimeSpan.FromMilliseconds(110));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.88, 1, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, 1, dur) { EasingFunction = ease });
        _view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
    }

    /// <summary>
    /// Affiche la roue sans glisser en cours (menu Depannage) : verifie le rendu, la position et la taille.
    /// Elle se ferme par Echap, un clic a l'exterieur ou au centre.
    /// </summary>
    public void ShowTest(int x, int y)
    {
        ShowAt(x, y);
        _interaction = new WheelInteraction(Placeholder("Test réussi · Échap pour fermer"), Cfg.Wheel.SubmenuDwellMs)
        {
            Mode = WheelMode.Click,
        };
        _view.InvalidateVisual();
    }

    public void Dismiss(string reason)
    {
        if (!IsOpen) return;
        IsOpen = false;
        _timer.Stop();
        Hide();
        _interaction = null;
        _files = [];
        Log.Debug("Roue fermée : " + reason);
        Dismissed?.Invoke();
    }

    /// <summary>
    /// Bouton de la souris relache (signale par les crochets). Si le curseur etait sur la roue, le depot
    /// OLE va arriver : on attend. Sinon le depot natif a lieu ailleurs et la roue disparait.
    /// </summary>
    public void OnMouseReleasedGlobally()
    {
        if (!IsOpen || _interaction?.Mode == WheelMode.Click) return;
        if (!_pointerInside) { Dismiss("bouton relâché hors de la roue"); return; }
        var started = DateTime.UtcNow;
        var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        wait.Tick += (_, _) =>
        {
            if (_dropDone || !IsOpen || _interaction?.Mode == WheelMode.Click || (DateTime.UtcNow - started).TotalMilliseconds > 1200)
            {
                wait.Stop();
                if (IsOpen && !_dropDone && _interaction?.Mode != WheelMode.Click) Dismiss("aucun dépôt reçu");
            }
        };
        wait.Start();
    }

    /// <summary>Clic quelque part a l'ecran alors que la roue est en mode clic : en dehors = fermeture.</summary>
    public void OnGlobalMouseDown(int x, int y)
    {
        if (!IsOpen || _interaction?.Mode != WheelMode.Click) return;
        if (!_rect.Contains(x, y)) Dismiss("clic à l'extérieur");
    }

    private void OnTimer()
    {
        if (_interaction is null) return;
        if (_interaction.Mode == WheelMode.Drag && _interaction.OnPointer(_interaction.HoverIndex, Now)) _view.InvalidateVisual();
        if (_interaction.DwellProgress(Now) > 0) _view.InvalidateVisual();
        // Inactivite prolongee en mode clic : fermeture
        if (_interaction.Mode == WheelMode.Click && (DateTime.UtcNow - _lastActivity).TotalSeconds > 45) Dismiss("inactivité");
    }

    // ------------------------------------------------------------------ glisser-deposer OLE

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _pointerInside = true;
        if (_files.Count == 0)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                _files = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                var cfg = Cfg;
                if (_files.Count > 1 && !cfg.Trigger.AllowMultiple || _files.Count == 1 && !cfg.Trigger.AllowSingle)
                {
                    _interaction = new WheelInteraction(Placeholder(_files.Count > 1 ? "Désactivée pour plusieurs fichiers" : "Désactivée pour un seul fichier"), cfg.Wheel.SubmenuDwellMs);
                    _files = [];
                }
                else
                {
                    ComputeSummary();
                    var menu = MenuFactory?.Invoke(_files) ?? Placeholder("Aucune action");
                    _interaction = new WheelInteraction(menu, cfg.Wheel.SubmenuDwellMs);
                }
            }
            else
            {
                _interaction = new WheelInteraction(Placeholder("Éléments non pris en charge"), Cfg.Wheel.SubmenuDwellMs);
            }
        }
        SetEffects(e);
        _view.InvalidateVisual();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        _pointerInside = true;
        _lastActivity = DateTime.UtcNow;
        Pointer(e.GetPosition(_view));
        SetEffects(e);
    }

    private void SetEffects(DragEventArgs e)
    {
        var item = _interaction?.HoverItem;
        bool actionable = _files.Count > 0 && item is { Enabled: true };
        var allowed = (OleDropEffect)(int)e.AllowedEffects;
        var chosen = actionable ? DropSafety.ChooseEffect(allowed) : OleDropEffect.None;
        e.Effects = (DragDropEffects)(int)chosen;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dropDone = true;
        // Effet renvoye a la source : jamais « Deplacer » (sinon l'Explorateur supprimerait les originaux).
        SetEffects(e);
        if (_files.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            Dismiss("dépôt sans fichier exploitable");
            return;
        }
        Release(e.GetPosition(_view));
    }

    // ------------------------------------------------------------------ pointeur

    private void Pointer(Point p)
    {
        if (_interaction is null) return;
        int hit = _view.HitTest(p);
        if (_interaction.OnPointer(hit, Now)) _view.InvalidateVisual();
    }

    private void Release(Point p)
    {
        if (_interaction is null) return;
        _lastActivity = DateTime.UtcNow;
        int hit = _view.HitTest(p);
        var outcome = _interaction.OnRelease(hit, Now);
        switch (outcome)
        {
            case WheelOutcome.Execute:
                var item = _interaction.Selected!;
                var files = _files;
                Dismiss("action « " + item.Label + " »");
                // Laisse le depot OLE se terminer avant d'ouvrir des dialogues
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ActionChosen?.Invoke(item, files));
                break;
            case WheelOutcome.Cancel:
                Dismiss("annulation");
                break;
            case WheelOutcome.StayOpen:
                _view.InvalidateVisual();
                break;
        }
    }

    public void EscapePressed() => Dismiss("Échap");

    private static WheelItem Placeholder(string message)
    {
        var root = new WheelItem { Id = "root", Label = "FileWheel" };
        root.Children.Add(WheelItem.Disabled("wait", message, Glyphs.Info, message));
        return root;
    }
}

/// <summary>Dessin de la roue (rendu immediat, aucun controle WPF par secteur : rapide et net a tout DPI).</summary>
internal sealed class WheelView : FrameworkElement
{
    private readonly WheelWindow _owner;
    public double Diameter { get; set; } = 320;

    public WheelView(WheelWindow owner)
    {
        _owner = owner;
        // Pas d'effet bitmap (le texte du centre resterait flou) : l'ombre douce est dessinee dans OnRender.
        SnapsToDevicePixels = true;
    }

    private double Outer => Diameter / 2 - 6;
    private double Inner => Outer * 0.38;
    private Point Center => new(Diameter / 2, Diameter / 2);

    public int HitTest(Point p)
    {
        var items = _owner.Interaction?.Items;
        if (items is null || items.Count == 0) return WheelLayout.Outside;
        var layout = new WheelLayout(Outer, Inner, items.Count);
        return layout.HitTest(p.X - Center.X, p.Y - Center.Y);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Diameter, Diameter);

    protected override void OnRender(DrawingContext dc)
    {
        var inter = _owner.Interaction;
        if (inter is null) return;
        var cfg = _owner.Cfg;
        bool dark = Ui.Dark;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var c = Center;
        byte alpha = (byte)Math.Round(255 * Math.Clamp(cfg.Wheel.Opacity, 0.55, 1.0) * (cfg.Wheel.Blur ? 0.8 : 1.0));
        // Charte hybride : disque en « verre » facon macOS (degrade vertical, reflet sur le bord superieur,
        // ombre portee douce) ; accent, icones Fluent et secteurs nets facon Windows 11.
        var bg = Vertical(dark ? Color.FromArgb(alpha, 54, 54, 58) : Color.FromArgb(alpha, 255, 255, 255),
                          dark ? Color.FromArgb(alpha, 34, 34, 37) : Color.FromArgb(alpha, 238, 238, 243));
        var innerBg = Vertical(dark ? Color.FromArgb(alpha, 62, 62, 67) : Color.FromArgb(alpha, 246, 246, 249),
                               dark ? Color.FromArgb(alpha, 44, 44, 48) : Color.FromArgb(alpha, 229, 229, 235));
        var border = new Pen(new SolidColorBrush(dark ? Color.FromArgb(110, 0, 0, 0) : Color.FromArgb(38, 0, 0, 0)), 1);
        var rim = new Pen(Vertical(Color.FromArgb((byte)(dark ? 70 : 255), 255, 255, 255), Color.FromArgb(0, 255, 255, 255)), 1.2);
        var sep = new Pen(new SolidColorBrush(dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0)), 1);
        var fg = dark ? Color.FromRgb(245, 245, 247) : Color.FromRgb(29, 29, 31);
        var accentBrush = (SolidColorBrush)Ui.Accent;
        var accent = accentBrush.Color;

        // Ombre portee douce (degrade radial), legerement decalee vers le bas
        var shadow = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        double sr = Outer + 6;
        shadow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(dark ? 120 : 70), 0, 0, 0), (Outer - 10) / sr));
        shadow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        dc.DrawEllipse(shadow, null, new Point(c.X, c.Y + 2.5), sr, sr);

        // Disque de fond plus transparent que les secteurs : les interstices laissent deviner le bureau (effet verre).
        var discBg = Vertical(dark ? Color.FromArgb((byte)(alpha * 0.55), 40, 40, 44) : Color.FromArgb((byte)(alpha * 0.5), 250, 250, 252),
                              dark ? Color.FromArgb((byte)(alpha * 0.55), 26, 26, 29) : Color.FromArgb((byte)(alpha * 0.5), 232, 232, 238));
        dc.DrawEllipse(discBg, border, c, Outer, Outer);

        var items = inter.Items;
        int n = Math.Max(1, items.Count);
        var layout = new WheelLayout(Outer, Inner, n);
        bool captions = cfg.Wheel.ShowCaptions;
        double gapDeg = n > 1 ? Math.Min(2.2, layout.SweepDegrees * 0.08) : 0;
        double midR = (Outer + Inner) / 2;
        double maxTextW = n > 1 ? 2 * midR * Math.Sin(layout.SweepDegrees / 2 * Math.PI / 180) * 0.78 : Outer;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            bool hover = inter.HoverIndex == i;
            // Tuile du secteur (verre plus dense) ou, au survol, tuile d'accent
            var tile = Sector(c, layout.StartAngle(i) + gapDeg / 2, layout.SweepDegrees - gapDeg, Outer - 3, Inner + 4);
            Brush tileFill;
            if (hover)
            {
                var baseColor = it.Destructive ? Color.FromRgb(215, 44, 31) : accent;
                tileFill = it.Enabled
                    ? Vertical(Theme.Mix(baseColor, Colors.White, 0.18), baseColor)
                    : new SolidColorBrush(dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
            }
            else tileFill = bg;
            dc.DrawGeometry(tileFill, n > 1 ? sep : null, tile);

            var color = hover && it.Enabled ? (Theme.Luminance(accent) > 0.6 && !it.Destructive ? Color.FromRgb(16, 16, 16) : Colors.White)
                : it.Destructive ? (dark ? Color.FromRgb(255, 105, 97) : Color.FromRgb(215, 44, 31)) : fg;
            if (!it.Enabled) color = Color.FromArgb(90, color.R, color.G, color.B);
            var brush = new SolidColorBrush(color);
            var (ix, iy) = layout.IconPosition(i, 0.5);
            var at = new Point(c.X + ix, c.Y + iy);
            string caption = WheelMenuBuilder.CaptionOf(it);

            if (it.TextFace)
            {
                // Format cible ecrit en gros (DOCX, JPG...) : on lit la destination d'un coup d'oeil.
                double faceSize = Math.Clamp(Diameter / 16.5, 13, 26);
                var t = Text(caption, faceSize, FontWeights.Bold, brush, pixelsPerDip);
                if (t.Width > maxTextW) t.SetFontSize(faceSize * maxTextW / t.Width);
                dc.DrawText(t, new Point(at.X - t.Width / 2, at.Y - t.Height / 2));
            }
            else
            {
                double iconSize = Math.Clamp(Diameter / (captions ? 17 : 15.5), 15, 30) * (n > 10 ? 0.85 : 1);
                var glyph = new FormattedText(it.Glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(Ui.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), iconSize, brush, pixelsPerDip);
                if (captions)
                {
                    double capSize = Math.Clamp(Diameter / 33, 8.5, 12);
                    var cap = Text(caption, capSize, FontWeights.SemiBold, brush, pixelsPerDip);
                    if (cap.Width > maxTextW) cap.SetFontSize(capSize * maxTextW / cap.Width);
                    double block = glyph.Height * 0.82 + cap.Height;
                    double top = at.Y - block / 2;
                    dc.DrawText(glyph, new Point(at.X - glyph.Width / 2, top - glyph.Height * 0.09));
                    dc.DrawText(cap, new Point(at.X - cap.Width / 2, top + glyph.Height * 0.82));
                }
                else dc.DrawText(glyph, new Point(at.X - glyph.Width / 2, at.Y - glyph.Height / 2));
            }
            // Indicateur de sous-menu
            if (it.IsBranch)
            {
                var (bx, by) = layout.IconPosition(i, 0.965); // au bord exterieur : ne chevauche jamais le libelle
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(it.Enabled ? 160 : 60), color.R, color.G, color.B)), null, new Point(c.X + bx, c.Y + by), 2, 2);
            }
        }
        dc.DrawEllipse(null, rim, c, Outer - 1.2, Outer - 1.2);

        // Progression du survol (ouverture de sous-menu)
        double dwell = inter.DwellProgress(_owner.NowMs);
        if (dwell > 0 && inter.HoverIndex >= 0)
        {
            var pen = new Pen(new SolidColorBrush(Colors.White), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(c, Outer - 6, layout.StartAngle(inter.HoverIndex) + 3, (layout.SweepDegrees - 6) * dwell));
        }
        else if (dwell > 0 && inter.HoverIndex == WheelLayout.Center)
        {
            var pen = new Pen(accentBrush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(c, Inner - 4, -90, 360 * dwell));
        }

        // Centre : libelle de l'element survole, message, ou « Annuler / Retour »
        bool centerHover = inter.HoverIndex == WheelLayout.Center;
        dc.DrawEllipse(centerHover ? Vertical(dark ? Color.FromRgb(76, 76, 82) : Color.FromRgb(236, 236, 241), dark ? Color.FromRgb(58, 58, 63) : Color.FromRgb(220, 220, 227)) : innerBg, border, c, Inner, Inner);
        dc.DrawEllipse(null, rim, c, Inner - 1, Inner - 1);
        string title, subtitle = "";
        Color titleColor = fg;
        var hoverItem = inter.HoverItem;
        if (inter.Message is { } msg) { title = "Indisponible"; subtitle = msg; titleColor = Color.FromRgb(230, 150, 40); }
        else if (hoverItem is not null)
        {
            title = hoverItem.Label;
            subtitle = !hoverItem.Enabled ? hoverItem.DisabledReason ?? "" : hoverItem.Tooltip;
            if (!hoverItem.Enabled) titleColor = Color.FromArgb(150, fg.R, fg.G, fg.B);
        }
        else if (inter.InSubmenu) { title = inter.Breadcrumb; subtitle = centerHover ? "Retour" : "Centre = retour"; }
        else if (centerHover) { title = "Annuler"; subtitle = "Lâcher ici ne fait rien"; }
        else if (inter.Items.Count == 1 && !inter.Items[0].Enabled) { title = "Indisponible"; subtitle = inter.Items[0].DisabledReason ?? ""; }
        else
        {
            // Au repos : taille du fichier (comme un apercu) et son nom
            title = _owner.SummaryTitle;
            subtitle = _owner.SummarySubtitle;
        }
        double textWidth = Inner * 1.62;
        var t1 = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Ui.TextFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), Math.Clamp(Diameter / 22, 12, 18), new SolidColorBrush(titleColor), pixelsPerDip)
        { MaxTextWidth = textWidth, TextAlignment = TextAlignment.Center, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis };
        var t2 = new FormattedText(subtitle, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Ui.TextFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), Math.Clamp(Diameter / 27, 11, 14), new SolidColorBrush(Color.FromArgb(190, fg.R, fg.G, fg.B)), pixelsPerDip)
        { MaxTextWidth = textWidth, TextAlignment = TextAlignment.Center, MaxLineCount = 3, Trimming = TextTrimming.WordEllipsis };
        double total = t1.Height + (subtitle.Length > 0 ? t2.Height + 3 : 0);
        double y = c.Y - total / 2;
        dc.DrawText(t1, new Point(c.X - textWidth / 2, y));
        if (subtitle.Length > 0) dc.DrawText(t2, new Point(c.X - textWidth / 2, y + t1.Height + 3));
    }

    private static FormattedText Text(string s, double size, FontWeight weight, Brush brush, double ppd) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Ui.TextFont, FontStyles.Normal, weight, FontStretches.Normal), size, brush, ppd);

    private static LinearGradientBrush Vertical(Color top, Color bottom)
    {
        var b = new LinearGradientBrush(top, bottom, new Point(0.5, 0), new Point(0.5, 1));
        b.Freeze();
        return b;
    }

    private static Geometry Sector(Point c, double startDeg, double sweepDeg, double rOuter, double rInner)
    {
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var p0 = new Point(c.X + rOuter * Math.Cos(a0), c.Y + rOuter * Math.Sin(a0));
        var p1 = new Point(c.X + rOuter * Math.Cos(a1), c.Y + rOuter * Math.Sin(a1));
        var p2 = new Point(c.X + rInner * Math.Cos(a1), c.Y + rInner * Math.Sin(a1));
        var p3 = new Point(c.X + rInner * Math.Cos(a0), c.Y + rInner * Math.Sin(a0));
        bool large = sweepDeg > 180;
        var fig = new PathFigure { StartPoint = p0, IsClosed = true };
        fig.Segments.Add(new ArcSegment(p1, new Size(rOuter, rOuter), 0, large, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(p2, true));
        fig.Segments.Add(new ArcSegment(p3, new Size(rInner, rInner), 0, large, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        sweepDeg = Math.Clamp(sweepDeg, 0.1, 359.9);
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var fig = new PathFigure { StartPoint = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), IsClosed = false };
        fig.Segments.Add(new ArcSegment(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }
}
