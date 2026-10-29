using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Actions;
using FileWheel.Core.Formats;
using FileWheel.Core.Input;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;
using FileWheel.Core.Util;
using FileWheel.Core.Wheel;
using FileWheel.Windows.SystemIntegration;

namespace FileWheel.App.Ui;

/// <summary>Parametres de FileWheel. Chaque modification est enregistree et appliquee immediatement.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly ConversionEngine _engine;
    private readonly Action<AppSettings> _apply;
    private readonly Func<List<CustomAction>> _loadActions;
    private readonly Action<List<CustomAction>> _saveActions;
    private readonly DispatcherTimer _save;
    // Barre laterale (macOS : reglages Systeme ; Windows 11 : Parametres) + zone de contenu
    private readonly ListBox _nav = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(10, 4, 10, 10) };
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly Dictionary<string, (ListBoxItem item, UIElement page)> _byName = new();

    public SettingsWindow(AppSettings settings, ConversionEngine engine, Action<AppSettings> apply,
        Func<List<CustomAction>> loadActions, Action<List<CustomAction>> saveActions)
    {
        _s = settings;
        _engine = engine;
        _apply = apply;
        _loadActions = loadActions;
        _saveActions = saveActions;
        Title = "FileWheel — Paramètres";
        Width = 920;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.Decorate(this);
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _save.Tick += (_, _) => { _save.Stop(); Persist(); };
        Closed += (_, _) => { if (_save.IsEnabled) Persist(); };

        if (Application.Current?.TryFindResource(Theme.SidebarItemStyle) is Style itemStyle) _nav.ItemContainerStyle = itemStyle;
        ScrollViewer.SetHorizontalScrollBarVisibility(_nav, ScrollBarVisibility.Disabled);
        _nav.SelectionChanged += (_, _) =>
        {
            if (_nav.SelectedItem is ListBoxItem { Tag: string name } && _byName.TryGetValue(name, out var e)) _host.Content = e.page;
        };
        Add("Général", Glyphs.Settings, General());
        Add("Déclenchement", Glyphs.Lightning, Trigger());
        Add("Roue", Glyphs.Convert, Wheel());
        Add("Conversion", Glyphs.Document, Conversion());
        Add("Moteurs", Glyphs.Data, Engines());
        Add("Envoyer vers et favoris", Glyphs.Favorite, Folders());
        Add("Actions personnalisées", Glyphs.Star, Actions());
        Add("Historique", Glyphs.List, History());
        Add("À propos", Glyphs.Info, About());
        _nav.SelectedIndex = 0;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var side = new DockPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 18, 12, 14) };
        if (System.IO.File.Exists(Ui.AppLogoPath))
        {
            try
            {
                var logo = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(Ui.AppLogoPath)), Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0) };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(logo, System.Windows.Media.BitmapScalingMode.HighQuality);
                brand.Children.Add(logo);
            }
            catch { }
        }
        var brandText = Ui.Column(new TextBlock { Text = "FileWheel", FontSize = 16, FontWeight = FontWeights.SemiBold, FontFamily = Theme.DisplayFont }, Ui.Hint("Paramètres"));
        ((TextBlock)brandText.Children[1]).Margin = new Thickness(0);
        brand.Children.Add(brandText);
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);
        side.Children.Add(_nav);
        var sideBorder = new Border { Child = side, BorderThickness = new Thickness(0, 0, 1, 0) };
        sideBorder.SetResourceReference(Border.BackgroundProperty, Theme.SidebarBg);
        sideBorder.SetResourceReference(Border.BorderBrushProperty, Theme.Separator);
        root.Children.Add(sideBorder);
        Grid.SetColumn(_host, 1);
        root.Children.Add(_host);
        Content = root;
    }

    public void ShowTab(string name)
    {
        if (_byName.TryGetValue(name, out var e)) _nav.SelectedItem = e.item;
    }

    private void Add(string name, string glyph, UIElement content)
    {
        if (content is StackPanel page) GroupIntoCards(page);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Icon(glyph, 15);
        icon.SetResourceReference(TextBlock.ForegroundProperty, Theme.Accent);
        icon.Margin = new Thickness(0, 0, 12, 0);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ListBoxItem { Content = row, Tag = name };
        _byName[name] = (item, Ui.Scroll(content));
        _nav.Items.Add(item);
    }

    /// <summary>
    /// Mise en page des reglages macOS / Windows 11 : le titre reste en haut, chaque section (introduite par un
    /// en-tete) devient une carte arrondie, avec une fine separation entre les lignes de reglage.
    /// </summary>
    private static void GroupIntoCards(StackPanel page)
    {
        var children = page.Children.Cast<UIElement>().ToList();
        page.Children.Clear();
        page.MaxWidth = 760;
        page.HorizontalAlignment = HorizontalAlignment.Left;
        StackPanel? group = null;
        void Flush()
        {
            if (group is { Children.Count: > 0 }) page.Children.Add(Ui.Card(group, 14));
            group = null;
        }
        bool first = true;
        foreach (var c in children)
        {
            bool isTitle = first && c is TextBlock;
            first = false;
            bool isHeading = c is TextBlock tb && tb.FontWeight == FontWeights.SemiBold && !isTitle;
            bool isCard = c is FrameworkElement { Tag: "fw.card" };
            if (isTitle || isHeading || isCard)
            {
                Flush();
                page.Children.Add(c);
                continue;
            }
            group ??= new StackPanel();
            bool isHint = c is TextBlock { FontSize: < 12.5 };
            if (group.Children.Count > 0 && !isHint) group.Children.Add(Ui.Separator());
            group.Children.Add(c);
        }
        Flush();
    }

    private void Changed()
    {
        _save.Stop();
        _save.Start();
    }

    private void Persist()
    {
        try
        {
            _s.Normalize();
            SettingsStore.Save(_s);
            _apply(_s);
        }
        catch (Exception ex) { Log.Error("Enregistrement des paramètres", ex); Ui.Message("Paramètres non enregistrés : " + ex.Message, image: MessageBoxImage.Error, owner: this); }
    }

    // ------------------------------------------------------------------ Général

    private UIElement General()
    {
        var p = Ui.Column(Ui.Title("Général"));
        p.Children.Add(Ui.Check("Roue activée", _s.WheelEnabled, v => { _s.WheelEnabled = v; Changed(); }));
        bool startup = false;
        try { startup = Startup.IsEnabled(); } catch { }
        p.Children.Add(Ui.Check("Démarrer FileWheel avec Windows", startup, v =>
        {
            try { Startup.Set(v); _s.StartWithWindows = v; Changed(); }
            catch (Exception ex) { Ui.Message("Impossible de modifier le démarrage automatique : " + ex.Message, image: MessageBoxImage.Warning, owner: this); }
        }, "Ajoute FileWheel aux programmes lancés à l'ouverture de session (sans droits administrateur)."));
        p.Children.Add(Ui.Heading("Journal"));
        p.Children.Add(Ui.Field("Niveau de détail", Ui.Combo([(LogLevel.Info, "Normal"), (LogLevel.Debug, "Détaillé (diagnostic)")], Log.MinimumLevel, v => Log.MinimumLevel = v)));
        p.Children.Add(Ui.Hint("Le journal ne contient ni le contenu des fichiers ni votre nom d'utilisateur (le dossier du profil est masqué)."));
        p.Children.Add(Ui.Row(
            Ui.Button("Ouvrir le dossier des journaux", () => Process.Start("explorer.exe", AppPaths.LogsDir), glyph: Glyphs.Folder),
            Ui.Button("Ouvrir le dossier des paramètres", () => Process.Start("explorer.exe", AppPaths.ConfigDir), glyph: Glyphs.Folder)));
        return p;
    }

    // ------------------------------------------------------------------ Declenchement

    private UIElement Trigger()
    {
        var t = _s.Trigger;
        var p = Ui.Column(Ui.Title("Déclenchement de la roue"));
        p.Children.Add(Ui.Text("Commencez à faire glisser un fichier, puis maintenez la combinaison choisie."));
        var warn = Ui.Text("", Ui.Warning, 12);
        void UpdateWarn() => warn.Text = string.Join("\n", t.Chord.Warnings());
        var ctrl = Ui.Check("Ctrl", t.Ctrl, v => { t.Ctrl = v; UpdateWarn(); Changed(); });
        var alt = Ui.Check("Alt", t.Alt, v => { t.Alt = v; UpdateWarn(); Changed(); });
        var shift = Ui.Check("Maj", t.Shift, v => { t.Shift = v; UpdateWarn(); Changed(); });
        var win = Ui.Check("Win", t.Win, v => { t.Win = v; UpdateWarn(); Changed(); });
        foreach (var c in new[] { ctrl, alt, shift, win }) c.Margin = new Thickness(0, 4, 16, 4);
        var keys = new List<(int, string)> { (0, "(aucune)"), (VK.Space, "Espace"), (VK.Capital, "Verr. Maj") };
        for (int i = 0; i < 12; i++) keys.Add((VK.F1 + i, "F" + (i + 1)));
        for (int c = VK.A; c <= VK.Z; c++) keys.Add((c, ((char)c).ToString()));
        p.Children.Add(Ui.Field("Touches", Ui.Row(ctrl, alt, shift, win)));
        p.Children.Add(Ui.Field("Touche supplémentaire", Ui.Combo(keys, t.Key, v => { t.Key = v; UpdateWarn(); Changed(); }, 140)));
        UpdateWarn();
        p.Children.Add(warn);
        p.Children.Add(Ui.Hint("Combinaison par défaut : Alt. La correspondance est exacte : Ctrl+Alt ne déclenche pas une combinaison « Alt » (évite AltGr)."));

        p.Children.Add(Ui.Heading("Sensibilité"));
        var delayText = Ui.Text($"{t.DelayMs} ms");
        p.Children.Add(Ui.Field("Délai d'apparition", Ui.Row(Ui.Slider(0, 1000, t.DelayMs, v => { t.DelayMs = (int)v; delayText.Text = $"{(int)v} ms"; Changed(); }, 10), delayText)));
        var distText = Ui.Text($"{t.MinDistancePx} px");
        p.Children.Add(Ui.Field("Distance minimale de glisser", Ui.Row(Ui.Slider(2, 100, t.MinDistancePx, v => { t.MinDistancePx = (int)v; distText.Text = $"{(int)v} px"; Changed(); }), distText)));
        p.Children.Add(Ui.Field("Vérification de l'élément", Ui.Segmented(
            [(ItemCheckPolicy.Lenient, "Tolérante"), (ItemCheckPolicy.Strict, "Stricte"), (ItemCheckPolicy.None, "Désactivée")],
            t.ItemCheck, v => { t.ItemCheck = v; Changed(); }),
            "Évite que la roue s'ouvre lors d'une sélection rectangulaire (glisser sur une zone vide)."));
        p.Children.Add(Ui.Hint("Tolérante (recommandé) : la roue s'ouvre si Windows ne répond pas à temps. Stricte : uniquement sur un fichier confirmé."));

        p.Children.Add(Ui.Heading("Où et quand"));
        p.Children.Add(Ui.Check("Dans l'Explorateur de fichiers", t.InExplorer, v => { t.InExplorer = v; Changed(); }));
        p.Children.Add(Ui.Check("Sur le Bureau", t.OnDesktop, v => { t.OnDesktop = v; Changed(); }));
        p.Children.Add(Ui.Check("Pour un seul fichier", t.AllowSingle, v => { t.AllowSingle = v; Changed(); }));
        p.Children.Add(Ui.Check("Pour une sélection multiple", t.AllowMultiple, v => { t.AllowMultiple = v; Changed(); }));
        p.Children.Add(Ui.Check("Fermer la roue si la combinaison est relâchée", t.CloseOnChordRelease, v => { t.CloseOnChordRelease = v; Changed(); },
            "Sinon la roue reste affichée jusqu'au dépôt, ce qui permet de relâcher la touche pendant que vous visez."));
        return p;
    }

    // ------------------------------------------------------------------ Roue

    private UIElement Wheel()
    {
        var w = _s.Wheel;
        var p = Ui.Column(Ui.Title("Apparence de la roue"));
        p.Children.Add(Ui.Field("Disposition", Ui.Segmented([(WheelMenuLayout.Express, "Express"), (WheelMenuLayout.Classic, "Classique")], w.Layout, v => { w.Layout = v; Changed(); })));
        p.Children.Add(Ui.Hint("Express : formats (DOCX, JPG…) et outils image directement dans la roue, actions de fichier dans « Fichier ». Classique : Convertir, Copier, Déplacer, Renommer… au premier niveau."));
        p.Children.Add(Ui.Check("Libellés sous les icônes", w.ShowCaptions, v => { w.ShowCaptions = v; Changed(); }));
        p.Children.Add(Ui.Heading("Taille et position"));
        var sizeText = Ui.Text($"{w.DiameterDip} px");
        p.Children.Add(Ui.Field("Taille", Ui.Row(Ui.Slider(200, 600, w.DiameterDip, v => { w.DiameterDip = (int)v; sizeText.Text = $"{(int)v} px"; Changed(); }, 10), sizeText)));
        var ox = Ui.Text($"{w.OffsetXDip}");
        p.Children.Add(Ui.Field("Décalage horizontal", Ui.Row(Ui.Slider(-300, 300, w.OffsetXDip, v => { w.OffsetXDip = (int)v; ox.Text = $"{(int)v}"; Changed(); }, 5), ox)));
        var oy = Ui.Text($"{w.OffsetYDip}");
        p.Children.Add(Ui.Field("Décalage vertical", Ui.Row(Ui.Slider(-300, 300, w.OffsetYDip, v => { w.OffsetYDip = (int)v; oy.Text = $"{(int)v}"; Changed(); }, 5), oy)));
        var op = Ui.Text($"{w.Opacity:P0}");
        p.Children.Add(Ui.Field("Opacité du fond", Ui.Row(Ui.Slider(0.55, 1, w.Opacity, v => { w.Opacity = v; op.Text = $"{v:P0}"; Changed(); }, 0.05), op)));
        p.Children.Add(Ui.Check("Verre dépoli (flou de l'arrière-plan) — expérimental", w.Blur, v => { w.Blur = v; Changed(); },
            "Utilise le flou de Windows 10/11. Si l'effet s'affiche mal sur votre configuration, désactivez-le : la roue reste lisible avec son fond semi-opaque."));
        p.Children.Add(Ui.Check("Animations", w.Animations, v => { w.Animations = v; Changed(); }));
        var dw = Ui.Text($"{w.SubmenuDwellMs} ms");
        p.Children.Add(Ui.Field("Ouverture des sous-menus au survol", Ui.Row(Ui.Slider(150, 1500, w.SubmenuDwellMs, v => { w.SubmenuDwellMs = (int)v; dw.Text = $"{(int)v} ms"; Changed(); }, 10), dw)));
        p.Children.Add(Ui.Check("Demander confirmation avant de placer dans la Corbeille", w.ConfirmRecycle, v => { w.ConfirmRecycle = v; Changed(); }));
        p.Children.Add(Ui.Hint("La suppression définitive demande toujours une confirmation explicite (bouton par défaut : Annuler)."));
        return p;
    }

    // ------------------------------------------------------------------ Conversion

    private UIElement Conversion()
    {
        var c = _s.Conversion;
        var p = Ui.Column(Ui.Title("Conversion"));
        var dir = new TextBox { Text = c.DefaultOutputDir, MinWidth = 320 };
        dir.TextChanged += (_, _) => { c.DefaultOutputDir = dir.Text.Trim(); Changed(); };
        p.Children.Add(Ui.Field("Dossier de destination par défaut", Ui.Row(dir, Ui.Button("…", () => { var f = Pickers.Folder("Dossier par défaut"); if (f is not null) dir.Text = f; }))));
        p.Children.Add(Ui.Hint("Vide = à côté du fichier d'origine."));
        var suffix = new TextBox { Text = c.Suffix, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        suffix.TextChanged += (_, _) => { c.Suffix = suffix.Text; Changed(); };
        p.Children.Add(Ui.Field("Suffixe ajouté au nom", suffix, "Exemple : « _converti » donne photo_converti.png"));
        p.Children.Add(Ui.Field("Si le fichier existe déjà", Ui.Combo([(ConflictPolicy.Rename, "Garder les deux (« (2) »)"), (ConflictPolicy.Skip, "Ignorer"), (ConflictPolicy.Overwrite, "Remplacer")], c.Conflicts, v => { c.Conflicts = v; Changed(); })));
        p.Children.Add(Ui.Hint("Le fichier d'origine n'est jamais modifié, quel que soit ce réglage."));
        p.Children.Add(Ui.Field("Conversions simultanées", Ui.Combo(Enumerable.Range(1, 8).Select(i => (i, i.ToString())), c.MaxConcurrent, v => { c.MaxConcurrent = v; Changed(); }, 80)));
        p.Children.Add(Ui.Field("Priorité des conversions", Ui.Combo([(TaskPriority.Normal, "Normale"), (TaskPriority.BelowNormal, "Réduite (recommandé)"), (TaskPriority.Idle, "Basse (inactivité)")], c.Priority, v => { c.Priority = v; Changed(); })));
        p.Children.Add(Ui.Field("Qualité des images par défaut", Ui.Combo(new[] { 60, 70, 80, 85, 90, 95, 100 }.Select(q => (q, $"{q} %")), c.DefaultImageQuality, v => { c.DefaultImageQuality = v; Changed(); }, 100)));
        p.Children.Add(Ui.Heading("Limites et sécurité"));
        p.Children.Add(Ui.Field("Taille maximale d'un fichier source", Ui.Combo(new long[] { 0, 1024, 4096, 16384, 65536 }.Select(v => (v, v == 0 ? "Illimitée" : $"{v / 1024} Gio")), c.MaxInputMiB, v => { c.MaxInputMiB = v; Changed(); }, 140)));
        p.Children.Add(Ui.Field("Espace libre à préserver", Ui.Combo(new long[] { 256, 512, 1024, 4096, 10240 }.Select(v => (v, v >= 1024 ? $"{v / 1024} Gio" : $"{v} Mio")), c.MinFreeSpaceMiB, v => { c.MinFreeSpaceMiB = v; Changed(); }, 140)));
        p.Children.Add(Ui.Field("Taille maximale d'une extraction", Ui.Combo(new long[] { 1, 5, 20, 100, 500 }.Select(v => (v, $"{v} Gio")), c.ExtractMaxGiB, v => { c.ExtractMaxGiB = v; Changed(); }, 140)));
        p.Children.Add(Ui.Field("Taux de compression maximal", Ui.Combo(new[] { 100, 250, 1000, 10000 }.Select(v => (v, $"× {v}")), c.ExtractMaxRatio, v => { c.ExtractMaxRatio = v; Changed(); }, 140),
            "Au-delà, une archive est considérée comme une « bombe de décompression » et l'extraction s'arrête."));
        p.Children.Add(Ui.Heading("Fichiers temporaires"));
        p.Children.Add(Ui.Field("Supprimer les temporaires plus vieux que", Ui.Combo(new[] { 1, 6, 24, 72, 168 }.Select(h => (h, h < 24 ? $"{h} h" : $"{h / 24} jour(s)")), c.TempMaxAgeHours, v => { c.TempMaxAgeHours = v; Changed(); }, 140)));
        p.Children.Add(Ui.Button("Nettoyer maintenant", () =>
        {
            int n = OutputPlanner.CleanupStale(AppPaths.TempDir, TimeSpan.Zero);
            foreach (var d in Directory.EnumerateDirectories(AppPaths.TempDir)) { try { Directory.Delete(d, true); n++; } catch { } }
            Ui.Message($"{n} élément(s) temporaire(s) supprimé(s).", owner: this);
        }, glyph: Glyphs.Recycle));
        return p;
    }

    // ------------------------------------------------------------------ Moteurs

    private StackPanel? _enginesPanel;

    private UIElement Engines()
    {
        _enginesPanel = new StackPanel();
        RenderEngines();
        return _enginesPanel;
    }

    private void RenderEngines()
    {
        var p = _enginesPanel!;
        p.Children.Clear();
        p.Children.Add(Ui.Title("Moteurs de conversion"));
        p.Children.Add(Ui.Text("FileWheel convertit de nombreux formats sans rien installer. Les moteurs ci-dessous ajoutent des formats ; ils ne sont téléchargés que si vous le demandez, depuis leur source officielle, avec vérification d'empreinte."));
        var deps = _engine.Dependencies;
        foreach (var def in EngineCatalog.All.Where(e => e.Id != EngineIds.FFprobe))
        {
            var st = deps.Status(def.Id);
            var col = new StackPanel();
            var head = Ui.Row(Ui.Icon(st.Available ? Glyphs.Check : "", 16, st.Available ? Ui.Success : Ui.Warning),
                new TextBlock { Text = "  " + def.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            col.Children.Add(head);
            col.Children.Add(Ui.Text(def.Purpose, null, 12));
            var where = st.Available ? $"Trouvé : {st.Path} ({SourceLabel(st.Source)})" : "Non installé.";
            if (deps.InstalledInfo(def.Id) is { } inst && st.Source == EngineSource.FileWheel) where += $" — version {inst.Version}";
            col.Children.Add(Ui.Text(where, Ui.Subtle, 12));
            col.Children.Add(Ui.Hint("Licence : " + def.License));
            var row = Ui.Row();
            if (def.Method == InstallMethod.GitHubRelease && st.Source != EngineSource.UserPath)
                row.Children.Add(Ui.Button(st.Source == EngineSource.FileWheel ? "Mettre à jour" : "Installer", () => InstallGitHub(def), accent: !st.Available));
            if (def.WingetId is not null && !st.Available && def.Method != InstallMethod.GitHubRelease)
                row.Children.Add(Ui.Button("Installer avec winget", () => InstallWinget(def), accent: true));
            row.Children.Add(Ui.Button("Choisir l'emplacement…", () =>
            {
                var f = Pickers.File($"Emplacement de {def.Name}", $"{def.Name}|{string.Join(';', def.ExeNames.Where(n => n.Contains('.')))}|Tous les fichiers|*.*");
                if (f is null) return;
                deps.SetUserPath(def.Id, f);
                if (def.Id == EngineIds.FFmpeg)
                {
                    var probe = Path.Combine(Path.GetDirectoryName(f)!, "ffprobe.exe");
                    if (File.Exists(probe)) deps.SetUserPath(EngineIds.FFprobe, probe);
                }
                RenderEngines();
            }));
            if (st.Source == EngineSource.UserPath) row.Children.Add(Ui.Button("Oublier l'emplacement", () => { deps.SetUserPath(def.Id, null); RenderEngines(); }));
            if (st.Source == EngineSource.FileWheel)
                row.Children.Add(Ui.Button("Supprimer", () =>
                {
                    if (!ConfirmDialog.Ask("Supprimer le moteur", $"Supprimer {def.Name} installé par FileWheel ?", "Supprimer", destructive: true)) return;
                    try { deps.Uninstall(def.Id); } catch (Exception ex) { Ui.Message(ex.Message, image: MessageBoxImage.Warning, owner: this); }
                    RenderEngines();
                }));
            row.Children.Add(Ui.Button("Site officiel", () => Process.Start(new ProcessStartInfo(def.Homepage) { UseShellExecute = true })));
            col.Children.Add(row);
            p.Children.Add(Ui.Card(col, 14));
        }
        p.Children.Add(Ui.Button("Rechercher à nouveau les moteurs installés", () => { deps.Refresh(); RenderEngines(); }, glyph: ""));
    }

    private static string SourceLabel(EngineSource s) => s switch
    {
        EngineSource.UserPath => "emplacement choisi",
        EngineSource.FileWheel => "installé par FileWheel",
        EngineSource.Installed => "installé sur le PC",
        EngineSource.SystemPath => "trouvé dans le PATH",
        _ => "",
    };

    private async void InstallGitHub(EngineDefinition def)
    {
        InstallOffer offer;
        try { offer = await _engine.Dependencies.PrepareGitHubInstallAsync(def.Id, CancellationToken.None); }
        catch (Exception ex) { Ui.Message(ConversionManager.Translate(ex).Message, image: MessageBoxImage.Warning, owner: this); return; }
        if (!new EngineConsentDialog(offer).Run()) return;
        var progress = new ProgressDialog($"Installation de {def.Name}");
        progress.Show();
        try
        {
            if (def.Id == EngineIds.ImageMagick && offer.AssetName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && !_engine.Dependencies.IsAvailable(EngineIds.SevenZip))
                throw new ConversionException(ConversionErrorKind.EngineMissing, "Ce paquet ImageMagick est au format 7Z : installez d'abord le moteur 7-Zip (même page).");
            await _engine.Dependencies.InstallAsync(offer, new Progress<ConversionProgress>(pr => progress.Update(pr.Fraction, pr.Stage)), progress.Token);
            progress.Close();
            Ui.Message($"{def.Name} est installé.", owner: this);
        }
        catch (OperationCanceledException) { progress.Close(); }
        catch (Exception ex)
        {
            progress.Close();
            Ui.Message("Installation impossible : " + ConversionManager.Translate(ex).Message, image: MessageBoxImage.Error, owner: this);
        }
        RenderEngines();
    }

    private async void InstallWinget(EngineDefinition def)
    {
        if (!ConfirmDialog.Ask($"Installer {def.Name}", $"{def.Name} sera installé par winget (gestionnaire de paquets de Windows), depuis la source officielle « winget ». Une fenêtre d'installation ou une demande d'autorisation Windows peut apparaître.\n\nLicence : {def.License}", "Installer"))
            return;
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(winget)) { Ui.Message("winget est introuvable sur ce PC. Installez « App Installer » depuis le Microsoft Store, ou installez le logiciel depuis son site officiel puis indiquez son emplacement.", image: MessageBoxImage.Warning, owner: this); return; }
        var progress = new ProgressDialog($"Installation de {def.Name} (winget)");
        progress.Show();
        try
        {
            var r = await ProcessRunner.RunAsync(winget, DependencyManager.WingetArguments(def), progress.Token, timeout: TimeSpan.FromMinutes(30));
            progress.Close();
            _engine.Dependencies.Refresh();
            Ui.Message(_engine.Dependencies.IsAvailable(def.Id) ? $"{def.Name} est installé." : $"winget a terminé (code {r.ExitCode}) mais {def.Name} n'a pas été trouvé. Indiquez son emplacement manuellement.", owner: this);
        }
        catch (Exception ex) { progress.Close(); Ui.Message(ConversionManager.Translate(ex).Message, image: MessageBoxImage.Error, owner: this); }
        RenderEngines();
    }

    // ------------------------------------------------------------------ Dossiers

    private UIElement Folders()
    {
        var p = Ui.Column(Ui.Title("Envoyer vers et favoris"));
        p.Children.Add(Ui.Text("Emplacements proposés dans « Envoyer vers » (copie) et « Déplacer vers » de la roue."));
        p.Children.Add(FolderList(_s.SendToFolders, 20));
        p.Children.Add(Ui.Heading("Favoris"));
        p.Children.Add(Ui.Text("Accessibles depuis l'icône de notification. Ajoutez-en depuis la roue (Plus › Ajouter aux favoris)."));
        p.Children.Add(FolderList(_s.Favorites, 50, allowFiles: true));
        return p;
    }

    private UIElement FolderList(List<string> list, int max, bool allowFiles = false)
    {
        var box = new ListBox { Height = 160, Margin = new Thickness(0, 6, 0, 6) };
        void Fill() { box.Items.Clear(); foreach (var f in list) box.Items.Add(f); }
        Fill();
        var add = Ui.Button("Ajouter un dossier…", () =>
        {
            if (list.Count >= max) return;
            var f = Pickers.Folder("Ajouter un dossier");
            if (f is not null && !list.Contains(f, StringComparer.OrdinalIgnoreCase)) { list.Add(f); Fill(); Changed(); }
        }, glyph: Glyphs.Add);
        var remove = Ui.Button("Retirer", () => { if (box.SelectedItem is string s) { list.Remove(s); Fill(); Changed(); } }, glyph: Glyphs.Cancel);
        var up = Ui.Button("Monter", () =>
        {
            if (box.SelectedItem is string s && list.IndexOf(s) is > 0 and var i) { list.RemoveAt(i); list.Insert(i - 1, s); Fill(); box.SelectedIndex = i - 1; Changed(); }
        });
        _ = allowFiles;
        return Ui.Column(box, Ui.Row(add, remove, up));
    }

    // ------------------------------------------------------------------ Actions personnalisees

    private UIElement Actions()
    {
        var p = Ui.Column(Ui.Title("Actions personnalisées"));
        p.Children.Add(Ui.Text("Ajoutez vos propres actions à la roue (Plus › Mes actions) : lancer un programme, copier ou déplacer vers un dossier fixe, convertir avec des réglages enregistrés."));
        p.Children.Add(Ui.Hint("Sécurité : le programme est choisi par vous et son empreinte est vérifiée à chaque lancement ; les arguments sont transmis un par un, sans interpréteur de commandes. Aucune action n'est jamais importée depuis un fichier glissé."));
        var list = new ListBox { Height = 200, Margin = new Thickness(0, 8, 0, 8) };
        var actions = _loadActions();
        void Fill() { list.Items.Clear(); foreach (var a in actions.OrderBy(a => a.Order)) list.Items.Add(new ListBoxItem { Content = $"{a.Name}  —  {TypeLabel(a.Type)}{(a.Placement == CustomActionPlacement.Root ? " (roue principale)" : "")}", Tag = a }); }
        Fill();
        p.Children.Add(list);
        p.Children.Add(Ui.Row(
            Ui.Button("Nouvelle…", () => { var a = new CustomAction { Order = actions.Count }; if (new CustomActionDialog(a, _engine).Run()) { actions.Add(a); _saveActions(actions); Fill(); } }, accent: true, glyph: Glyphs.Add),
            Ui.Button("Modifier…", () => { if ((list.SelectedItem as ListBoxItem)?.Tag is CustomAction a && new CustomActionDialog(a, _engine).Run()) { _saveActions(actions); Fill(); } }, glyph: Glyphs.Rename),
            Ui.Button("Supprimer", () =>
            {
                if ((list.SelectedItem as ListBoxItem)?.Tag is not CustomAction a) return;
                if (!ConfirmDialog.Ask("Supprimer l'action", $"Supprimer « {a.Name} » ?", "Supprimer", destructive: true)) return;
                actions.Remove(a);
                _saveActions(actions);
                Fill();
            }, glyph: Glyphs.Recycle)));
        return p;
    }

    public static string TypeLabel(CustomActionType t) => t switch
    {
        CustomActionType.RunProgram => "lancer un programme",
        CustomActionType.CopyToFolder => "copier vers un dossier",
        CustomActionType.MoveToFolder => "déplacer vers un dossier",
        CustomActionType.ConvertTo => "convertir",
        _ => "",
    };

    // ------------------------------------------------------------------ Historique

    private UIElement History()
    {
        var h = _engine.History;
        var p = Ui.Column(Ui.Title("Historique des conversions"));
        p.Children.Add(Ui.Check("Conserver l'historique", _s.Conversion.HistoryEnabled, v => { _s.Conversion.HistoryEnabled = v; Changed(); }));
        p.Children.Add(Ui.Hint("Seuls les noms de fichiers, formats, dates, durées et résultats sont enregistrés — jamais le contenu des fichiers."));
        var search = new TextBox { MinWidth = 260 };
        var list = new ListBox { Height = 360, Margin = new Thickness(0, 8, 0, 8) };
        void Fill()
        {
            list.Items.Clear();
            foreach (var e in h.Search(search.Text).Take(500))
            {
                var txt = $"{e.Date:g}  ·  {e.FileName}  ·  {e.SourceFormat.ToUpperInvariant()} → {e.TargetFormat.ToUpperInvariant()}  ·  {e.Result} ({e.DurationSeconds:0.#} s)";
                if (e.Error is not null) txt += "\n" + e.Error;
                list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = txt, TextWrapping = TextWrapping.Wrap, Foreground = e.Result == "Échec" ? Ui.Error : null }, Tag = e });
            }
        }
        search.TextChanged += (_, _) => Fill();
        h.Changed += () => Dispatcher.BeginInvoke(Fill);
        Fill();
        p.Children.Add(Ui.Field("Rechercher", search, labelWidth: 100));
        p.Children.Add(list);
        HistoryEntry? Sel() => (list.SelectedItem as ListBoxItem)?.Tag as HistoryEntry;
        p.Children.Add(Ui.Row(
            Ui.Button("Ouvrir le dossier du résultat", () => { if (Sel()?.Outputs.FirstOrDefault() is { } o && (File.Exists(o) || Directory.Exists(o))) FileWheel.Windows.Shell.ShellOps.RevealInExplorer([o]); }, glyph: Glyphs.Folder),
            Ui.Button("Relancer", () =>
            {
                if (Sel() is not { } e) return;
                if (e.Inputs.Any(i => !File.Exists(i))) { Ui.Message("Le fichier d'origine n'existe plus.", image: MessageBoxImage.Warning, owner: this); return; }
                if (_engine.Registry.ByKey(e.CapabilityKey) is null) { Ui.Message("Cette conversion n'est plus disponible avec les moteurs actuels.", image: MessageBoxImage.Warning, owner: this); return; }
                _engine.Queue.Enqueue(new ConversionOrder { Inputs = e.Inputs, CapabilityKey = e.CapabilityKey, Options = new(e.Options) });
            }, glyph: ""),
            Ui.Button("Supprimer l'entrée", () => { if (Sel() is { } e) h.Remove(e.Id); }, glyph: Glyphs.Cancel),
            Ui.Button("Vider l'historique", () => { if (ConfirmDialog.Ask("Vider l'historique", "Effacer tout l'historique des conversions ?", "Vider", destructive: true)) h.Clear(); }, glyph: Glyphs.Recycle)));
        return p;
    }

    // ------------------------------------------------------------------ A propos

    private UIElement About()
    {
        var p = Ui.Column(Ui.Title("À propos de FileWheel"));
        var v = typeof(SettingsWindow).Assembly.GetName().Version;
        p.Children.Add(Ui.Text($"FileWheel {v?.ToString(3)} — roue d'actions et convertisseur de fichiers pour Windows 10 et 11."));
        p.Children.Add(Ui.Text("Code de FileWheel : licence MIT. Aucune publicité, aucune télémétrie, aucun envoi de fichier sur Internet : toutes les conversions sont locales."));
        p.Children.Add(Ui.Heading("Moteurs externes (facultatifs) et licences"));
        foreach (var e in EngineCatalog.All.Where(e => e.Id != EngineIds.FFprobe))
            p.Children.Add(Ui.Text($"• {e.Name} : {e.License}", null, 12));
        p.Children.Add(Ui.Hint("Les moteurs sont des programmes distincts appelés par FileWheel ; ils ne sont pas inclus dans l'installeur et ne sont téléchargés qu'à votre demande."));
        var docs = Path.Combine(AppContext.BaseDirectory, "docs");
        if (Directory.Exists(docs)) p.Children.Add(Ui.Button("Ouvrir la documentation", () => Process.Start("explorer.exe", docs), glyph: Glyphs.Book));
        p.Children.Add(Ui.Hint("Journal du jour : " + Log.Redact(Log.CurrentLogFile)));
        return p;
    }
}

/// <summary>Petite fenetre de progression annulable (installation de moteur...).</summary>
public sealed class ProgressDialog : Window
{
    private readonly ProgressBar _bar = new() { Height = 6, Minimum = 0, Maximum = 1, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 6) };
    private readonly TextBlock _stage = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource _cts = new();
    public CancellationToken Token => _cts.Token;

    public ProgressDialog(string title)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.Decorate(this);
        var cancel = Ui.Button("Annuler", () => _cts.Cancel());
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        Content = new StackPanel { Margin = new Thickness(20), Children = { _stage, _bar, cancel } };
    }

    public void Update(double? fraction, string stage)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _stage.Text = stage;
            _bar.IsIndeterminate = fraction is null;
            if (fraction is { } f) _bar.Value = f;
        });
    }
}
