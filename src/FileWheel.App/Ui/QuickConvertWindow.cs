using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Formats;
using FileWheel.Core.Settings;
using FileWheel.Core.Wheel;

namespace FileWheel.App.Ui;

/// <summary>
/// Conversion rapide : fichier(s) detecte(s), formats de sortie REELLEMENT disponibles,
/// reglages essentiels, analyse (pertes, transparence...) avant lancement, progression.
/// </summary>
public sealed class QuickConvertWindow : Window
{
    private readonly ConversionEngine _engine;
    private readonly Func<AppSettings> _settings;
    private readonly IReadOnlyList<string> _files;
    private readonly Action<string> _openEngines;
    private readonly ListBox _routes = new() { MinHeight = 160, MaxHeight = 260 };
    private readonly OptionsPanel _options = new();
    private readonly StackPanel _analysis = new();
    private readonly TextBox _dest = new() { MinWidth = 300 };
    private readonly RadioButton _destSame = new() { Content = "À côté de l'original", IsChecked = true, GroupName = "dest" };
    private readonly RadioButton _destOther = new() { Content = "Dans le dossier :", GroupName = "dest" };
    private readonly StackPanel _progress = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _convert;
    private readonly DispatcherTimer _debounce;
    private ConflictPolicy _conflicts;
    private readonly List<ConversionJob> _jobs = new();
    private int _analysisVersion;

    public QuickConvertWindow(ConversionEngine engine, Func<AppSettings> settings, IReadOnlyList<string> files, string? preselectKey, Action<string> openEngines, Action<IReadOnlyList<string>> openAdvanced)
    {
        _engine = engine;
        _settings = settings;
        _files = files;
        _openEngines = openEngines;
        _conflicts = settings().Conversion.Conflicts;
        Title = "Conversion rapide";
        Width = 720;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.Decorate(this);

        var det = FormatDetector.Detect(files[0]);
        var body = new StackPanel();
        body.Children.Add(Header(det));

        // Routes disponibles
        body.Children.Add(Ui.Heading("Convertir en"));
        var formats = files.Select(f => FormatDetector.Detect(f).Format?.Id).Where(f => f is not null).Distinct().ToList();
        var routes = formats.Count == 0 ? [] : engine.Registry.CommonRoutes(formats!).ToList();
        if (files.Count > 1)
        {
            // Conversions « plusieurs fichiers -> un seul » (assemblage PDF, fusion audio)
            var merges = engine.Registry.AllCapabilities.Where(c => c.MultiInput && formats.All(f => c.From.Contains(f!)))
                .GroupBy(c => c.To).Select(g => g.OrderBy(c => engine.Registry.EngineOk(c) ? 0 : 1).ThenBy(c => c.Priority).First())
                .Select(c => new RouteInfo(c, engine.Registry.EngineOk(c), engine.Registry.MissingEngine(c) is { } m ? EngineCatalog.Get(m).Name : null));
            routes = routes.Where(r => !r.Capability.MultiInput).Concat(merges).ToList();
        }
        else routes = routes.Where(r => !r.Capability.MultiInput).ToList();
        if (det.IsExecutable) routes.Clear();

        foreach (var r in routes)
        {
            var label = r.Label + (r.Available ? "" : $"  —  nécessite {r.MissingEngine}");
            var item = new ListBoxItem { Content = label, Tag = r, ToolTip = r.Capability.LossNote ?? r.Capability.Limitations };
            if (!r.Available) item.Foreground = Ui.Subtle;
            _routes.Items.Add(item);
            if (preselectKey is not null && r.Capability.Key == preselectKey) _routes.SelectedItem = item;
        }
        if (_routes.Items.Count == 0)
            body.Children.Add(Ui.Text(det.IsExecutable ? "Les programmes et scripts ne sont jamais convertis." : "Aucune conversion connue pour ce format avec les moteurs actuels.", Ui.Warning));
        else body.Children.Add(_routes);
        if (_routes.SelectedIndex < 0) _routes.SelectedItem = _routes.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((RouteInfo)i.Tag).Available);
        _routes.SelectionChanged += (_, _) => RouteChanged();

        // Reglages
        body.Children.Add(Ui.Heading("Réglages"));
        body.Children.Add(_options);
        _options.Changed += () => ScheduleAnalysis();

        // Destination et conflits
        body.Children.Add(Ui.Heading("Destination"));
        var cfg = settings().Conversion;
        if (!string.IsNullOrWhiteSpace(cfg.DefaultOutputDir)) { _destOther.IsChecked = true; _dest.Text = cfg.DefaultOutputDir; }
        body.Children.Add(_destSame);
        var other = Ui.Row(_destOther, _dest, Ui.Button("Parcourir…", () =>
        {
            var f = Pickers.Folder("Dossier de destination", _dest.Text.Length > 0 ? _dest.Text : Path.GetDirectoryName(files[0]));
            if (f is not null) { _dest.Text = f; _destOther.IsChecked = true; }
        }));
        body.Children.Add(other);
        body.Children.Add(Ui.Field("Si le fichier existe déjà", Ui.Combo(
            [(ConflictPolicy.Rename, "Garder les deux (ajouter « (2) »)"), (ConflictPolicy.Skip, "Ignorer ce fichier"), (ConflictPolicy.Overwrite, "Remplacer le fichier existant")],
            _conflicts == ConflictPolicy.Ask ? ConflictPolicy.Rename : _conflicts, v => _conflicts = v)));
        body.Children.Add(Ui.Hint("Le fichier d'origine n'est jamais modifié ni remplacé."));

        body.Children.Add(Ui.Heading("Avant de lancer"));
        body.Children.Add(_analysis);
        body.Children.Add(_progress);

        _convert = Ui.Button(files.Count > 1 ? $"Convertir {files.Count} fichiers" : "Convertir", Start, accent: true, glyph: Glyphs.Convert);
        var buttons = Ui.Row(_convert,
            Ui.Button("Convertisseur avancé…", () => { openAdvanced(files); Close(); }),
            Ui.Button("Fermer", Close));
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(24, 8, 16, 16);

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(Ui.Scroll(body));
        Content = dock;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Analyze(); };
        _engine.Queue.JobChanged += OnJobChanged;
        Closed += (_, _) => _engine.Queue.JobChanged -= OnJobChanged;
        RouteChanged();
    }

    private UIElement Header(DetectionResult det)
    {
        var col = new StackPanel();
        var f = _files[0];
        var title = _files.Count == 1 ? Path.GetFileName(f) : $"{_files.Count} fichiers ({Path.GetFileName(f)}…)";
        col.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        long size = _files.Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } });
        var info = $"{det.Format?.Label ?? "Format inconnu"}  ·  {Ui.Size(size)}  ·  détection : {det.Method}";
        var infoText = Ui.Text(info, Ui.Subtle, 12);
        col.Children.Add(infoText);
        if (det.Warning is not null) col.Children.Add(Ui.Text(det.Warning, Ui.Warning, 12));
        // Apercu leger (sans charger les gros fichiers)
        _ = Task.Run(() => Preview(det)).ContinueWith(t =>
        {
            if (t.Result is { Length: > 0 } extra) Dispatcher.Invoke(() => infoText.Text += "  ·  " + extra);
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Icon(ConversionRegistry.GlyphFor(det.Format?.Id ?? ""), 34, Ui.Accent));
        col.Margin = new Thickness(14, 0, 0, 0);
        row.Children.Add(col);
        return Ui.Card(row);
    }

    private string Preview(DetectionResult det)
    {
        try
        {
            var f = _files[0];
            switch (det.Format?.Category)
            {
                case FormatCategory.Image when det.Format.Id is "png" or "jpg" or "bmp" or "gif" or "tiff" or "ico":
                {
                    using var fs = File.OpenRead(f);
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var fr = dec.Frames[0];
                    return $"{fr.PixelWidth} × {fr.PixelHeight} px" + (dec.Frames.Count > 1 ? $", {dec.Frames.Count} images" : "");
                }
                case FormatCategory.Video or FormatCategory.Audio:
                {
                    var ffmpeg = _engine.Dependencies.PathOf(EngineIds.FFmpeg);
                    if (ffmpeg is null) return "";
                    var info = FileWheel.Conversion.Providers.Media.MediaProbe.ProbeAsync(_engine.Dependencies.PathOf(EngineIds.FFprobe), ffmpeg, f, CancellationToken.None).GetAwaiter().GetResult();
                    var parts = new List<string>();
                    if (info.Duration is { } d) parts.Add(TimeSpan.FromSeconds(d).ToString(d >= 3600 ? @"h\:mm\:ss" : @"m\:ss"));
                    var v = info.Video.FirstOrDefault();
                    if (v is not null) parts.Add($"{v.Width} × {v.Height} {v.Codec}");
                    var a = info.Audio.FirstOrDefault();
                    if (a is not null) parts.Add($"audio {a.Codec}");
                    return string.Join(", ", parts);
                }
                case FormatCategory.Pdf:
                    return _engine.Pdfium.PageCount(f) is { } n ? $"{n} page(s)" : "";
            }
        }
        catch { }
        return "";
    }

    private RouteInfo? Selected => (_routes.SelectedItem as ListBoxItem)?.Tag as RouteInfo;

    private void RouteChanged()
    {
        var r = Selected;
        if (r is null)
        {
            _options.Load([], null, false);
            _convert.IsEnabled = false;
            return;
        }
        var initial = r.Capability.Options.Any(o => o.Key == "quality") ? new Dictionary<string, string> { ["quality"] = _settings().Conversion.DefaultImageQuality.ToString() } : null;
        _options.Load(r.Capability.Options, initial, showAdvanced: true);
        _convert.IsEnabled = r.Available;
        _analysis.Children.Clear();
        if (!r.Available)
        {
            var missing = r.MissingEngine ?? "";
            _analysis.Children.Add(Ui.Text($"Cette conversion nécessite {missing}, qui n'est pas installé.", Ui.Warning));
            _analysis.Children.Add(Ui.Button("Installer ou indiquer le moteur…", () => _openEngines(missing), glyph: Glyphs.Settings));
            return;
        }
        ScheduleAnalysis();
    }

    private void ScheduleAnalysis()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Analyze()
    {
        var r = Selected;
        if (r is null || !r.Available) return;
        int version = ++_analysisVersion;
        var order = Order(r, r.Capability.MultiInput ? _files : [_files[0]]);
        _analysis.Children.Clear();
        _analysis.Children.Add(Ui.Hint("Analyse…"));
        Task.Run(() =>
        {
            try { var p = _engine.Manager.Prepare(order); return (p.Analysis, (string?)null); }
            catch (ConversionException ex) { return ((ConversionAnalysis?)null, ex.Message); }
        }).ContinueWith(t => Dispatcher.Invoke(() =>
        {
            if (version != _analysisVersion) return;
            _analysis.Children.Clear();
            var (a, error) = t.Result;
            if (error is not null) { _analysis.Children.Add(Ui.Text(error, Ui.Error)); _convert.IsEnabled = false; return; }
            _convert.IsEnabled = !(a?.Blocking ?? false);
            if (r.Capability.Loss == LossKind.None && (a?.Warnings.Count ?? 0) == 0) _analysis.Children.Add(Ui.Text("Conversion sans perte.", Ui.Success));
            foreach (var w in a!.Warnings) _analysis.Children.Add(Ui.Text("⚠ " + w, Ui.Warning));
            foreach (var n in a.Notes.Distinct()) _analysis.Children.Add(Ui.Text("• " + n, Ui.Subtle));
            if (a.EstimatedBytes is { } est) _analysis.Children.Add(Ui.Hint($"Taille estimée : environ {Ui.Size(est)}."));
        }));
    }

    private ConversionOrder Order(RouteInfo r, IReadOnlyList<string> inputs) => new()
    {
        Inputs = inputs,
        CapabilityKey = r.Capability.Key,
        Options = new Dictionary<string, string>(_options.Values),
        OutputDir = _destOther.IsChecked == true && _dest.Text.Trim().Length > 0 ? _dest.Text.Trim() : null,
        Conflicts = _conflicts,
    };

    private void Start()
    {
        var r = Selected;
        if (r is null || !r.Available) return;
        if (_destOther.IsChecked == true && !Directory.Exists(_dest.Text.Trim()))
        {
            Ui.Message("Le dossier de destination n'existe pas.", image: MessageBoxImage.Warning, owner: this);
            return;
        }
        _jobs.Clear();
        if (r.Capability.MultiInput) _jobs.Add(_engine.Queue.Enqueue(Order(r, _files), $"{_files.Count} fichiers → {r.Label}"));
        else foreach (var f in _files) _jobs.Add(_engine.Queue.Enqueue(Order(r, [f])));
        _convert.IsEnabled = false;
        RenderProgress();
    }

    private void OnJobChanged(ConversionJob j)
    {
        if (j is null || !_jobs.Contains(j)) return;
        Dispatcher.BeginInvoke(RenderProgress);
    }

    private void RenderProgress()
    {
        _progress.Children.Clear();
        if (_jobs.Count == 0) return;
        int done = _jobs.Count(j => j.IsFinished);
        double frac = _jobs.Sum(j => j.IsFinished ? 1 : j.Progress ?? 0) / _jobs.Count;
        var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Value = frac, Margin = new Thickness(0, 4, 0, 4), IsIndeterminate = done == 0 && _jobs.All(j => j.Progress is null) };
        _progress.Children.Add(bar);
        var running = _jobs.FirstOrDefault(j => j.Status == JobStatus.Running);
        _progress.Children.Add(Ui.Text(done < _jobs.Count ? $"{done} / {_jobs.Count} terminé(s){(running is not null ? " — " + running.Stage : "")}" : "Terminé.", Ui.Subtle, 12));
        foreach (var j in _jobs.Where(j => j.IsFinished))
        {
            if (j.Status == JobStatus.Succeeded)
            {
                var outp = j.Outputs.FirstOrDefault() ?? "";
                var row = Ui.Row(Ui.Icon(Glyphs.Check, 14, Ui.Success), new TextBlock { Text = "  " + Path.GetFileName(outp), VerticalAlignment = VerticalAlignment.Center },
                    Ui.Button("Ouvrir le dossier", () => FileWheel.Windows.Shell.ShellOps.RevealInExplorer([outp])));
                _progress.Children.Add(row);
                foreach (var w in j.Warnings.Take(3)) _progress.Children.Add(Ui.Text("⚠ " + w, Ui.Warning, 12));
            }
            else _progress.Children.Add(Ui.Text($"✕ {j.Title} : {j.Error ?? j.StatusLabel}", j.Status == JobStatus.Failed ? Ui.Error : Ui.Subtle, 12));
        }
        if (done == _jobs.Count) _convert.IsEnabled = true;
    }
}
