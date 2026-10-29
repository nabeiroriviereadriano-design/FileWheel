using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Core.Wheel;

namespace FileWheel.App.Ui;

/// <summary>Fenetre des taches : conversions et operations sur fichiers, progression, annulation, relance.</summary>
public sealed class TasksWindow : Window
{
    private readonly ConversionEngine _engine;
    private readonly OperationsCenter _ops;
    private readonly StackPanel _list = new();
    private readonly Button _pause;
    private readonly DispatcherTimer _refresh;
    private bool _dirty = true;

    public TasksWindow(ConversionEngine engine, OperationsCenter ops)
    {
        _engine = engine;
        _ops = ops;
        Title = "FileWheel — Tâches";
        Width = 720;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.Decorate(this);

        _pause = Ui.Button("Mettre la file en pause", TogglePause, glyph: "");
        var toolbar = Ui.Row(
            _pause,
            Ui.Button("Annuler tout", () => { _engine.Queue.CancelAll(); foreach (var o in _ops.All.Where(o => !o.IsFinished)) o.Cancel(); }, glyph: Glyphs.Cancel),
            Ui.Button("Effacer les terminées", () => { _engine.Queue.ClearFinished(); _ops.ClearFinished(); _dirty = true; }, glyph: Glyphs.Check));
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var header = Ui.Column(Ui.Title("Tâches"), toolbar,
            Ui.Hint("La pause empêche le démarrage de nouvelles tâches. Une conversion déjà commencée ne peut pas être suspendue (aucun moteur ne le permet de façon fiable) : elle peut seulement être annulée."));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        Content = root;

        _engine.Queue.JobChanged += _ => _dirty = true;
        _ops.Changed += _ => _dirty = true;
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _refresh.Tick += (_, _) => { if (_dirty) Rebuild(); };
        _refresh.Start();
        Closed += (_, _) => _refresh.Stop();
    }

    private void TogglePause()
    {
        if (_engine.Queue.Paused) _engine.Queue.Resume(); else _engine.Queue.Pause();
        _dirty = true;
    }

    private void Rebuild()
    {
        _dirty = false;
        _pause.Content = _engine.Queue.Paused ? "Reprendre la file" : "Mettre la file en pause";
        _list.Children.Clear();
        var jobs = _engine.Queue.Jobs.OrderByDescending(j => j.Created).ToList();
        var ops = _ops.All.OrderByDescending(o => o.Started).ToList();
        if (jobs.Count == 0 && ops.Count == 0)
        {
            _list.Children.Add(Ui.Hint("Aucune tâche. Les conversions lancées depuis la roue ou le convertisseur apparaissent ici."));
            return;
        }
        foreach (var o in ops) _list.Children.Add(OpRow(o));
        foreach (var j in jobs) _list.Children.Add(JobRow(j));
    }

    private UIElement JobRow(ConversionJob j)
    {
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = j.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var cap = _engine.Registry.ByKey(j.Order.CapabilityKey);
        var status = j.StatusLabel + (cap is not null ? "  ·  " + cap.DisplayLabel : "");
        if (j.Remaining is { } r && j.Status == JobStatus.Running) status += $"  ·  environ {Format(r)} restant";
        col.Children.Add(Ui.Text(status, j.Status switch { JobStatus.Failed => Ui.Error, JobStatus.Succeeded => Ui.Success, _ => Ui.Subtle }, 12));
        if (j.Status is JobStatus.Running or JobStatus.Pending)
            col.Children.Add(new ProgressBar { Height = 4, Margin = new Thickness(0, 6, 0, 2), Minimum = 0, Maximum = 1, Value = j.Progress ?? 0, IsIndeterminate = j.Status == JobStatus.Running && j.Progress is null });
        if (j.Error is { } err) col.Children.Add(Ui.Text(err, Ui.Error, 12));
        foreach (var w in j.Warnings.Take(4)) col.Children.Add(Ui.Text("• " + w, Ui.Warning, 12));
        var buttons = Ui.Row();
        if (!j.IsFinished && j.Cancellable) buttons.Children.Add(Ui.Button("Annuler", () => _engine.Queue.Cancel(j)));
        if (j.CanRetry) buttons.Children.Add(Ui.Button("Relancer", () => _engine.Queue.Retry(j)));
        if (j.Status == JobStatus.Succeeded && j.Outputs.Count > 0)
        {
            var first = j.Outputs[0];
            buttons.Children.Add(Ui.Button("Ouvrir le dossier", () => FileWheel.Windows.Shell.ShellOps.RevealInExplorer([first])));
            if (File.Exists(first)) buttons.Children.Add(Ui.Button("Ouvrir", () => OpenSafe(first)));
        }
        if (buttons.Children.Count > 0) col.Children.Add(buttons);
        return Ui.Card(col, 12);
    }

    private UIElement OpRow(FileOperation o)
    {
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = o.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var text = o.Status switch
        {
            OperationStatus.Running => o.Stage.Length > 0 ? o.Stage : "En cours…",
            OperationStatus.Succeeded => "Terminée",
            OperationStatus.Cancelled => "Annulée",
            _ => "Échec",
        };
        col.Children.Add(Ui.Text(text, o.Status == OperationStatus.Failed ? Ui.Error : o.Status == OperationStatus.Succeeded ? Ui.Success : Ui.Subtle, 12));
        if (o.Status == OperationStatus.Running)
            col.Children.Add(new ProgressBar { Height = 4, Margin = new Thickness(0, 6, 0, 2), Minimum = 0, Maximum = 1, Value = o.Progress ?? 0, IsIndeterminate = o.Progress is null });
        if (o.Error is { } e) col.Children.Add(Ui.Text(e, Ui.Error, 12));
        var buttons = Ui.Row();
        if (!o.IsFinished) buttons.Children.Add(Ui.Button("Annuler", o.Cancel));
        if (o.Outputs.Count > 0) buttons.Children.Add(Ui.Button("Ouvrir le dossier", () => FileWheel.Windows.Shell.ShellOps.RevealInExplorer([o.Outputs[0]])));
        if (buttons.Children.Count > 0) col.Children.Add(buttons);
        return Ui.Card(col, 12);
    }

    private static void OpenSafe(string path)
    {
        try { FileWheel.Windows.Shell.ShellOps.Open(path, IntPtr.Zero); }
        catch (Exception ex) { Ui.Message(ex.Message, image: MessageBoxImage.Warning); }
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00}" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{Math.Max(1, t.Seconds)} s";

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _ = Process.GetCurrentProcess();
    }
}
