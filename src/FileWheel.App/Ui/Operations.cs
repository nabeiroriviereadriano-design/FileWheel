using FileWheel.Core.Logging;

namespace FileWheel.App.Ui;

public enum OperationStatus { Running, Succeeded, Failed, Cancelled }

/// <summary>Operation sur fichiers en arriere-plan (copie, deplacement, archive...) affichee dans la fenetre des taches.</summary>
public sealed class FileOperation
{
    private readonly CancellationTokenSource _cts = new();
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public required string Title { get; init; }
    public OperationStatus Status { get; internal set; } = OperationStatus.Running;
    public double? Progress { get; internal set; }
    public string Stage { get; internal set; } = "";
    public string? Error { get; internal set; }
    public List<string> Outputs { get; } = new();
    public DateTime Started { get; } = DateTime.Now;
    public DateTime? Finished { get; internal set; }
    public CancellationToken Token => _cts.Token;
    public bool IsFinished => Status != OperationStatus.Running;
    public void Cancel() => _cts.Cancel();
}

/// <summary>Registre des operations sur fichiers en cours (thread-safe), avec notification de fin.</summary>
public sealed class OperationsCenter
{
    private readonly List<FileOperation> _ops = new();
    private readonly object _gate = new();
    public event Action<FileOperation>? Changed;

    public IReadOnlyList<FileOperation> All { get { lock (_gate) return _ops.ToList(); } }

    /// <summary>Lance une operation ; <paramref name="body"/> recoit l'operation pour signaler sa progression.</summary>
    public FileOperation Start(string title, Func<FileOperation, Task<string?>> body, Action<FileOperation>? done = null)
    {
        var op = new FileOperation { Title = title };
        lock (_gate) _ops.Add(op);
        Changed?.Invoke(op);
        _ = Task.Run(async () =>
        {
            try
            {
                var error = await body(op).ConfigureAwait(false);
                op.Status = op.Token.IsCancellationRequested ? OperationStatus.Cancelled : error is null ? OperationStatus.Succeeded : OperationStatus.Failed;
                op.Error = error;
            }
            catch (OperationCanceledException) { op.Status = OperationStatus.Cancelled; }
            catch (Exception ex)
            {
                op.Status = OperationStatus.Failed;
                op.Error = FileWheel.Conversion.Core.ConversionManager.Translate(ex).Message;
                Log.Error($"Opération « {title} »", ex);
            }
            op.Finished = DateTime.Now;
            op.Progress = op.Status == OperationStatus.Succeeded ? 1 : op.Progress;
            Changed?.Invoke(op);
            try { done?.Invoke(op); } catch (Exception ex) { Log.Error("Fin d'opération", ex); }
        });
        return op;
    }

    public void Report(FileOperation op, double? fraction, string stage)
    {
        op.Progress = fraction;
        op.Stage = stage;
        Changed?.Invoke(op);
    }

    public void ClearFinished()
    {
        lock (_gate) _ops.RemoveAll(o => o.IsFinished);
        Changed?.Invoke(null!);
    }
}
