using System.Diagnostics;
using System.Text;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;

namespace FileWheel.Conversion.Core;

public sealed record ProcessResult(int ExitCode, string StdOutTail, string StdErrTail, TimeSpan Duration, bool Killed);

/// <summary>
/// Lancement des moteurs externes, sans interpreteur de commandes :
/// - chaque argument est passe separement (ArgumentList) : pas d'injection possible via un nom
///   de fichier, meme contenant « &amp; », « | », « ; » ou des guillemets ;
/// - pas de fenetre console ; priorite reduite par defaut ;
/// - annulation = arret de l'arbre de processus complet ;
/// - seules les dernieres lignes de sortie sont conservees (memoire bornee).
/// </summary>
public static class ProcessRunner
{
    public static TaskPriority DefaultPriority { get; set; } = TaskPriority.BelowNormal;

    public static async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct,
        Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, string? workingDir = null,
        IDictionary<string, string>? env = null, TimeSpan? timeout = null, int tailChars = 16_000)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDir ?? Path.GetTempPath(),
        };
        foreach (var a in args)
        {
            if (a.Contains('\0')) throw new ConversionException(ConversionErrorKind.InvalidInput, "Argument invalide.");
            psi.ArgumentList.Add(a);
        }
        if (env is not null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        Log.Debug($"Lancement : {Path.GetFileName(exe)} {string.Join(' ', psi.ArgumentList.Select(Quote))}");
        var outTail = new Tail(tailChars);
        var errTail = new Tail(tailChars);
        var sw = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.Exited += (_, _) => exited.TrySetResult();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            outTail.Add(e.Data);
            try { onStdOutLine?.Invoke(e.Data); } catch (Exception ex) { Log.Debug("onStdOut : " + ex.Message); }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            errTail.Add(e.Data);
            try { onStdErrLine?.Invoke(e.Data); } catch (Exception ex) { Log.Debug("onStdErr : " + ex.Message); }
        };

        try
        {
            if (!proc.Start()) throw new ConversionException(ConversionErrorKind.EngineMissing, $"Impossible de démarrer {Path.GetFileName(exe)}.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ConversionException(ConversionErrorKind.EngineMissing, $"Le moteur {Path.GetFileName(exe)} est introuvable ou ne peut pas être lancé.", ex.Message, ex);
        }
        try { proc.StandardInput.Close(); } catch { }
        SetPriority(proc);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        bool killed = false;
        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await exited.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            killed = true;
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch { }
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new ConversionException(ConversionErrorKind.EngineCrashed, $"{Path.GetFileName(exe)} ne répond plus (délai dépassé) : tâche arrêtée.");
        }
        // Laisse les lecteurs asynchrones vider les tampons
        proc.WaitForExit();
        sw.Stop();
        var res = new ProcessResult(proc.ExitCode, outTail.ToString(), errTail.ToString(), sw.Elapsed, killed);
        if (res.ExitCode != 0) Log.Debug($"{Path.GetFileName(exe)} code {res.ExitCode} : {LastLines(res.StdErrTail, 6)}");
        return res;
    }

    private static void SetPriority(Process p)
    {
        try
        {
            p.PriorityClass = DefaultPriority switch
            {
                TaskPriority.Idle => ProcessPriorityClass.Idle,
                TaskPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
                _ => ProcessPriorityClass.Normal,
            };
        }
        catch { /* le processus a pu se terminer immediatement */ }
    }

    public static string LastLines(string text, int n)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" | ", lines.TakeLast(n));
    }

    private static string Quote(string a) => a.Contains(' ') ? "\"" + a + "\"" : a;

    /// <summary>Tampon circulaire de texte : garde les N derniers caracteres.</summary>
    private sealed class Tail(int max)
    {
        private readonly StringBuilder _sb = new();
        private readonly object _lock = new();

        public void Add(string line)
        {
            lock (_lock)
            {
                _sb.Append(line).Append('\n');
                if (_sb.Length > max * 2) _sb.Remove(0, _sb.Length - max);
            }
        }

        public override string ToString()
        {
            lock (_lock) return _sb.Length > max ? _sb.ToString(_sb.Length - max, max) : _sb.ToString();
        }
    }
}
