using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using FileWheel.Windows.SystemIntegration;

namespace FileWheel.App.Ui;

/// <summary>
/// Petits constructeurs d'interface. Les styles (charte hybride Windows 11 + macOS) sont definis dans
/// <see cref="Theme"/> ; les champs de saisie, listes et barres de defilement gardent le theme Fluent de WPF.
/// Les couleurs sont des references de ressources dynamiques : elles suivent le mode clair/sombre en direct.
/// </summary>
public static class Ui
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    public static bool Dark => WindowsTheme.AppsUseDarkTheme();

    public static Brush Accent => Res(Theme.Accent, Color.FromRgb(0, 103, 192));
    public static Brush Subtle => Res(Theme.TextSecondary, Color.FromRgb(110, 110, 115));
    public static Brush Warning => Frozen(new SolidColorBrush(Dark ? Color.FromRgb(255, 214, 10) : Color.FromRgb(178, 110, 0)));
    public static Brush Error => Frozen(new SolidColorBrush(Dark ? Color.FromRgb(255, 105, 97) : Color.FromRgb(215, 44, 31)));
    public static Brush Success => Frozen(new SolidColorBrush(Dark ? Color.FromRgb(48, 209, 88) : Color.FromRgb(36, 138, 61)));
    public static Brush CardBackground => Res(Theme.Surface, Colors.White);
    public static Brush CardBorder => Res(Theme.SurfaceBorder, Color.FromArgb(20, 0, 0, 0));

    private static Brush Res(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? Frozen(new SolidColorBrush(fallback));

    private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>Grand titre de page (facon macOS), police d'affichage Windows.</summary>
    public static TextBlock Title(string text) => Themed(new TextBlock
    {
        Text = text, FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = Theme.DisplayFont, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap,
    });

    /// <summary>En-tete de groupe : petit, gras, couleur secondaire (comme les sections des reglages macOS / Windows 11).</summary>
    public static TextBlock Heading(string text) => Themed(new TextBlock
    {
        Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 18, 0, 6), TextWrapping = TextWrapping.Wrap,
    }, Theme.TextSecondary);

    public static TextBlock Text(string text, Brush? brush = null, double size = 13)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, Margin = new Thickness(0, 2, 0, 2) };
        if (brush is not null) t.Foreground = brush;
        else t.SetResourceReference(TextBlock.ForegroundProperty, Theme.TextPrimary);
        return t;
    }

    public static TextBlock Hint(string text) => Themed(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 6) }, Theme.TextSecondary);

    private static TextBlock Themed(TextBlock t, string key = Theme.TextPrimary)
    {
        t.SetResourceReference(TextBlock.ForegroundProperty, key);
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, Brush? brush = null)
    {
        var t = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (brush is not null) t.Foreground = brush;
        return t;
    }

    public static Button Button(string text, Action onClick, bool accent = false, string? glyph = null, bool danger = false)
    {
        object content = text;
        if (glyph is not null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Icon(glyph, 14));
            sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            content = sp;
        }
        var b = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0), MinWidth = 88 };
        if (danger)
        {
            if (Application.Current?.TryFindResource(Theme.DangerButtonStyle) is Style ds) b.Style = ds;
        }
        else if (accent)
        {
            if (Application.Current?.TryFindResource(Theme.AccentButtonStyle) is Style s) b.Style = s;
            b.IsDefault = true;
        }
        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    public static StackPanel Column(params UIElement[] children)
    {
        var sp = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    /// <summary>Champ « libelle : controle » sur une grille a deux colonnes.</summary>
    public static Grid Field(string label, UIElement control, string? help = null, double labelWidth = 220)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        if (help is not null) lbl.ToolTip = help;
        g.Children.Add(lbl);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    /// <summary>
    /// Carte groupee (macOS) : fond « surface », coins 11 px, liseré fin, ombre tres douce.
    /// L'ombre est un calque separe derriere la carte : le texte garde son lissage ClearType.
    /// </summary>
    public static FrameworkElement Card(UIElement content, double padding = 16)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12), Tag = "fw.card" };
        var shadow = new Border { CornerRadius = Theme.CardRadius, Margin = new Thickness(0, 1, 0, -1), IsHitTestVisible = false };
        shadow.SetResourceReference(Border.BackgroundProperty, Theme.SurfaceShadow);
        shadow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
        var card = new Border { Child = content, Padding = new Thickness(padding), CornerRadius = Theme.CardRadius, BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, Theme.Surface);
        card.SetResourceReference(Border.BorderBrushProperty, Theme.SurfaceBorder);
        grid.Children.Add(shadow);
        grid.Children.Add(card);
        return grid;
    }

    /// <summary>Fine ligne de separation (entre les lignes d'une carte).</summary>
    public static Border Separator()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6), SnapsToDevicePixels = true };
        b.SetResourceReference(Border.BackgroundProperty, Theme.Separator);
        return b;
    }

    /// <summary>
    /// Controle segmente (pilule macOS) pour 2 a 5 choix courts ; remplace avantageusement une liste deroulante.
    /// </summary>
    public static FrameworkElement Segmented<T>(IEnumerable<(T value, string label)> items, T selected, Action<T> changed, string? group = null)
    {
        var track = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
        track.SetResourceReference(Border.BackgroundProperty, Theme.SegmentTrack);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        group ??= "seg" + Guid.NewGuid().ToString("N"); // meme nom = choix exclusif entre plusieurs rangees
        foreach (var (v, l) in items)
        {
            var rb = new RadioButton { Content = l, GroupName = group, IsChecked = Equals(v, selected), Tag = v };
            if (Application.Current?.TryFindResource(Theme.SegmentStyle) is Style st) rb.Style = st;
            rb.Checked += (_, _) => changed(v);
            panel.Children.Add(rb);
        }
        track.Child = panel;
        return track;
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(24, 16, 24, 16),
    };

    public static CheckBox Check(string text, bool value, Action<bool> changed, string? tooltip = null)
    {
        // Rendu en interrupteur a bascule (style implicite de Theme) : libelle a gauche, bascule a droite.
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 2, 0, 2), ToolTip = tooltip };
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    public static ComboBox Combo<T>(IEnumerable<(T value, string label)> items, T selected, Action<T> changed, double minWidth = 220)
    {
        var cb = new ComboBox { MinWidth = minWidth, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (v, l) in items) cb.Items.Add(new ComboBoxItem { Content = l, Tag = v });
        foreach (ComboBoxItem it in cb.Items) if (Equals(it.Tag, selected)) cb.SelectedItem = it;
        if (cb.SelectedIndex < 0 && cb.Items.Count > 0) cb.SelectedIndex = 0;
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is ComboBoxItem { Tag: T v }) changed(v); };
        return cb;
    }

    public static Slider Slider(double min, double max, double value, Action<double> changed, double tick = 1)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = 240, IsSnapToTickEnabled = true, TickFrequency = tick, HorizontalAlignment = HorizontalAlignment.Left };
        s.ValueChanged += (_, e) => changed(e.NewValue);
        return s;
    }

    /// <summary>Fond, police, couleur de texte et barre de titre (claire/sombre) d'une fenetre de l'application.</summary>
    public static void Decorate(Window w)
    {
        w.FontFamily = TextFont;
        w.FontSize = 13;
        w.SetResourceReference(Window.BackgroundProperty, Theme.WindowBg);
        w.SetResourceReference(Window.ForegroundProperty, Theme.TextPrimary);
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            WindowsTheme.StyleWindow(h, Theme.IsDark, mica: false);
        };
        w.UseLayoutRounding = true;
        if (System.IO.File.Exists(AppIconPath))
        {
            // BitmapFrame garde toutes les tailles de l'icone : Windows choisit la bonne (barre de titre, Alt+Tab...).
            try { w.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(AppIconPath)); } catch { }
        }
    }

    public static string AppIconPath => System.IO.Path.Combine(AppContext.BaseDirectory, "filewheel.ico");
    /// <summary>Logo en grand format (256 px) pour l'interface.</summary>
    public static string AppLogoPath => System.IO.Path.Combine(AppContext.BaseDirectory, "filewheel.png");

    /// <summary>Boite de message coherente, centree sur la fenetre proprietaire si elle existe.</summary>
    public static MessageBoxResult Message(string text, string title = "FileWheel", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information, MessageBoxResult def = MessageBoxResult.OK, Window? owner = null)
    {
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive);
        return owner is not null
            ? MessageBox.Show(owner, text, title, buttons, image, def)
            : MessageBox.Show(text, title, buttons, image, def, MessageBoxOptions.None);
    }

    public static string Size(long bytes) => FileWheel.Core.FileOperations.SafeFileOps.FormatSize(bytes);
}
