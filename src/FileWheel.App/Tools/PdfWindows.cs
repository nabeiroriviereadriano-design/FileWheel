using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileWheel.App.Ui;
using FileWheel.Conversion.Providers.Pdf;
using FileWheel.Core.Localization;
using FileWheel.Core.Logging;

namespace FileWheel.App.Tools;

/// <summary>Rendu des miniatures de pages (PDFium) et encodeur JPEG (codecs Windows) des outils PDF.</summary>
public static class PdfImaging
{
    /// <summary>Page rendue en image WPF (RVBA → BVRA).</summary>
    public static BitmapSource Thumbnail(Pdfium.Document doc, int index, double dpi)
    {
        var (w, h, rgba) = doc.Render(index, dpi);
        for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, rgba, w * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Encodeur JPEG pour la compression des images d'un PDF (utilisable hors du fil de l'interface).</summary>
    public static byte[] Jpeg(int w, int h, byte[] rgb, int quality)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Rgb24, null, rgb, w * 3);
        var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>« rapport.pdf » + « pages 1-3 » → « dossier\rapport pages 1-3.pdf ».</summary>
    public static string Output(string source, string suffix) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, $"{Path.GetFileNameWithoutExtension(source)} {suffix}.pdf");
}

/// <summary>
/// Pages d'un PDF : miniatures, selection (clic, Maj+clic, Ctrl+clic, ou « 1-3, 5 »), puis
/// extraire, supprimer ou pivoter la selection. Le resultat est toujours un nouveau PDF.
/// </summary>
public sealed class PdfPagesWindow : ToolWindow
{
    private readonly string _path;
    private readonly int _count;
    private readonly WrapPanel _grid = new() { Margin = new Thickness(12) };
    private readonly List<Border> _tiles = new();
    private readonly SortedSet<int> _selected = new();
    private readonly TextBox _range = new() { MinWidth = 160 };
    private readonly TextBlock _info = Ui.Ui.Text("");
    private int _anchor = -1;

    public PdfPagesWindow(string path, int pageCount) : base("Pages du PDF", Path.GetFileName(path), 1080, 720)
    {
        _path = path;
        _count = pageCount;
        PreviewHost.Background = null;
        PreviewHost.Child = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        Section("Sélection");
        Add(_info);
        Add(Ui.Ui.Hint("Cliquez sur les pages ; Maj+clic pour une suite, Ctrl+clic pour en ajouter une."));
        Add(Ui.Ui.Row(_range, Ui.Ui.Button("Choisir", ApplyRange)));
        Add(Ui.Ui.Hint("Exemple : 1-3, 5, 8-10"));
        Add(Ui.Ui.Row(Ui.Ui.Button("Tout", () => SetSelection(Enumerable.Range(1, _count))), Ui.Ui.Button("Aucune", () => SetSelection([]))));

        Section("Nouveau PDF avec…");
        Add(Ui.Ui.Button("Seulement ces pages", () => Act("extract"), glyph: ""));
        Add(Ui.Ui.Spacer(6));
        Add(Ui.Ui.Button("Toutes sauf ces pages", () => Act("delete"), glyph: ""));
        Section("Pivoter ces pages");
        Add(Ui.Ui.Row(Ui.Ui.Button("↻ 90°", () => Act("rotate1")), Ui.Ui.Button("↺ 90°", () => Act("rotate3")), Ui.Ui.Button("180°", () => Act("rotate2"))));
        Add(Ui.Ui.Hint("Le PDF d'origine n'est jamais modifié."));

        for (int i = 1; i <= _count; i++) _grid.Children.Add(MakeTile(i));
        UpdateInfo();
        Loaded += async (_, _) => await LoadThumbnailsAsync();
    }

    private Border MakeTile(int page)
    {
        var img = new Image { Width = 120, Height = 160, Stretch = Stretch.Uniform };
        var label = new TextBlock { Text = page.ToString(), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var tile = new Border
        {
            Child = new StackPanel { Children = { img, label } },
            Margin = new Thickness(6), Padding = new Thickness(6), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, Tag = page,
        };
        tile.SetResourceReference(Border.BackgroundProperty, Theme.Surface);
        tile.MouseLeftButtonUp += (_, _) => Click(page);
        _tiles.Add(tile);
        return tile;
    }

    private async Task LoadThumbnailsAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                using var doc = Pdfium.Open(_path, null);
                int n = Math.Min(_count, 400); // au-dela, numeros seulement (memoire)
                for (int i = 0; i < n && IsVisibleOnScreen(); i++)
                {
                    var bmp = PdfImaging.Thumbnail(doc, i, 28);
                    int index = i;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_tiles[index].Child is StackPanel { Children: [Image img, ..] }) img.Source = bmp;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Miniatures PDF : " + ex.Message);
            Status.Text = Loc.T("Aperçu des pages indisponible.");
        }
    }

    private bool _closed;
    protected override void OnClosed(EventArgs e) { _closed = true; base.OnClosed(e); }
    private bool IsVisibleOnScreen() => !_closed;

    private void Click(int page)
    {
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Shift) && _anchor > 0)
        {
            int a = Math.Min(_anchor, page), b = Math.Max(_anchor, page);
            for (int i = a; i <= b; i++) _selected.Add(i);
        }
        else if (mods.HasFlag(ModifierKeys.Control) || _selected.Count > 0 && !mods.HasFlag(ModifierKeys.Shift))
        {
            if (!_selected.Remove(page)) _selected.Add(page);
            _anchor = page;
        }
        else
        {
            _selected.Add(page);
            _anchor = page;
        }
        Refresh();
    }

    private void SetSelection(IEnumerable<int> pages)
    {
        _selected.Clear();
        foreach (var p in pages) _selected.Add(p);
        Refresh();
    }

    private void ApplyRange()
    {
        try { SetSelection(PdfTools.ParsePages(_range.Text, _count)); }
        catch (Exception ex) { Status.Text = Loc.T(ex.Message); }
    }

    private void Refresh()
    {
        foreach (var t in _tiles)
        {
            bool on = _selected.Contains((int)t.Tag);
            if (on) t.SetResourceReference(Border.BorderBrushProperty, Theme.Accent);
            else t.BorderBrush = Brushes.Transparent;
        }
        _range.Text = PdfTools.FormatPages(_selected);
        UpdateInfo();
    }

    private void UpdateInfo() => _info.Text = _selected.Count == 0
        ? Loc.F("{0} page(s) — aucune sélectionnée", _count)
        : Loc.F("{0} page(s) sélectionnée(s) sur {1}", _selected.Count, _count);

    private void Act(string what)
    {
        if (_selected.Count == 0) { Status.Text = Loc.T("Sélectionnez au moins une page."); return; }
        var pages = _selected.ToList();
        var label = PdfTools.FormatPages(pages);
        try
        {
            string result = what switch
            {
                "extract" => PdfTools.ExtractPages(_path, pages, PdfImaging.Output(_path, Loc.T("pages") + " " + label)),
                "delete" => PdfTools.DeletePages(_path, pages, PdfImaging.Output(_path, Loc.T("sans les pages") + " " + label)),
                _ => PdfTools.Rotate(_path, pages, int.Parse(what[^1..]), PdfImaging.Output(_path, Loc.T("pivoté"))),
            };
            Done(result);
        }
        catch (Exception ex)
        {
            Log.Error("Pages PDF", ex);
            Ui.Ui.Message(FileWheel.Conversion.Core.ConversionManager.Translate(ex).Message, image: MessageBoxImage.Warning, owner: this);
        }
    }
}

/// <summary>Ordre des PDF a fusionner (glisser-deposer ou boutons Monter / Descendre).</summary>
public sealed class PdfMergeDialog : DialogWindow
{
    private readonly ListBox _list = new() { Height = 220, Margin = new Thickness(0, 6, 0, 6) };
    public List<string> Ordered { get; private set; } = new();

    public PdfMergeDialog(IReadOnlyList<string> files) : base("Fusionner des PDF", 560)
    {
        Body.Children.Add(Ui.Ui.Text("Les PDF seront assemblés dans cet ordre, en un nouveau fichier. Les originaux ne changent pas."));
        foreach (var f in files.OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase))
            _list.Items.Add(new ListBoxItem { Content = Path.GetFileName(f), Tag = f });
        _list.SelectedIndex = 0;
        Body.Children.Add(_list);
        Body.Children.Add(Ui.Ui.Row(
            Ui.Ui.Button("Monter", () => Move(-1), glyph: ""),
            Ui.Ui.Button("Descendre", () => Move(1), glyph: ""),
            Ui.Ui.Button("Trier par nom", () => Sort())));
        AddButton("Annuler", false, isCancel: true);
        Buttons.Children.Add(Ui.Ui.Button("Fusionner", () =>
        {
            Ordered = _list.Items.Cast<ListBoxItem>().Select(i => (string)i.Tag).ToList();
            DialogResult = true;
        }, accent: true));
    }

    private void Move(int delta)
    {
        int i = _list.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _list.Items.Count) return;
        var item = _list.Items[i];
        _list.Items.RemoveAt(i);
        _list.Items.Insert(j, item);
        _list.SelectedIndex = j;
    }

    private void Sort()
    {
        var items = _list.Items.Cast<ListBoxItem>().OrderBy(i => i.Content as string, StringComparer.CurrentCultureIgnoreCase).ToList();
        _list.Items.Clear();
        foreach (var i in items) _list.Items.Add(i);
    }
}

/// <summary>Reglages de compression d'un PDF : usage (ecran / standard / impression).</summary>
public sealed class PdfCompressDialog : DialogWindow
{
    public int Dpi { get; private set; } = 150;
    public int Quality { get; private set; } = 70;

    public PdfCompressDialog(int fileCount) : base("Compresser le PDF", 520)
    {
        Body.Children.Add(Ui.Ui.Text("Les photos et images du PDF sont réduites et recompressées en JPEG. Le texte et les dessins restent nets (vectoriels)."));
        Body.Children.Add(Ui.Ui.Heading("Usage"));
        Body.Children.Add(Ui.Ui.Segmented([("screen", "Écran (96 ppp)"), ("standard", "Standard (150 ppp)"), ("print", "Impression (220 ppp)")], "standard", v =>
        {
            (Dpi, Quality) = v switch { "screen" => (96, 60), "print" => (220, 82), _ => (150, 70) };
        }));
        Body.Children.Add(Ui.Ui.Hint("Les scans en noir et blanc, les images transparentes et les petites images sont laissés tels quels. Une image n'est remplacée que si elle devient nettement plus légère."));
        if (fileCount > 1) Body.Children.Add(Ui.Ui.Hint(Loc.F("{0} PDF seront compressés, chacun dans un nouveau fichier.", fileCount)));
        AddButton("Annuler", false, isCancel: true);
        AddButton("Compresser", true, accent: true);
    }
}
