using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Formats;
using FileWheel.Core.Settings;
using FileWheel.Core.Wheel;

namespace FileWheel.App.Ui;

/// <summary>
/// Convertisseur avance : catalogue des conversions par categorie, recherche de format,
/// conversion par lot avec reglages communs ou individuels, profils enregistres.
/// </summary>
public sealed class ConverterWindow : Window
{
    private sealed class BatchItem
    {
        public required string Path { get; init; }
        public FormatInfo? Format { get; init; }
        public string? Warning { get; init; }
        public Dictionary<string, string>? Override { get; set; }
        public ConversionJob? Job { get; set; }
    }

    private readonly ConversionEngine _engine;
    private readonly Func<AppSettings> _settings;
    private readonly Action<string> _openEngines;
    private readonly List<BatchItem> _items = new();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended };
    private readonly ComboBox _target = new() { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly OptionsPanel _options = new();
    private readonly CheckBox _individual = new() { Content = "Réglages individuels pour le fichier sélectionné", Margin = new Thickness(0, 6, 0, 6) };
    private readonly ComboBox _presets = new() { MinWidth = 200 };
    private readonly TextBox _dest = new() { MinWidth = 260 };
    private readonly StackPanel _catalog = new();
    private readonly TextBox _search = new() { MinWidth = 220 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private ConflictPolicy _conflicts;
    private bool _loadingOptions;

    public ConverterWindow(ConversionEngine engine, Func<AppSettings> settings, IReadOnlyList<string> initialFiles, Action<string> openEngines)
    {
        _engine = engine;
        _settings = settings;
        _openEngines = openEngines;
        _conflicts = settings().Conversion.Conflicts is ConflictPolicy.Ask ? ConflictPolicy.Rename : settings().Conversion.Conflicts;
        Title = "FileWheel — Convertisseur avancé";
        Width = 1180;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.Decorate(this);

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });

        // ---- Colonne 1 : catalogue et recherche
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var searchRow = Ui.Column(Ui.Heading("Formats et conversions"), Ui.Row(Ui.Icon("", 14), _search),
            Ui.Hint("Tapez un format (ex. « WebP ») pour voir les conversions possibles."));
        DockPanel.SetDock(searchRow, Dock.Top);
        left.Children.Add(searchRow);
        left.Children.Add(new ScrollViewer { Content = _catalog, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _search.TextChanged += (_, _) => RenderCatalog();
        grid.Children.Add(left);

        // ---- Colonne 2 : lot de fichiers
        var center = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var top = Ui.Column(Ui.Heading("Fichiers à convertir"),
            Ui.Row(
                Ui.Button("Ajouter des fichiers…", () => AddFiles(Pickers.Files("Fichiers à convertir")), glyph: Glyphs.Add),
                Ui.Button("Ajouter un dossier…", AddFolder, glyph: Glyphs.Folder),
                Ui.Button("Retirer", RemoveSelected, glyph: Glyphs.Cancel),
                Ui.Button("Vider", () => { _items.Clear(); ItemsChanged(); })));
        DockPanel.SetDock(top, Dock.Top);
        center.Children.Add(top);
        var bottom = Ui.Column(_summary);
        DockPanel.SetDock(bottom, Dock.Bottom);
        center.Children.Add(bottom);
        center.Children.Add(_list);
        _list.SelectionChanged += (_, _) => { if (_individual.IsChecked == true) LoadOptions(); };
        _list.AllowDrop = true;
        _list.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped) AddFiles(dropped);
            e.Effects = DragDropEffects.Copy; // ajout a la liste : la source n'est jamais modifiee
            e.Handled = true;
        };
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);

        // ---- Colonne 3 : cible, reglages, destination
        var right = new StackPanel();
        right.Children.Add(Ui.Heading("Convertir en"));
        right.Children.Add(_target);
        _target.SelectionChanged += (_, _) => LoadOptions();
        right.Children.Add(Ui.Heading("Profils"));
        right.Children.Add(Ui.Row(_presets,
            Ui.Button("Enregistrer…", SavePreset),
            Ui.Button("Supprimer", DeletePreset)));
        _presets.SelectionChanged += (_, _) => ApplyPreset();
        right.Children.Add(Ui.Heading("Réglages"));
        right.Children.Add(_individual);
        _individual.Checked += (_, _) => LoadOptions();
        _individual.Unchecked += (_, _) => LoadOptions();
        right.Children.Add(_options);
        _options.Changed += OptionsChanged;
        right.Children.Add(Ui.Heading("Destination"));
        right.Children.Add(Ui.Row(_dest, Ui.Button("…", () => { var f = Pickers.Folder("Dossier de destination"); if (f is not null) _dest.Text = f; })));
        right.Children.Add(Ui.Hint("Vide = à côté de chaque fichier d'origine."));
        _dest.Text = settings().Conversion.DefaultOutputDir;
        right.Children.Add(Ui.Field("Si le fichier existe", Ui.Combo(
            [(ConflictPolicy.Rename, "Garder les deux"), (ConflictPolicy.Skip, "Ignorer"), (ConflictPolicy.Overwrite, "Remplacer")], _conflicts, v => _conflicts = v, 160), labelWidth: 130));
        var start = Ui.Button("Lancer la conversion", Start, accent: true, glyph: Glyphs.Convert);
        start.Margin = new Thickness(0, 16, 8, 0);
        right.Children.Add(Ui.Row(start,
            Ui.Button(_engine.Queue.Paused ? "Reprendre la file" : "Pause de la file", () => { if (_engine.Queue.Paused) _engine.Queue.Resume(); else _engine.Queue.Pause(); }),
            Ui.Button("Relancer les échecs", RetryFailed)));
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rightScroll, 2);
        grid.Children.Add(rightScroll);

        Content = grid;
        _engine.Queue.JobChanged += OnJob;
        Closed += (_, _) => _engine.Queue.JobChanged -= OnJob;
        AddFiles(initialFiles);
        RenderCatalog();
    }

    // ------------------------------------------------------------------ catalogue

    private void RenderCatalog()
    {
        _catalog.Children.Clear();
        var text = _search.Text.Trim();
        if (text.Length > 0)
        {
            var hits = _engine.Registry.Search(text).Take(200).ToList();
            if (hits.Count == 0) { _catalog.Children.Add(Ui.Hint("Aucune conversion pour « " + text + " »."));  return; }
            foreach (var (cap, ok) in hits) _catalog.Children.Add(CatalogLine(cap, ok));
            return;
        }
        // Par categorie de source : seules les categories ayant au moins une conversion
        var byCat = _engine.Registry.AllCapabilities
            .SelectMany(c => c.From.Select(f => (fmt: FormatCatalog.Get(f), cap: c)))
            .Where(x => x.fmt is not null)
            .GroupBy(x => x.fmt!.Category)
            .OrderBy(g => g.Key);
        foreach (var g in byCat)
        {
            int total = g.Select(x => (x.cap.Key, x.fmt!.Id)).Distinct().Count();
            int available = g.Where(x => _engine.Registry.EngineOk(x.cap)).Select(x => (x.cap.Key, x.fmt!.Id)).Distinct().Count();
            var inner = new StackPanel();
            foreach (var fmtGroup in g.GroupBy(x => x.fmt!.Id).OrderBy(x => x.Key))
            {
                var targets = fmtGroup.Select(x => x.cap).GroupBy(c => (c.Kind, c.To))
                    .Select(t => t.OrderBy(c => _engine.Registry.EngineOk(c) ? 0 : 1).ThenBy(c => c.Priority).First()).ToList();
                var ok = targets.Where(_engine.Registry.EngineOk).Select(Short).Distinct().ToList();
                var missing = targets.Where(c => !_engine.Registry.EngineOk(c)).Select(Short).Distinct().ToList();
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4), FontSize = 12 };
                tb.Inlines.Add(new System.Windows.Documents.Run(FormatCatalog.Get(fmtGroup.Key)!.Label + " → ") { FontWeight = FontWeights.SemiBold });
                tb.Inlines.Add(new System.Windows.Documents.Run(ok.Count > 0 ? string.Join(", ", ok) : "—"));
                if (missing.Count > 0) tb.Inlines.Add(new System.Windows.Documents.Run("   (à installer : " + string.Join(", ", missing) + ")") { Foreground = Ui.Subtle });
                inner.Children.Add(tb);
            }
            var exp = new Expander { Header = $"{FormatCatalog.CategoryLabel(g.Key)}  ({available}/{total} disponibles)", Content = inner, Margin = new Thickness(0, 2, 0, 2) };
            _catalog.Children.Add(exp);
        }
    }

    private static string Short(ConversionCapability c) => c.Kind switch
    {
        ConversionKind.Convert => FormatCatalog.Get(c.To)?.Label ?? c.To,
        _ => c.DisplayLabel,
    };

    private UIElement CatalogLine(ConversionCapability cap, bool ok)
    {
        var from = string.Join(", ", cap.From.Select(f => FormatCatalog.Get(f)?.Label ?? f).Take(6)) + (cap.From.Count > 6 ? "…" : "");
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3), FontSize = 12 };
        tb.Inlines.Add(new System.Windows.Documents.Run(cap.DisplayLabel) { FontWeight = FontWeights.SemiBold });
        tb.Inlines.Add(new System.Windows.Documents.Run($"\ndepuis {from}"));
        var engineName = _engine.Registry.Providers.First(p => p.Id == cap.ProviderId).DisplayName;
        tb.Inlines.Add(new System.Windows.Documents.Run($"\n{engineName}{(ok ? "" : " — non installé")}") { Foreground = ok ? Ui.Subtle : Ui.Warning });
        return tb;
    }

    // ------------------------------------------------------------------ lot

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) { AddDirectory(p, recursive: false); continue; }
            if (!File.Exists(p) || _items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            var d = FormatDetector.Detect(p);
            _items.Add(new BatchItem { Path = p, Format = d.IsExecutable ? null : d.Format, Warning = d.IsExecutable ? "Programme : jamais converti." : d.Warning });
        }
        ItemsChanged();
    }

    private void AddFolder()
    {
        var f = Pickers.Folder("Dossier à ajouter");
        if (f is null) return;
        bool recursive = ConfirmDialog.Ask("Sous-dossiers", "Inclure aussi les fichiers des sous-dossiers ?", "Inclure les sous-dossiers");
        AddDirectory(f, recursive);
        ItemsChanged();
    }

    private void AddDirectory(string dir, bool recursive)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
        var files = Directory.EnumerateFiles(dir, "*", opts).Take(5001).ToList();
        if (files.Count > 5000) Ui.Message("Plus de 5 000 fichiers : seuls les 5 000 premiers sont ajoutés.", image: MessageBoxImage.Warning, owner: this);
        AddFiles(files.Take(5000));
    }

    private void RemoveSelected()
    {
        foreach (var it in _list.SelectedItems.OfType<ListBoxItem>().Select(i => (BatchItem)i.Tag).ToList()) _items.Remove(it);
        ItemsChanged();
    }

    private void Refresh()
    {
        var selected = new HashSet<string>(_list.SelectedItems.OfType<ListBoxItem>().Select(i => ((BatchItem)i.Tag).Path));
        _list.Items.Clear();
        foreach (var it in _items)
        {
            var row = new StackPanel { Margin = new Thickness(2) };
            row.Children.Add(new TextBlock { Text = Path.GetFileName(it.Path), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var status = (it.Format?.Label ?? "non convertible") + (it.Override is not null ? "  ·  réglages individuels" : "");
            if (it.Job is { } j)
            {
                status += "  ·  " + j.StatusLabel + (j.Status == JobStatus.Running && j.Progress is { } p ? $" {p:P0}" : "");
                if (j.Error is not null) status += " — " + j.Error;
            }
            var brush = it.Job?.Status switch { JobStatus.Failed => Ui.Error, JobStatus.Succeeded => Ui.Success, _ => Ui.Subtle };
            row.Children.Add(new TextBlock { Text = status, FontSize = 12, Foreground = brush, TextWrapping = TextWrapping.Wrap });
            if (it.Warning is not null) row.Children.Add(new TextBlock { Text = it.Warning, FontSize = 12, Foreground = Ui.Warning, TextWrapping = TextWrapping.Wrap });
            var lbi = new ListBoxItem { Content = row, Tag = it, IsSelected = selected.Contains(it.Path) };
            _list.Items.Add(lbi);
        }
        _summary.Text = _items.Count == 0 ? "Glissez des fichiers ici ou utilisez « Ajouter »." :
            $"{_items.Count} fichier(s) · {_items.Count(i => i.Job?.Status == JobStatus.Succeeded)} réussi(s) · {_items.Count(i => i.Job?.Status == JobStatus.Failed)} échec(s)";
    }

    /// <summary>La liste des fichiers a change : formats de sortie communs recalcules.</summary>
    private void ItemsChanged()
    {
        Refresh();
        RefreshTargets();
    }

    private void RefreshTargets()
    {
        var current = (_target.SelectedItem as ComboBoxItem)?.Tag as RouteInfo;
        _target.Items.Clear();
        var formats = _items.Where(i => i.Format is not null).Select(i => i.Format!.Id).Distinct().ToList();
        if (formats.Count == 0) { LoadOptions(); return; }
        var routes = _engine.Registry.CommonRoutes(formats).Where(r => !r.Capability.MultiInput).ToList();
        if (_items.Count > 1)
            routes.AddRange(_engine.Registry.AllCapabilities.Where(c => c.MultiInput && formats.All(f => c.From.Contains(f)))
                .Select(c => new RouteInfo(c, _engine.Registry.EngineOk(c), _engine.Registry.MissingEngine(c) is { } m ? EngineCatalog.Get(m).Name : null)));
        foreach (var r in routes)
        {
            var item = new ComboBoxItem { Content = r.Label + (r.Available ? "" : $" — nécessite {r.MissingEngine}"), Tag = r };
            if (!r.Available) item.Foreground = Ui.Subtle;
            _target.Items.Add(item);
            if (current is not null && r.Capability.Key == current.Capability.Key) _target.SelectedItem = item;
        }
        if (_target.SelectedIndex < 0) _target.SelectedItem = _target.Items.OfType<ComboBoxItem>().FirstOrDefault(i => ((RouteInfo)i.Tag).Available);
        if (formats.Count > 1 && routes.Count == 0)
            _summary.Text += "\nCes fichiers n'ont aucune conversion commune : convertissez-les par type.";
    }

    private RouteInfo? Route => (_target.SelectedItem as ComboBoxItem)?.Tag as RouteInfo;

    private Dictionary<string, string> _common = new();

    private BatchItem? SelectedItem => _list.SelectedItems.Count == 1 ? (BatchItem)((ListBoxItem)_list.SelectedItems[0]!).Tag : null;

    private void LoadOptions()
    {
        _loadingOptions = true;
        var r = Route;
        RefreshPresets();
        if (r is null) { _options.Load([], null, false); _loadingOptions = false; return; }
        IReadOnlyDictionary<string, string>? init = _common.Count > 0 ? _common : null;
        if (_individual.IsChecked == true && SelectedItem is { } sel) init = sel.Override ?? (IReadOnlyDictionary<string, string>?)_common;
        _options.Load(r.Capability.Options, init, showAdvanced: true);
        if (!r.Available)
            _options.Children.Insert(0, Ui.Column(Ui.Text($"Nécessite {r.MissingEngine}.", Ui.Warning), Ui.Button("Installer…", () => _openEngines(r.MissingEngine ?? ""))));
        _loadingOptions = false;
    }

    private void OptionsChanged()
    {
        if (_loadingOptions) return;
        if (_individual.IsChecked == true && SelectedItem is { } sel)
        {
            sel.Override = new Dictionary<string, string>(_options.Values);
            Refresh();
        }
        else _common = new Dictionary<string, string>(_options.Values);
    }

    // ------------------------------------------------------------------ profils

    private void RefreshPresets()
    {
        _presets.Items.Clear();
        _presets.Items.Add(new ComboBoxItem { Content = "(profil par défaut)", Tag = null });
        if (Route is { } r)
            foreach (var p in _engine.Presets.For(r.Capability.Key)) _presets.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
        _presets.SelectedIndex = 0;
    }

    private void ApplyPreset()
    {
        if (_loadingOptions || Route is null) return;
        if ((_presets.SelectedItem as ComboBoxItem)?.Tag is ConversionPreset p)
        {
            _common = new Dictionary<string, string>(p.Options);
            _loadingOptions = true;
            _options.Load(Route.Capability.Options, _common, true);
            _loadingOptions = false;
        }
    }

    private void SavePreset()
    {
        if (Route is null) return;
        var name = TextPromptDialog.Ask("Enregistrer un profil", "Nom du profil :", "Mon profil", s => s.Trim().Length == 0 ? "Nom obligatoire." : null);
        if (name is null) return;
        _engine.Presets.Save(new ConversionPreset { Name = name.Trim(), CapabilityKey = Route.Capability.Key, Options = new Dictionary<string, string>(_options.Values) });
        RefreshPresets();
    }

    private void DeletePreset()
    {
        if (Route is null || (_presets.SelectedItem as ComboBoxItem)?.Tag is not ConversionPreset p) return;
        _engine.Presets.Delete(p.Name, Route.Capability.Key);
        RefreshPresets();
    }

    // ------------------------------------------------------------------ lancement

    private void Start()
    {
        var r = Route;
        if (r is null) { Ui.Message("Choisissez un format de sortie.", owner: this); return; }
        if (!r.Available) { _openEngines(r.MissingEngine ?? ""); return; }
        var dest = _dest.Text.Trim();
        if (dest.Length > 0 && !Directory.Exists(dest)) { Ui.Message("Le dossier de destination n'existe pas.", image: MessageBoxImage.Warning, owner: this); return; }
        var todo = _items.Where(i => i.Format is not null && r.Capability.From.Contains(i.Format.Id) && i.Job is not { IsFinished: false }).ToList();
        if (todo.Count == 0) { Ui.Message("Aucun fichier compatible avec cette conversion.", owner: this); return; }
        if (r.Capability.MultiInput)
        {
            var job = _engine.Queue.Enqueue(new ConversionOrder
            {
                Inputs = todo.Select(i => i.Path).ToList(), CapabilityKey = r.Capability.Key, Options = new(_common),
                OutputDir = dest.Length > 0 ? dest : null, Conflicts = _conflicts,
            }, $"{todo.Count} fichiers → {r.Label}");
            foreach (var i in todo) i.Job = job;
        }
        else
        {
            foreach (var i in todo)
            {
                i.Job = _engine.Queue.Enqueue(new ConversionOrder
                {
                    Inputs = [i.Path], CapabilityKey = r.Capability.Key, Options = new(i.Override ?? _common),
                    OutputDir = dest.Length > 0 ? dest : null, Conflicts = _conflicts,
                });
            }
        }
        Refresh();
    }

    private void RetryFailed()
    {
        foreach (var i in _items.Where(i => i.Job?.CanRetry == true)) i.Job = _engine.Queue.Retry(i.Job!);
        Refresh();
    }

    private void OnJob(ConversionJob j)
    {
        if (j is null || _items.All(i => i.Job != j)) return;
        Dispatcher.BeginInvoke(Refresh);
    }
}
