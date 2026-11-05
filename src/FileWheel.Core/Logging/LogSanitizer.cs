using System.Text.RegularExpressions;

namespace FileWheel.Core.Logging;

/// <summary>
/// Retire les donnees personnelles d'un extrait de journal avant de le joindre a un signalement :
/// chemins, noms de fichiers et de dossiers, adresses e-mail, nom de l'utilisateur et du PC.
/// L'extension des fichiers est gardee (utile pour comprendre un probleme de conversion).
/// </summary>
public static partial class LogSanitizer
{
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var s = text;
        foreach (var secret in new[] { Environment.UserName, Environment.MachineName })
            if (secret is { Length: >= 3 }) s = Regex.Replace(s, Regex.Escape(secret), "<…>", RegexOptions.IgnoreCase);
        s = Email().Replace(s, "<e-mail>");
        s = Quoted().Replace(s, m => "« " + Keep(m.Groups[1].Value) + " »");
        s = WinPath().Replace(s, m => "<chemin>" + Ext(m.Value));
        s = UnixPath().Replace(s, m => "<chemin>" + Ext(m.Value));
        s = FileName().Replace(s, m => KnownExtension(m.Groups[2].Value) ? "<fichier>" + m.Groups[2].Value : m.Value);
        return s;
    }

    /// <summary>Contenu entre guillemets : nom de fichier → extension seule ; sinon texte garde (noms d'actions...).</summary>
    private static string Keep(string inner)
    {
        if (WinPath().IsMatch(inner) || UnixPath().IsMatch(inner)) return "<chemin>" + Ext(inner);
        var m = FileName().Match(inner);
        if (m.Success && m.Length == inner.Length && KnownExtension(m.Groups[2].Value)) return "<fichier>" + m.Groups[2].Value;
        return inner;
    }

    private static readonly HashSet<string> Extra = new(StringComparer.OrdinalIgnoreCase)
        { "exe", "dll", "txt", "log", "json", "lnk", "tmp", "part", "bat", "ps1", "msi", "ini", "cfg", "bak" };

    /// <summary>Extension de fichier connue (catalogue des formats + quelques courantes) : evite de masquer « System.IO ».</summary>
    private static bool KnownExtension(string dotExt)
    {
        var e = dotExt.TrimStart('.');
        return Extra.Contains(e) || Formats.FormatCatalog.FromFileName("x." + e) is not null;
    }

    private static string Ext(string path)
    {
        var e = Path.GetExtension(path.TrimEnd('\\', '/', ')', '.', ','));
        return e.Length is > 1 and <= 6 && Regex.IsMatch(e, @"^\.[A-Za-z0-9]+$") ? e : "";
    }

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex Email();

    [GeneratedRegex(@"«\s*([^«»]{1,260}?)\s*»")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\|%[A-Z]+%\\)[^\r\n""«»<>|]*?(?=\s[-—(]|\s*$|[""«»<>|,;]|\s\(|\s→)", RegexOptions.Multiline)]
    private static partial Regex WinPath();

    [GeneratedRegex(@"(?<![\w.])/(?:home|tmp|Users|mnt|var)/[^\s""«»]+")]
    private static partial Regex UnixPath();

    // « mes vacances.jpg », « rapport_2024.pdf » : un mot suivi d'une extension connue de 2 a 5 caracteres
    [GeneratedRegex(@"(?<![\w<])([\p{L}\p{N}_\-()\[\]']+(?: [\p{L}\p{N}_\-()\[\]']+){0,6})(\.(?:[a-zA-Z][a-zA-Z0-9]{1,4}))(?![\w])")]
    private static partial Regex FileName();
}
