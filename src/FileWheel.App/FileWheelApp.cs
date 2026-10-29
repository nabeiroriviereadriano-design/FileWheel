using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FileWheel.App.Ui;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Core.Actions;
using FileWheel.Core.Input;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;
using FileWheel.Core.Util;
using FileWheel.Core.Wheel;
using FileWheel.Windows.Imaging;
using FileWheel.Windows.Input;
using FileWheel.Windows.SystemIntegration;
using FileWheel.Windows.Tray;
using AutoStart = FileWheel.Windows.SystemIntegration.Startup;

namespace FileWheel.App;

/// <summary>
/// Application residente : icone de notification, crochets, roue, file de conversions.
/// Tout ce qui touche a l'interface passe par le Dispatcher WPF ; les crochets vivent sur leur propre thread.
/// </summary>
public sealed class FileWheelApp : Application
{
    private readonly bool _background;
    private readonly bool _afterSetup;
    private InputHookService? _hooks;
    private WheelWindow? _wheel;
    private TrayIcon? _tray;
    private TasksWindow? _tasks;
    private SettingsWindow? _settingsWindow;
    private ConverterWindow? _converter;
    private ActionExecutor? _executor;
    private string? _lastOutput;
    private readonly HashSet<string> _watched = new();
    private DispatcherTimer? _cleanupTimer;
    // Icone de la zone de notification (monochrome, etat d'activite)
    private DispatcherTimer? _trayTimer;
    private TrayBadge _badge;
    private string _trayKey = "";
    private int _trayPhase;

    public AppSettings Settings { get; private set; } = new();
    public List<CustomAction> CustomActions { get; private set; } = new();
    public ConversionEngine Engine { get; private set; } = null!;
    public OperationsCenter Operations { get; } = new();

    public FileWheelApp(bool background, bool afterSetup = false)
    {
        _background = background;
        _afterSetup = afterSetup;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Theme Fluent de WPF (.NET 9+) : aspect Windows 11, clair/sombre selon le systeme.
        ThemeMode = System.Windows.ThemeMode.System;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.Install(this); // charte hybride Windows 11 + macOS (avant toute fenetre)
        bool firstRun = !File.Exists(AppPaths.SettingsFile);
        Settings = SettingsStore.Load();
        CustomActions = CustomActionStore.Load();

        Engine = new ConversionEngine(() => Settings.Conversion, [new WicImageProvider()]);
        Engine.Queue.JobChanged += job => Dispatcher.BeginInvoke(() => OnJobChanged(job));
        _executor = new ActionExecutor(this);

        CleanupTemp();
        _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _cleanupTimer.Tick += (_, _) => CleanupTemp();
        _cleanupTimer.Start();

        // Roue
        _wheel = new WheelWindow
        {
            Settings = () => Settings,
            MenuFactory = files => WheelMenuBuilder.Build(files.Select(f => SelectedEntry.FromPath(f, detectContent: files.Count <= 20)).ToList(),
                Settings, Engine.Registry, CustomActions),
        };
        _wheel.ActionChosen += (item, files) => _executor.Execute(item, files);
        _wheel.Dismissed += () =>
        {
            if (_hooks is null) return;
            _hooks.WheelVisible = false;
            _hooks.NotifyWheelClosed();
        };

        // Crochets
        _hooks = new InputHookService { Probe = new ItemProbe() };
        _hooks.Gesture += o => Dispatcher.BeginInvoke(() => OnGesture(o));
        _hooks.EscapeWhileVisible += () => Dispatcher.BeginInvoke(() => _wheel.EscapePressed());
        _hooks.MouseDownWhileVisible += (x, y) => Dispatcher.BeginInvoke(() => _wheel.OnGlobalMouseDown(x, y));
        _hooks.Start(Settings.Trigger.ToGestureOptions());
        _hooks.SetEnabled(Settings.WheelEnabled);

        // Zone de notification
        _tray = new TrayIcon(Ui.Ui.AppIconPath, TrayTooltip()) { MenuProvider = BuildTrayMenu };
        _tray.MenuCommand += OnTrayCommand;
        _tray.Activated += () => { ClearBadge(); OpenTasks(); };
        _tray.BalloonClicked += () =>
        {
            if (_lastOutput is not null && (File.Exists(_lastOutput) || Directory.Exists(_lastOutput)))
                FileWheel.Windows.Shell.ShellOps.RevealInExplorer([_lastOutput]);
            else OpenTasks();
        };

        _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _trayTimer.Tick += (_, _) => { _trayPhase++; UpdateTrayIcon(); };
        Operations.Changed += op => Dispatcher.BeginInvoke(() =>
        {
            if (op is { IsFinished: true }) SetBadge(op.Status == OperationStatus.Failed ? TrayBadge.Error : op.Status == OperationStatus.Succeeded ? TrayBadge.Success : TrayBadge.None);
            UpdateTrayIcon();
        });
        Theme.SystemAppearanceChanged += () => { _trayKey = ""; UpdateTrayIcon(); };
        UpdateTrayIcon();

        Log.Info($"FileWheel {AppInfo.Version} démarré ({(_background ? "arrière-plan" : "interactif")}).");

        if (firstRun)
        {
            // Apres FileWheel-Setup, le demarrage automatique a deja ete choisi dans l'assistant.
            var dlg = new FirstRunDialog(Settings.Trigger.Chord.ToString(), askStartup: !_afterSetup);
            if (dlg.Run() && !_afterSetup)
            {
                try { AutoStart.Set(dlg.StartWithWindows); Settings.StartWithWindows = dlg.StartWithWindows; }
                catch (Exception ex) { Log.Error("Démarrage automatique", ex); }
            }
            SaveSettings();
        }
        else if (_afterSetup)
        {
            _tray.ShowBalloon($"FileWheel {AppInfo.Version} installé", "Mise à jour terminée. Vos réglages sont conservés.");
        }
        else if (!_background)
        {
            _tray.ShowBalloon("FileWheel est actif", $"Glissez un fichier et maintenez {Settings.Trigger.Chord} pour ouvrir la roue.");
        }
    }

    // ------------------------------------------------------------------ geste

    private void OnGesture(GestureOutput o)
    {
        if (_wheel is null || _hooks is null) return;
        switch (o.Action)
        {
            case GestureAction.ShowWheel:
                try
                {
                    _wheel.ShowAt(o.X, o.Y);
                    _hooks.WheelVisible = true;
                }
                catch (Exception ex)
                {
                    Log.Error("Affichage de la roue", ex);
                    _hooks.WheelVisible = false;
                    _hooks.NotifyWheelClosed();
                }
                break;
            case GestureAction.HideWheel:
                switch (o.Reason)
                {
                    case HideReason.MouseReleased: _wheel.OnMouseReleasedGlobally(); break;
                    case HideReason.Escape: _wheel.EscapePressed(); break;
                    default: _wheel.Dismiss(o.Reason.ToString()); break;
                }
                break;
        }
    }

    // ------------------------------------------------------------------ zone de notification

    private string TrayTooltip() => Settings.WheelEnabled
        ? $"FileWheel — actif ({Settings.Trigger.Chord} pendant un glisser)"
        : "FileWheel — roue désactivée";

    // ------------------------------------------------------------------ icone de notification

    /// <summary>
    /// Redessine l'icone si son etat a change : theme de la barre des taches, roue desactivee,
    /// progression des taches en cours, pastille de fin.
    /// </summary>
    private void UpdateTrayIcon()
    {
        if (_tray is null) return;
        var jobs = Engine.Queue.Jobs.Where(j => !j.IsFinished).ToList();
        var ops = Operations.All.Where(o => !o.IsFinished).ToList();
        bool busy = jobs.Count + ops.Count > 0;
        double? progress = null;
        if (busy)
        {
            // Une tache en attente compte pour 0 ; une tache en cours sans progression connue rend l'anneau « anime ».
            var parts = jobs.Select(j => j.Status == JobStatus.Pending ? 0 : j.Progress).Concat(ops.Select(o => o.Progress)).ToList();
            if (parts.All(p => p is not null)) progress = parts.Average(p => p!.Value);
        }
        if (busy && progress is null) { if (!_trayTimer!.IsEnabled) _trayTimer.Start(); }
        else _trayTimer?.Stop();

        var state = new TrayState(WindowsTheme.SystemUsesLightTheme(), !Settings.WheelEnabled, progress, busy, _trayPhase, busy ? TrayBadge.None : _badge);
        if (state.Key == _trayKey) return;
        _trayKey = state.Key;
        try
        {
            int size = TrayIcon.IconSize;
            var (png, bgra) = TrayGlyph.Render(state, size);
            _tray.SetIcon(TrayIcon.CreateIcon(png, bgra, size));
        }
        catch (Exception ex) { Log.Warn("Icône de notification : " + ex.Message); }
    }

    private void SetBadge(TrayBadge b)
    {
        if (b == TrayBadge.None) return;
        if (_badge == TrayBadge.Error && b == TrayBadge.Success) return; // un echec reste signale jusqu'au clic
        _badge = b;
    }

    private void ClearBadge()
    {
        if (_badge == TrayBadge.None) return;
        _badge = TrayBadge.None;
        UpdateTrayIcon();
    }

    private List<TrayMenuItem> BuildTrayMenu()
    {
        ClearBadge();
        bool startup = false;
        try { startup = AutoStart.IsEnabled(); } catch { }
        int active = Engine.Queue.ActiveCount + Operations.All.Count(o => !o.IsFinished);
        var menu = new List<TrayMenuItem>
        {
            new() { Id = "tasks", Text = active > 0 ? $"Tâches en cours ({active})" : "Tâches et historique", IsDefault = true },
            new() { Id = "converter", Text = "Convertisseur…" },
            TrayMenuItem.Sep(),
            new() { Id = "toggle", Text = "Roue activée", Checked = Settings.WheelEnabled },
            new() { Id = "startup", Text = "Lancer au démarrage de Windows", Checked = startup },
        };
        if (Settings.Favorites.Count > 0)
        {
            var fav = new TrayMenuItem { Id = "favorites", Text = "Favoris" };
            for (int i = 0; i < Settings.Favorites.Count; i++)
            {
                var path = Settings.Favorites[i];
                bool exists = File.Exists(path) || Directory.Exists(path);
                fav.Children.Add(new TrayMenuItem { Id = "fav:" + i, Text = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, Enabled = exists });
            }
            menu.Add(fav);
        }
        menu.Add(TrayMenuItem.Sep());
        menu.Add(new() { Id = "settings", Text = "Paramètres…" });
        var diag = new TrayMenuItem { Id = "diagnostic", Text = "Dépannage" };
        diag.Children.Add(new() { Id = "testwheel", Text = "Tester l'affichage de la roue" });
        diag.Children.Add(new() { Id = "diagmode", Text = "Mode diagnostic (journal détaillé)", Checked = _hooks?.Diagnostic == true });
        diag.Children.Add(new() { Id = "logs", Text = "Ouvrir le dossier des journaux" });
        menu.Add(diag);
        menu.Add(TrayMenuItem.Sep());
        menu.Add(new() { Id = "quit", Text = "Quitter FileWheel" });
        return menu;
    }

    private void OnTrayCommand(string id)
    {
        try
        {
            switch (id)
            {
                case "tasks": OpenTasks(); break;
                case "converter": OpenConverter([]); break;
                case "toggle":
                    Settings.WheelEnabled = !Settings.WheelEnabled;
                    SaveSettings();
                    ApplySettings(Settings);
                    break;
                case "startup":
                    bool now = AutoStart.IsEnabled();
                    AutoStart.Set(!now);
                    Settings.StartWithWindows = !now;
                    SaveSettings();
                    break;
                case "settings": OpenSettings(null); break;
                case "logs": Process.Start(new ProcessStartInfo(AppPaths.LogsDir) { UseShellExecute = true }); break;
                case "testwheel":
                {
                    var (cx, cy) = FileWheel.Windows.Display.Monitors.Cursor();
                    _wheel?.ShowTest(cx, cy);
                    if (_hooks is not null) _hooks.WheelVisible = true;
                    Log.Info($"Test d'affichage de la roue en {cx},{cy}");
                    break;
                }
                case "diagmode":
                    if (_hooks is null) break;
                    _hooks.Diagnostic = !_hooks.Diagnostic;
                    Log.MinimumLevel = _hooks.Diagnostic ? LogLevel.Debug : LogLevel.Info;
                    Log.Info("Mode diagnostic " + (_hooks.Diagnostic ? "activé" : "désactivé"));
                    if (_hooks.Diagnostic)
                        Notify("Mode diagnostic activé", "Refaites le geste (glisser + touche) 2 ou 3 fois, puis envoyez le journal (Dépannage › Ouvrir le dossier des journaux).");
                    break;
                case "quit": Quit(); break;
                default:
                    if (id.StartsWith("fav:") && int.TryParse(id[4..], out int i) && i < Settings.Favorites.Count)
                        FileWheel.Windows.Shell.ShellOps.Open(Settings.Favorites[i], IntPtr.Zero);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Menu de notification : " + id, ex);
            Ui.Ui.Message(ex.Message, image: MessageBoxImage.Error);
        }
    }

    public void Notify(string title, string text, bool error = false)
    {
        if (_tray is null) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Notify(title, text, error)); return; }
        _tray.ShowBalloon(title, text, error);
    }

    /// <summary>Un outil a cree un fichier : notification (clic = afficher dans l'Explorateur) et pastille verte.</summary>
    public void ReportCreated(string path, string tool)
    {
        _lastOutput = path;
        SetBadge(TrayBadge.Success);
        UpdateTrayIcon();
        Notify(tool + " : terminé", Path.GetFileName(path));
    }

    public void NotifyOperation(FileOperation op)
    {
        switch (op.Status)
        {
            case OperationStatus.Succeeded:
                _lastOutput = op.Outputs.LastOrDefault();
                Notify("Terminé", op.Title + (op.Outputs.Count == 1 ? $"\n→ {Path.GetFileName(op.Outputs[0].TrimEnd('\\'))}" : ""));
                break;
            case OperationStatus.Failed:
                Notify("Échec", op.Title + "\n" + op.Error, error: true);
                break;
            case OperationStatus.Cancelled:
                Notify("Annulé", op.Title);
                break;
        }
    }

    /// <summary>Notification a la fin d'une conversion lancee depuis la roue (sans fenetre ouverte).</summary>
    public void WatchJob(ConversionJob job)
    {
        lock (_watched) _watched.Add(job.Id);
    }

    private void OnJobChanged(ConversionJob job)
    {
        if (job.IsFinished) SetBadge(job.Status == JobStatus.Failed ? TrayBadge.Error : job.Status == JobStatus.Succeeded ? TrayBadge.Success : TrayBadge.None);
        UpdateTrayIcon();
        if (!job.IsFinished) return;
        lock (_watched) { if (!_watched.Remove(job.Id)) return; }
        switch (job.Status)
        {
            case JobStatus.Succeeded:
                _lastOutput = job.Outputs.FirstOrDefault();
                var name = job.Outputs.Count == 1 ? Path.GetFileName(job.Outputs[0].TrimEnd('\\')) : $"{job.Outputs.Count} fichiers";
                Notify("Conversion terminée", job.Warnings.Count > 0 ? $"{name}\n⚠ {job.Warnings[0]}" : name);
                break;
            case JobStatus.Failed:
                Notify("Conversion impossible", $"{job.Title}\n{job.Error}", error: true);
                break;
        }
    }

    // ------------------------------------------------------------------ fenetres

    public void OpenTasks()
    {
        if (_tasks is null || !_tasks.IsLoaded)
        {
            _tasks = new TasksWindow(Engine, Operations);
            _tasks.Closed += (_, _) => _tasks = null;
        }
        _tasks.ShowAndActivate();
    }

    public void OpenSettings(string? tab)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(Settings, Engine, ApplySettings, () => CustomActions, list => { CustomActions = list; SaveCustomActions(); });
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (tab is not null) _settingsWindow.ShowTab(tab);
        Activate(_settingsWindow);
    }

    public void OpenConverter(IReadOnlyList<string> files)
    {
        if (_converter is not null && files.Count == 0) { Activate(_converter); return; }
        var w = new ConverterWindow(Engine, () => Settings, files, tab => OpenSettings(tab));
        w.Closed += (_, _) => { if (_converter == w) _converter = null; };
        _converter = w;
        w.Show();
        Activate(w);
    }

    public void OpenQuickConvert(IReadOnlyList<string> files, string? preselectKey)
    {
        var w = new QuickConvertWindow(Engine, () => Settings, files, preselectKey, tab => OpenSettings(tab), OpenConverter);
        w.Show();
        Activate(w);
    }

    private static void Activate(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (h != IntPtr.Zero) WindowStyles.ForceForeground(h);
    }

    /// <summary>Signal d'une seconde instance (double-clic sur le raccourci alors que FileWheel tourne deja).</summary>
    public void BringToFront() => Dispatcher.BeginInvoke(() => OpenSettings(null));

    // ------------------------------------------------------------------ parametres

    public void SaveSettings()
    {
        try { SettingsStore.Save(Settings.Normalize()); }
        catch (Exception ex) { Log.Error("Enregistrement des paramètres", ex); }
    }

    public void SaveCustomActions()
    {
        try { CustomActionStore.Save(CustomActions); }
        catch (Exception ex) { Log.Error("Enregistrement des actions personnalisées", ex); }
    }

    private void ApplySettings(AppSettings s)
    {
        Settings = s;
        _hooks?.UpdateOptions(s.Trigger.ToGestureOptions());
        _hooks?.SetEnabled(s.WheelEnabled);
        Engine.Apply(s.Conversion);
        _tray?.SetTooltip(TrayTooltip());
        UpdateTrayIcon();
    }

    private void CleanupTemp()
    {
        try
        {
            int n = OutputPlanner.CleanupStale(AppPaths.TempDir, TimeSpan.FromHours(Settings.Conversion.TempMaxAgeHours));
            if (n > 0) Log.Info($"{n} fichier(s) temporaire(s) ancien(s) supprimé(s).");
        }
        catch (Exception ex) { Log.Warn("Nettoyage des fichiers temporaires : " + ex.Message); }
    }

    // ------------------------------------------------------------------ arret

    public void Quit()
    {
        int running = Engine.Queue.ActiveCount + Operations.All.Count(o => !o.IsFinished);
        if (running > 0 && !ConfirmDialog.Ask("Quitter FileWheel", $"{running} tâche(s) en cours seront annulées. Les fichiers déjà terminés sont conservés ; les fichiers partiels sont supprimés.", "Quitter"))
            return;
        Engine.Queue.CancelAll();
        foreach (var op in Operations.All.Where(o => !o.IsFinished)) op.Cancel();
        try { Engine.Queue.WhenIdleAsync(TimeSpan.FromSeconds(5)).Wait(TimeSpan.FromSeconds(6)); } catch { }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _hooks?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        Log.Info("FileWheel arrêté.");
        Log.Flush();
        base.OnExit(e);
    }
}

public static class AppInfo
{
    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
}
