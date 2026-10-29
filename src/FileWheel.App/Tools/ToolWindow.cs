using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileWheel.App.Ui;
using FileWheel.Core.Logging;

namespace FileWheel.App.Tools;

/// <summary>
/// Fenetre commune des outils image (charte hybride) : apercu au centre, reglages a droite,
/// pied de fenetre teinte avec un message d'etat a gauche et Annuler / action principale a droite.
/// </summary>
public abstract class ToolWindow : Window
{
    protected readonly Grid Root = new();
    protected readonly Border PreviewHost = new() { Margin = new Thickness(20, 20, 12, 12), ClipToBounds = true };
    protected readonly StackPanel Side = new() { Margin = new Thickness(8, 20, 20, 12), Width = 280 };
    protected readonly TextBlock Status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    protected Button? Primary;

    /// <summary>Chemin du fichier cree (null si annule).</summary>
    public string? Result { get; protected set; }

    protected ToolWindow(string title, string fileName, double width = 1080, double height = 720)
    {
        Title = $"{title} — {fileName}";
        Width = width;
        Height = height;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.Ui.Decorate(this);
        Status.SetResourceReference(TextBlock.ForegroundProperty, Theme.TextSecondary);

        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Damier discret derriere l'apercu : la transparence se voit
        PreviewHost.Background = Checkerboard();
        PreviewHost.CornerRadius = Theme.CardRadius;
        Root.Children.Add(PreviewHost);

        var sideScroll = new ScrollViewer { Content = Side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(sideScroll, 1);
        Root.Children.Add(sideScroll);

        var footer = new Border { Padding = new Thickness(20, 12, 16, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BackgroundProperty, Theme.FooterBg);
        footer.SetResourceReference(Border.BorderBrushProperty, Theme.Separator);
        var fg = new DockPanel();
        DockPanel.SetDock(_buttons, Dock.Right);
        fg.Children.Add(_buttons);
        fg.Children.Add(Status);
        footer.Child = fg;
        Grid.SetRow(footer, 1);
        Grid.SetColumnSpan(footer, 2);
        Root.Children.Add(footer);
        Content = Root;

        var cancel = Ui.Ui.Button("Annuler", () => Close());
        cancel.IsCancel = true;
        _buttons.Children.Add(cancel);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    protected void SetPrimary(string text, Action onClick)
    {
        Primary = Ui.Ui.Button(text, () =>
        {
            try { onClick(); }
            catch (Exception ex)
            {
                Log.Error($"Outil « {Title} »", ex);
                Ui.Ui.Message(ex.Message, image: MessageBoxImage.Warning, owner: this);
            }
        }, accent: true);
        Primary.Margin = new Thickness(0);
        _buttons.Children.Add(Primary);
    }

    protected void Section(string title) => Side.Children.Add(Ui.Ui.Heading(title));

    protected void Add(UIElement e) => Side.Children.Add(e);

    /// <summary>Termine : memorise le resultat, l'annonce et ferme la fenetre.</summary>
    protected void Done(string path)
    {
        Result = path;
        Close();
    }

    private static Brush Checkerboard()
    {
        bool dark = Theme.IsDark;
        var a = dark ? Color.FromRgb(0x2A, 0x2A, 0x2D) : Color.FromRgb(0xF2, 0xF2, 0xF5);
        var b = dark ? Color.FromRgb(0x33, 0x33, 0x37) : Color.FromRgb(0xE6, 0xE6, 0xEB);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var brush = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    /// <summary>Affiche une erreur de chargement et ferme proprement.</summary>
    public static bool TryLoad(string path, Window? owner, out ImageIO.Loaded loaded)
    {
        try
        {
            loaded = ImageIO.Load(path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Image illisible ({Path.GetExtension(path)}) : {ex.Message}");
            Ui.Ui.Message(ex is InvalidOperationException ? ex.Message : "Image illisible : " + ex.Message, image: MessageBoxImage.Warning, owner: owner);
            loaded = null!;
            return false;
        }
    }
}

/// <summary>
/// Apercu d'image avec calque de dessin en coordonnees de PIXELS de l'image : la mise a l'echelle
/// est faite par une Viewbox, donc les outils n'ont jamais a convertir les coordonnees.
/// </summary>
public sealed class ImageSurface : Viewbox
{
    public Image Picture { get; } = new() { Stretch = Stretch.Fill };
    public Canvas Overlay { get; } = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Grid _grid = new();

    public ImageSurface()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        RenderOptions.SetBitmapScalingMode(Picture, BitmapScalingMode.HighQuality);
        _grid.Children.Add(Picture);
        _grid.Children.Add(Overlay);
        Child = _grid;
    }

    public void SetImage(BitmapSource bmp)
    {
        Picture.Source = bmp;
        _grid.Width = Overlay.Width = bmp.PixelWidth;
        _grid.Height = Overlay.Height = bmp.PixelHeight;
    }

    /// <summary>Pixels d'ecran par pixel d'image (pour garder poignees et traits a taille constante).</summary>
    public double DisplayScale => _grid.Width > 0 && ActualWidth > 0
        ? Math.Min(ActualWidth / _grid.Width, ActualHeight / _grid.Height)
        : 1;
}
