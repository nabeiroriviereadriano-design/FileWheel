using System.Diagnostics;
using System.Text;

namespace FileWheel.Tests;

public sealed class AssertException(string message) : Exception(message);
public sealed class SkipException(string reason) : Exception(reason);

/// <summary>
/// Harnais minimal : chaque test est execute, chronometre et classe Reussi / Echec / Non execute.
/// Un test « non execute » l'est toujours pour une raison ecrite (moteur absent, Windows requis...) :
/// rien n'est compte comme teste sans avoir ete reellement execute et verifie.
/// </summary>
public static class T
{
    public sealed record Result(string Suite, string Name, string Status, string Detail, double Ms);

    public static readonly List<Result> Results = new();
    public static string? Filter { get; set; }
    private static string _suite = "";

    public static void Suite(string name)
    {
        _suite = name;
        Console.WriteLine();
        Console.WriteLine($"== {name}");
    }

    public static async Task Run(string name, Func<Task> body)
    {
        if (Filter is not null && !(_suite + " " + name).Contains(Filter, StringComparison.OrdinalIgnoreCase)) return;
        var sw = Stopwatch.StartNew();
        string status, detail = "";
        try
        {
            await body();
            status = "OK";
        }
        catch (SkipException ex) { status = "NON EXÉCUTÉ"; detail = ex.Message; }
        catch (AssertException ex) { status = "ÉCHEC"; detail = ex.Message; }
        catch (FileWheel.Conversion.Core.ConversionException ex)
        {
            status = "ÉCHEC";
            var tech = ex.Technical is { Length: > 0 } t ? " | " + (t.Length > 400 ? t[^400..] : t).Replace('\n', ' ') : "";
            detail = $"ConversionException ({ex.Kind}): {ex.Message}{tech}";
        }
        catch (Exception ex) { status = "ÉCHEC"; detail = $"{ex.GetType().Name}: {ex.Message}"; }
        sw.Stop();
        Results.Add(new Result(_suite, name, status, detail, sw.Elapsed.TotalMilliseconds));
        var mark = status == "OK" ? "  ok  " : status == "ÉCHEC" ? " FAIL " : " skip ";
        Console.WriteLine($"[{mark}] {name} ({sw.Elapsed.TotalMilliseconds:0} ms){(detail.Length > 0 ? " — " + detail : "")}");
    }

    public static Task Run(string name, Action body) => Run(name, () => { body(); return Task.CompletedTask; });

    public static void Check(bool cond, string message)
    {
        if (!cond) throw new AssertException(message);
    }

    public static void Equal<TV>(TV expected, TV actual, string what)
    {
        if (!EqualityComparer<TV>.Default.Equals(expected, actual))
            throw new AssertException($"{what} : attendu « {expected} », obtenu « {actual} »");
    }

    public static void Skip(string reason) => throw new SkipException(reason);

    public static async Task<TEx> Throws<TEx>(Func<Task> body, string what) where TEx : Exception
    {
        try { await body(); }
        catch (TEx ex) { return ex; }
        catch (Exception ex) { throw new AssertException($"{what} : exception {typeof(TEx).Name} attendue, obtenu {ex.GetType().Name}: {ex.Message}"); }
        throw new AssertException($"{what} : exception {typeof(TEx).Name} attendue, aucune levée");
    }

    public static TEx Throws<TEx>(Action body, string what) where TEx : Exception =>
        Throws<TEx>(() => { body(); return Task.CompletedTask; }, what).GetAwaiter().GetResult();

    public static string Report(string environment)
    {
        var sb = new StringBuilder();
        int ok = Results.Count(r => r.Status == "OK"), fail = Results.Count(r => r.Status == "ÉCHEC"), skip = Results.Count(r => r.Status == "NON EXÉCUTÉ");
        sb.AppendLine("# FileWheel — résultats des tests automatiques");
        sb.AppendLine();
        sb.AppendLine($"Exécution du {DateTime.Now:dd/MM/yyyy HH:mm}. {environment}");
        sb.AppendLine();
        sb.AppendLine($"**{ok} réussis, {fail} en échec, {skip} non exécutés** sur {Results.Count}.");
        sb.AppendLine();
        foreach (var g in Results.GroupBy(r => r.Suite))
        {
            sb.AppendLine($"## {g.Key}");
            sb.AppendLine();
            sb.AppendLine("| Test | Résultat | Détail |");
            sb.AppendLine("|---|---|---|");
            foreach (var r in g)
                sb.AppendLine($"| {r.Name.Replace("|", "\\|")} | {r.Status} | {r.Detail.Replace("|", "\\|").Replace("\n", " ")} |");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
