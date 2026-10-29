using FileWheel.Core.Logging;

namespace FileWheel.Conversion.Core;

public enum JobStatus { Pending, Running, Succeeded, Failed, Cancelled, Skipped }

/// <summary>Une tache de conversion dans la file (observable par l'interface).</summary>
public sealed class ConversionJob
{
    private readonly object _gate = new();
    internal CancellationTokenSource? Cts;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..10];
    public required ConversionOrder Order { get; init; }
    public string Title { get; init; } = "";
    public JobStatus Status { get; internal set; } = JobStatus.Pending;
    public double? Progress { get; internal set; }
    public string Stage { get; internal set; } = "En attente";
    public string? Error { get; internal set; }
    public ConversionErrorKind? ErrorKind { get; internal set; }
    public IReadOnlyList<string> Outputs { get; internal set; } = [];
    public IReadOnlyList<string> Warnings { get; internal set; } = [];
    public DateTime Created { get; } = DateTime.Now;
    public DateTime? Started { get; internal set; }
    public DateTime? Finished { get; internal set; }
    /// <summary>Temps restant estime ; null tant que l'estimation n'est pas raisonnable.</summary>
    public TimeSpan? Remaining { get; internal set; }
    public bool Cancellable { get; internal set; } = true;

    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped;
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Cancelled;

    public string StatusLabel => Status switch
    {
        JobStatus.Pending => "En attente",
        JobStatus.Running => Stage,
        JobStatus.Succeeded => Warnings.Count > 0 ? "Terminée (avec remarques)" : "Terminée",
        JobStatus.Failed => "Échec",
        JobStatus.Cancelled => "Annulée",
        JobStatus.Skipped => "Ignorée",
        _ => "",
    };

    internal void Update(Action change)
    {
        lock (_gate) change();
    }
}

/// <summary>
/// File de conversions asynchrone :
/// - nombre de taches simultanees borne (reglable a chaud) ;
/// - une erreur sur un fichier n'arrete pas les autres ;
/// - annulation individuelle ; relance d'une tache echouee ;
/// - « Mettre la file en pause » empeche le demarrage de nouvelles taches. Une tache en cours
///   ne peut PAS etre suspendue : aucun moteur ne le permet de facon fiable, et l'interface le dit.
/// </summary>
public sealed class JobQueue
{
    private readonly ConversionManager _manager;
    private readonly List<ConversionJob> _jobs = new();
    private readonly object _gate = new();
    private int _running;

    public JobQueue(ConversionManager manager, int maxConcurrent = 2)
    {
        _manager = manager;
        MaxConcurrent = maxConcurrent;
    }

    public int MaxConcurrent { get; set; }
    public bool Paused { get; private set; }

    /// <summary>Notifie un changement d'etat d'une tache (thread quelconque).</summary>
    public event Action<ConversionJob>? JobChanged;

    public IReadOnlyList<ConversionJob> Jobs { get { lock (_gate) return _jobs.ToList(); } }

    public int ActiveCount { get { lock (_gate) return _jobs.Count(j => !j.IsFinished); } }

    public ConversionJob Enqueue(ConversionOrder order, string? title = null)
    {
        var job = new ConversionJob { Order = order, Title = title ?? string.Join(", ", order.Inputs.Select(Path.GetFileName)) };
        lock (_gate) _jobs.Add(job);
        JobChanged?.Invoke(job);
        Pump();
        return job;
    }

    public void Cancel(ConversionJob job)
    {
        bool notify = false;
        lock (_gate)
        {
            if (job.Status == JobStatus.Pending)
            {
                job.Status = JobStatus.Cancelled;
                job.Stage = "Annulée";
                job.Finished = DateTime.Now;
                notify = true;
            }
        }
        if (job.Status == JobStatus.Running) job.Cts?.Cancel();
        if (notify) JobChanged?.Invoke(job);
    }

    public void CancelAll()
    {
        foreach (var j in Jobs.Where(j => !j.IsFinished)) Cancel(j);
    }

    public ConversionJob? Retry(ConversionJob job) => job.CanRetry ? Enqueue(job.Order, job.Title) : null;

    public void Pause() { Paused = true; }

    public void Resume() { Paused = false; Pump(); }

    public void ClearFinished()
    {
        lock (_gate) _jobs.RemoveAll(j => j.IsFinished);
        JobChanged?.Invoke(null!);
    }

    /// <summary>Attend la fin de toutes les taches (tests, fermeture).</summary>
    public async Task WhenIdleAsync(TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (ActiveCount > 0 && DateTime.UtcNow < end) await Task.Delay(50).ConfigureAwait(false);
    }

    private void Pump()
    {
        while (true)
        {
            ConversionJob? next;
            lock (_gate)
            {
                if (Paused || _running >= Math.Max(1, MaxConcurrent)) return;
                next = _jobs.FirstOrDefault(j => j.Status == JobStatus.Pending);
                if (next is null) return;
                next.Status = JobStatus.Running;
                next.Started = DateTime.Now;
                next.Stage = "Préparation…";
                next.Cts = new CancellationTokenSource();
                _running++;
            }
            JobChanged?.Invoke(next);
            var job = next;
            _ = Task.Run(() => RunJob(job));
        }
    }

    private async Task RunJob(ConversionJob job)
    {
        try
        {
            var prep = _manager.Prepare(job.Order);
            job.Cancellable = prep.Capability.Cancellable;
            var progress = new Progress(job, this);
            var result = await _manager.RunAsync(prep, progress, job.Cts!.Token).ConfigureAwait(false);
            job.Update(() =>
            {
                job.Outputs = result.Outputs;
                job.Warnings = result.Warnings;
                job.Status = JobStatus.Succeeded;
                job.Progress = 1;
                job.Remaining = null;
            });
        }
        catch (OperationCanceledException)
        {
            job.Update(() => { job.Status = JobStatus.Cancelled; job.Stage = "Annulée"; job.Remaining = null; });
        }
        catch (ConversionException ex)
        {
            job.Update(() =>
            {
                job.Status = ex.Kind == ConversionErrorKind.NameConflict ? JobStatus.Skipped : ex.Kind == ConversionErrorKind.Cancelled ? JobStatus.Cancelled : JobStatus.Failed;
                job.Error = ex.Message;
                job.ErrorKind = ex.Kind;
                job.Remaining = null;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Tâche de conversion", ex);
            job.Update(() => { job.Status = JobStatus.Failed; job.Error = ConversionManager.Translate(ex).Message; job.Remaining = null; });
        }
        finally
        {
            job.Finished = DateTime.Now;
            job.Cts?.Dispose();
            job.Cts = null;
            lock (_gate) _running--;
            JobChanged?.Invoke(job);
            Pump();
        }
    }

    private sealed class Progress(ConversionJob job, JobQueue queue) : IProgress<ConversionProgress>
    {
        private DateTime _lastNotify;

        public void Report(ConversionProgress value)
        {
            job.Update(() =>
            {
                if (value.Fraction is { } f) job.Progress = Math.Clamp(f, 0, 1);
                job.Stage = value.Stage;
                job.Remaining = value.Remaining ?? EstimateRemaining(job);
            });
            var now = DateTime.UtcNow;
            if ((now - _lastNotify).TotalMilliseconds < 150 && value.Fraction is not 1) return;
            _lastNotify = now;
            queue.JobChanged?.Invoke(job);
        }

        private static TimeSpan? EstimateRemaining(ConversionJob job)
        {
            if (job.Progress is not { } f || f < 0.05 || f >= 1 || job.Started is null) return null;
            var elapsed = DateTime.Now - job.Started.Value;
            if (elapsed.TotalSeconds < 3) return null;
            return TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - f) / f);
        }
    }
}
