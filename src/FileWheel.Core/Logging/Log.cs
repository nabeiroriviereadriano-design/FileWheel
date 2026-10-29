using System.Collections.Concurrent;
using System.Text;
using FileWheel.Core.Util;

namespace FileWheel.Core.Logging;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Journal fichier minimaliste, thread-safe, sans dependance.
/// - un fichier par jour dans %LOCALAPPDATA%\FileWheel\logs, conserve N jours ;
/// - le chemin du profil utilisateur est masque (%USERPROFILE%) pour ne pas exposer
///   inutilement le nom du compte dans les journaux partages ;
/// - l'ecriture se fait sur un thread dedie : un appel de journal ne bloque jamais
///   le hook clavier/souris ni l'interface.
/// </summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 10_000);
    private static readonly object InitLock = new();
    private static Thread? _writer;
    private static string? _dir;
    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public static int RetentionDays { get; set; } = 14;

    /// <summary>Copie en memoire des dernieres lignes (affichable dans l'interface).</summary>
    public static readonly ConcurrentQueue<string> Recent = new();

    /// <summary>Appele en plus de l'ecriture fichier (tests, console).</summary>
    public static Action<string>? Sink { get; set; }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg) => Write(LogLevel.Warning, msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? msg : $"{msg} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    public static void Write(LogLevel level, string msg)
    {
        if (level < MinimumLevel) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{LevelTag(level)}] [{Environment.CurrentManagedThreadId,3}] {Redact(msg)}";
        Recent.Enqueue(line);
        while (Recent.Count > 500 && Recent.TryDequeue(out _)) { }
        Sink?.Invoke(line);
        EnsureWriter();
        Queue.TryAdd(line);
    }

    /// <summary>Masque le profil utilisateur dans un texte de journal.</summary>
    public static string Redact(string s)
    {
        if (string.IsNullOrEmpty(UserProfile) || UserProfile.Length < 4) return s;
        return s.Replace(UserProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    public static string CurrentLogFile => Path.Combine(_dir ?? AppPaths.LogsDir, $"filewheel-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Vide la file d'ecriture (fin de programme, tests).</summary>
    public static void Flush(int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Queue.Count > 0 && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(10);
    }

    private static string LevelTag(LogLevel l) => l switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "AVR",
        _ => "ERR",
    };

    private static void EnsureWriter()
    {
        if (_writer is not null) return;
        lock (InitLock)
        {
            if (_writer is not null) return;
            try { _dir = AppPaths.LogsDir; } catch { _dir = Path.GetTempPath(); }
            PurgeOld();
            _writer = new Thread(WriterLoop) { IsBackground = true, Name = "FileWheel.Log" };
            _writer.Start();
        }
    }

    private static void WriterLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                var sb = new StringBuilder(line).Append(Environment.NewLine);
                while (Queue.TryTake(out var more)) sb.Append(more).Append(Environment.NewLine);
                File.AppendAllText(CurrentLogFile, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Le journal ne doit jamais faire tomber l'application.
            }
        }
    }

    private static void PurgeOld()
    {
        try
        {
            var limit = DateTime.Now.AddDays(-Math.Max(1, RetentionDays));
            foreach (var f in Directory.EnumerateFiles(_dir!, "filewheel-*.log"))
                if (File.GetLastWriteTime(f) < limit) File.Delete(f);
        }
        catch { /* sans importance */ }
    }
}
