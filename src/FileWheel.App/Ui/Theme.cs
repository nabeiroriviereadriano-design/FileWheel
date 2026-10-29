using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using FileWheel.Windows.SystemIntegration;

namespace FileWheel.App.Ui;

/// <summary>
/// Charte graphique « hybride » Windows 11 + macOS.
///
/// Cote Windows : couleur d'accent du systeme, icones Segoe Fluent, police Segoe UI Variable,
///   boutons de fenetre natifs, barre de titre claire/sombre, champs de saisie et listes du theme Fluent,
///   indicateur d'accent (petite pilule) dans la barre laterale.
/// Cote macOS : palette neutre (gris « systeme »), barre laterale teintee facon reglages Systeme,
///   grands titres, coins plus arrondis, cartes groupees, interrupteurs a bascule, controles segmentes
///   en pilule, boutons « push » a degrade, boutons principaux a droite des boites de dialogue,
///   roue en verre depoli avec ombre douce.
///
/// Toutes les couleurs passent par des ressources dynamiques : un changement clair/sombre ou de couleur
/// d'accent dans Windows est applique en direct, sans redemarrer.
/// Aucun effet bitmap (DropShadowEffect) n'est pose sur un element contenant du texte : il desactiverait
/// le lissage ClearType et rendrait le texte flou.
/// </summary>
public static class Theme
{
    // ------------------------------------------------------------------ cles de ressources
    public const string WindowBg = "fw.WindowBg";
    public const string SidebarBg = "fw.SidebarBg";
    public const string SidebarSelected = "fw.SidebarSelected";
    public const string Surface = "fw.Surface";
    public const string SurfaceBorder = "fw.SurfaceBorder";
    public const string SurfaceShadow = "fw.SurfaceShadow";
    public const string FooterBg = "fw.FooterBg";
    public const string Separator = "fw.Separator";
    public const string TextPrimary = "fw.Text";
    public const string TextSecondary = "fw.Text2";
    public const string Accent = "fw.Accent";
    public const string AccentTop = "fw.AccentTop";
    public const string AccentHover = "fw.AccentHover";
    public const string AccentPressed = "fw.AccentPressed";
    public const string AccentBorder = "fw.AccentBorder";
    public const string AccentText = "fw.AccentText";
    public const string AccentSoft = "fw.AccentSoft";
    public const string FocusRing = "fw.FocusRing";
    public const string ControlTop = "fw.ControlTop";
    public const string ControlBg = "fw.ControlBg";
    public const string ControlHover = "fw.ControlHover";
    public const string ControlPressed = "fw.ControlPressed";
    public const string ControlBorder = "fw.ControlBorder";
    public const string ControlBorderBottom = "fw.ControlBorderBottom";
    public const string SwitchOff = "fw.SwitchOff";
    public const string SwitchThumb = "fw.SwitchThumb";
    public const string SegmentTrack = "fw.SegmentTrack";
    public const string SegmentSelected = "fw.SegmentSelected";
    public const string SubtleHover = "fw.SubtleHover";
    public const string DangerKey = "fw.Danger";

    // ------------------------------------------------------------------ styles
    public const string AccentButtonStyle = "fw.AccentButton";
    public const string DangerButtonStyle = "fw.DangerButton";
    public const string SubtleButtonStyle = "fw.SubtleButton";
    public const string SegmentStyle = "fw.Segment";
    public const string SidebarItemStyle = "fw.SidebarItem";

    // Rayons : entre Windows 11 (4/8) et macOS (6/10-12)
    public static readonly CornerRadius ControlRadius = new(7);
    public static readonly CornerRadius CardRadius = new(11);

    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    public static bool IsDark { get; private set; }
    public static Color AccentColor { get; private set; } = Color.FromRgb(0, 103, 192);

    private static bool _installed;

    /// <summary>Installe palette et styles au niveau de l'application. A appeler une fois, avant d'ouvrir des fenetres.</summary>
    public static void Install(Application app)
    {
        if (_installed) return;
        _installed = true;
        ApplyPalette(app.Resources);
        var r = app.Resources;
        r[typeof(Button)] = NeutralButton();
        r[AccentButtonStyle] = FilledButton(Accent, AccentTop, AccentHover, AccentPressed, AccentBorder, AccentText);
        r[DangerButtonStyle] = FilledButton(DangerKey, DangerKey, DangerKey, DangerKey, DangerKey, AccentText);
        r[SubtleButtonStyle] = SubtleButton();
        r[typeof(CheckBox)] = SwitchStyle();
        r[SegmentStyle] = SegmentButton();
        r[SidebarItemStyle] = SidebarItem();

        // Fenetre cachee qui recoit les diffusions de Windows : changement clair/sombre (WM_SETTINGCHANGE
        // « ImmersiveColorSet ») ou de couleur d'accent (WM_DWMCOLORIZATIONCOLORCHANGED).
        try
        {
            _listener = new HwndSource(new HwndSourceParameters("FileWheel.Theme") { Width = 0, Height = 0, WindowStyle = 0 });
            _listener.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                const int WM_SETTINGCHANGE = 0x001A, WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320, WM_THEMECHANGED = 0x031A;
                if (msg is WM_SETTINGCHANGE or WM_DWMCOLORIZATIONCOLORCHANGED or WM_THEMECHANGED)
                    app.Dispatcher.BeginInvoke(() => Refresh(app));
                return IntPtr.Zero;
            });
        }
        catch { /* sans ecoute : la palette reste celle du demarrage */ }
    }

    private static HwndSource? _listener;

    /// <summary>Relit le mode clair/sombre et l'accent de Windows et met a jour toutes les fenetres ouvertes.</summary>
    /// <summary>Un reglage d'apparence de Windows a change (theme des applis, de la barre des taches, accent).</summary>
    public static event Action? SystemAppearanceChanged;

    public static void Refresh(Application app)
    {
        try { SystemAppearanceChanged?.Invoke(); } catch { }
        bool wasDark = IsDark;
        var oldAccent = AccentColor;
        ApplyPalette(app.Resources);
        if (wasDark == IsDark && oldAccent == AccentColor) return;
        foreach (Window w in app.Windows)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero) WindowsTheme.StyleWindow(h, IsDark, mica: false);
            w.InvalidateVisual();
        }
    }

    private static void ApplyPalette(ResourceDictionary r)
    {
        IsDark = WindowsTheme.AppsUseDarkTheme();
        uint a = WindowsTheme.AccentColor();
        var accent = Color.FromRgb((byte)(a >> 16), (byte)(a >> 8), (byte)a);
        // En sombre, Windows eclaircit l'accent pour garder le contraste ; on fait de meme.
        if (IsDark) accent = Mix(accent, Colors.White, 0.22);
        AccentColor = accent;
        bool d = IsDark;

        Set(r, WindowBg, d ? Rgb(0x1F, 0x1F, 0x21) : Rgb(0xF5, 0xF5, 0xF7));
        Set(r, SidebarBg, d ? Rgb(0x29, 0x29, 0x2C) : Rgb(0xEA, 0xEA, 0xEE));
        Set(r, SidebarSelected, d ? Argb(0x2E, 0xFF, 0xFF, 0xFF) : Argb(0x12, 0x00, 0x00, 0x00));
        Set(r, Surface, d ? Rgb(0x2B, 0x2B, 0x2E) : Rgb(0xFF, 0xFF, 0xFF));
        Set(r, SurfaceBorder, d ? Argb(0x1C, 0xFF, 0xFF, 0xFF) : Argb(0x14, 0x00, 0x00, 0x00));
        Set(r, SurfaceShadow, d ? Argb(0x50, 0x00, 0x00, 0x00) : Argb(0x0E, 0x00, 0x00, 0x00));
        Set(r, FooterBg, d ? Rgb(0x26, 0x26, 0x29) : Rgb(0xEC, 0xEC, 0xF0));
        Set(r, Separator, d ? Argb(0x1F, 0xFF, 0xFF, 0xFF) : Argb(0x17, 0x00, 0x00, 0x00));
        Set(r, TextPrimary, d ? Rgb(0xF5, 0xF5, 0xF7) : Rgb(0x1D, 0x1D, 0x1F));
        Set(r, TextSecondary, d ? Rgb(0x98, 0x98, 0x9F) : Rgb(0x6E, 0x6E, 0x73));

        Set(r, Accent, accent);
        Set(r, AccentTop, Mix(accent, Colors.White, 0.14));
        Set(r, AccentHover, Mix(accent, Colors.White, 0.10));
        Set(r, AccentPressed, Mix(accent, Colors.Black, 0.15));
        Set(r, AccentBorder, Mix(accent, Colors.Black, 0.18));
        Set(r, AccentText, Luminance(accent) > 0.6 ? Rgb(0x10, 0x10, 0x10) : Colors.White);
        Set(r, AccentSoft, Color.FromArgb((byte)(d ? 0x40 : 0x26), accent.R, accent.G, accent.B));
        Set(r, FocusRing, Color.FromArgb(0x80, accent.R, accent.G, accent.B));

        Set(r, ControlTop, d ? Rgb(0x48, 0x48, 0x4C) : Rgb(0xFF, 0xFF, 0xFF));
        Set(r, ControlBg, d ? Rgb(0x3A, 0x3A, 0x3D) : Rgb(0xFB, 0xFB, 0xFD));
        Set(r, ControlHover, d ? Rgb(0x45, 0x45, 0x49) : Rgb(0xF2, 0xF2, 0xF5));
        Set(r, ControlPressed, d ? Rgb(0x52, 0x52, 0x57) : Rgb(0xE6, 0xE6, 0xEB));
        Set(r, ControlBorder, d ? Argb(0x24, 0xFF, 0xFF, 0xFF) : Argb(0x22, 0x00, 0x00, 0x00));
        Set(r, ControlBorderBottom, d ? Argb(0x14, 0xFF, 0xFF, 0xFF) : Argb(0x38, 0x00, 0x00, 0x00));
        Set(r, SwitchOff, d ? Rgb(0x48, 0x48, 0x4D) : Rgb(0xE1, 0xE1, 0xE6));
        Set(r, SwitchThumb, Colors.White);
        Set(r, SegmentTrack, d ? Rgb(0x34, 0x34, 0x38) : Rgb(0xE6, 0xE6, 0xEB));
        Set(r, SegmentSelected, d ? Rgb(0x63, 0x63, 0x68) : Rgb(0xFF, 0xFF, 0xFF));
        Set(r, SubtleHover, d ? Argb(0x1A, 0xFF, 0xFF, 0xFF) : Argb(0x0F, 0x00, 0x00, 0x00));
        Set(r, DangerKey, d ? Rgb(0xFF, 0x45, 0x3A) : Rgb(0xD7, 0x2C, 0x1F));
    }

    private static void Set(ResourceDictionary r, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    // ------------------------------------------------------------------ boutons

    /// <summary>Bouton « push » : fond clair legerement degrade, liseré inferieur plus marque (macOS), coins 7 px.</summary>
    private static Style NeutralButton()
    {
        var s = new Style(typeof(Button));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 5, 14, 6)));
        s.Setters.Add(new Setter(Control.MinHeightProperty, 30.0));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Arrow));
        s.Setters.Add(DynSetter(Control.ForegroundProperty, TextPrimary));
        s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(ControlBg, ControlTop, ControlBorder, ControlBorderBottom, ControlHover, ControlPressed)));
        return s;
    }

    private static Style FilledButton(string bg, string top, string hover, string pressed, string border, string fg)
    {
        var s = new Style(typeof(Button), (Style)NeutralButton());
        s.Setters.Add(DynSetter(Control.ForegroundProperty, fg));
        s.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(bg, top, border, border, hover, pressed)));
        return s;
    }

    private static ControlTemplate ButtonTemplate(string bg, string top, string border, string borderBottom, string hover, string pressed)
    {
        var t = new ControlTemplate(typeof(Button));
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        // Anneau de focus clavier (macOS) dessine autour du bouton
        var ring = new FrameworkElementFactory(typeof(Border), "ring");
        ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius.TopLeft + 3));
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(3));
        ring.SetResourceReference(Border.BorderBrushProperty, FocusRing);
        ring.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
        root.AppendChild(ring);

        // Liseré inferieur plus fonce (relief discret des boutons macOS)
        var under = new FrameworkElementFactory(typeof(Border), "under");
        under.SetValue(Border.CornerRadiusProperty, ControlRadius);
        under.SetResourceReference(Border.BackgroundProperty, borderBottom);
        root.AppendChild(under);

        var bd = new FrameworkElementFactory(typeof(Border), "bd");
        bd.SetValue(Border.CornerRadiusProperty, ControlRadius);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bd.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 1));
        bd.SetResourceReference(Border.BorderBrushProperty, border);
        bd.SetResourceReference(Border.BackgroundProperty, bg);
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        root.AppendChild(bd);

        // Reflet superieur (degrade tres leger, comme les boutons macOS ; plat facon Windows au survol)
        var sheen = new FrameworkElementFactory(typeof(Border), "sheen");
        sheen.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius.TopLeft, ControlRadius.TopRight, 0, 0));
        sheen.SetValue(FrameworkElement.MarginProperty, new Thickness(1, 1, 1, 0));
        sheen.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        sheen.SetValue(FrameworkElement.HeightProperty, 12.0);
        sheen.SetResourceReference(Border.BackgroundProperty, top);
        sheen.SetValue(UIElement.OpacityProperty, 0.55);
        sheen.SetValue(UIElement.IsHitTestVisibleProperty, false);
        root.AppendChild(sheen);

        var cp = new FrameworkElementFactory(typeof(ContentPresenter), "cp");
        cp.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        root.AppendChild(cp);
        t.VisualTree = root;

        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(DynSetter(Border.BackgroundProperty, hover, "bd"));
        over.Setters.Add(new Setter(UIElement.OpacityProperty, 0.0, "sheen"));
        t.Triggers.Add(over);
        var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        press.Setters.Add(DynSetter(Border.BackgroundProperty, pressed, "bd"));
        press.Setters.Add(new Setter(UIElement.OpacityProperty, 0.0, "sheen"));
        t.Triggers.Add(press);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "ring"));
        t.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        t.Triggers.Add(disabled);
        return t;
    }

    /// <summary>Bouton sans cadre (barres d'outils, icones) : fond teinte au survol, facon Windows 11.</summary>
    private static Style SubtleButton()
    {
        var s = new Style(typeof(Button));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        s.Setters.Add(DynSetter(Control.ForegroundProperty, TextPrimary));
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border), "bd");
        bd.SetValue(Border.CornerRadiusProperty, ControlRadius);
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(DynSetter(Border.BackgroundProperty, SubtleHover, "bd"));
        t.Triggers.Add(over);
        var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        press.Setters.Add(DynSetter(Border.BackgroundProperty, SidebarSelected, "bd"));
        t.Triggers.Add(press);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(DynSetter(Border.BorderBrushProperty, FocusRing, "bd"));
        t.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        t.Triggers.Add(disabled);
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        return s;
    }

    // ------------------------------------------------------------------ interrupteur (CheckBox)

    /// <summary>
    /// Case a cocher dessinee en interrupteur a bascule (macOS / iOS, aussi present dans Windows 11) :
    /// libelle a gauche, interrupteur aligne a droite.
    /// </summary>
    private static Style SwitchStyle()
    {
        var s = new Style(typeof(CheckBox));
        s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        s.Setters.Add(DynSetter(Control.ForegroundProperty, TextPrimary));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        s.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));

        var t = new ControlTemplate(typeof(CheckBox));
        // DockPanel : interrupteur a droite, libelle sur le reste de la ligne (toute la ligne est cliquable)
        var grid = new FrameworkElementFactory(typeof(DockPanel));
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        grid.SetValue(DockPanel.LastChildFillProperty, true);

        var track = new FrameworkElementFactory(typeof(Border), "track");
        track.SetValue(DockPanel.DockProperty, Dock.Right);
        track.SetValue(FrameworkElement.WidthProperty, 40.0);
        track.SetValue(FrameworkElement.HeightProperty, 22.0);
        track.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        track.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        track.SetResourceReference(Border.BackgroundProperty, SwitchOff);
        track.SetResourceReference(Border.BorderBrushProperty, ControlBorder);

        var thumb = new FrameworkElementFactory(typeof(Ellipse), "thumb");
        thumb.SetValue(FrameworkElement.WidthProperty, 18.0);
        thumb.SetValue(FrameworkElement.HeightProperty, 18.0);
        thumb.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        thumb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        thumb.SetResourceReference(Shape.FillProperty, SwitchThumb);
        thumb.SetResourceReference(Shape.StrokeProperty, ControlBorder);
        thumb.SetValue(Shape.StrokeThicknessProperty, 0.5);
        track.AppendChild(thumb);
        grid.AppendChild(track);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 16, 0));
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        grid.AppendChild(cp);
        t.VisualTree = grid;

        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(DynSetter(Border.BackgroundProperty, Accent, "track"));
        on.Setters.Add(DynSetter(Border.BorderBrushProperty, AccentBorder, "track"));
        on.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "thumb"));
        on.Setters.Add(new Setter(Shape.StrokeProperty, Brushes.Transparent, "thumb"));
        t.Triggers.Add(on);
        var mixed = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = null };
        mixed.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center, "thumb"));
        t.Triggers.Add(mixed);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "track"));
        focus.Setters.Add(DynSetter(Border.BorderBrushProperty, FocusRing, "track"));
        t.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        t.Triggers.Add(disabled);
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        return s;
    }

    // ------------------------------------------------------------------ controle segmente (RadioButton)

    private static Style SegmentButton()
    {
        var s = new Style(typeof(RadioButton));
        s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        s.Setters.Add(DynSetter(Control.ForegroundProperty, TextPrimary));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 4, 14, 5)));
        var t = new ControlTemplate(typeof(RadioButton));
        var bd = new FrameworkElementFactory(typeof(Border), "bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(0.5));
        bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(DynSetter(Border.BackgroundProperty, SubtleHover, "bd"));
        t.Triggers.Add(over);
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(DynSetter(Border.BackgroundProperty, SegmentSelected, "bd"));
        on.Setters.Add(DynSetter(Border.BorderBrushProperty, ControlBorder, "bd"));
        on.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        t.Triggers.Add(on);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(DynSetter(Border.BorderBrushProperty, Accent, "bd"));
        t.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        t.Triggers.Add(disabled);
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        return s;
    }

    // ------------------------------------------------------------------ barre laterale

    /// <summary>Element de barre laterale : fond teinte arrondi (macOS) + pilule d'accent a gauche (Windows 11).</summary>
    private static Style SidebarItem()
    {
        var s = new Style(typeof(ListBoxItem));
        s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        s.Setters.Add(DynSetter(Control.ForegroundProperty, TextPrimary));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 10, 7)));
        s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
        var t = new ControlTemplate(typeof(ListBoxItem));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var bd = new FrameworkElementFactory(typeof(Border), "bd");
        bd.SetValue(Border.CornerRadiusProperty, ControlRadius);
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        grid.AppendChild(bd);
        var pill = new FrameworkElementFactory(typeof(Border), "pill");
        pill.SetValue(FrameworkElement.WidthProperty, 3.0);
        pill.SetValue(FrameworkElement.HeightProperty, 16.0);
        pill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        pill.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        pill.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.5));
        pill.SetResourceReference(Border.BackgroundProperty, Accent);
        pill.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
        grid.AppendChild(pill);
        t.VisualTree = grid;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(DynSetter(Border.BackgroundProperty, SubtleHover, "bd"));
        t.Triggers.Add(over);
        var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        sel.Setters.Add(DynSetter(Border.BackgroundProperty, SidebarSelected, "bd"));
        sel.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "pill"));
        sel.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        t.Triggers.Add(sel);
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        return s;
    }

    private static Setter DynSetter(DependencyProperty p, string key, string? target = null) =>
        target is null ? new Setter(p, new DynamicResourceExtension(key)) : new Setter(p, new DynamicResourceExtension(key), target);
}
