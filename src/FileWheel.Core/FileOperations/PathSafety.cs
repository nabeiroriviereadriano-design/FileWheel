using System.Text;

namespace FileWheel.Core.FileOperations;

/// <summary>
/// Regles de nommage et de confinement des chemins.
/// Tous les chemins produits par FileWheel passent par ici : noms Windows valides,
/// pas de nom reserve (CON, NUL...), pas d'ecriture hors du dossier autorise.
/// </summary>
public static class PathSafety
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    // Caracteres interdits sous Windows (independamment de la plateforme d'execution des tests).
    private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public const int MaxNameLength = 240;

    /// <summary>Raison pour laquelle un nom est refuse, ou null s'il est valide.</summary>
    public static string? ValidateFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Le nom ne peut pas être vide.";
        if (name.Length > MaxNameLength) return $"Le nom est trop long (maximum {MaxNameLength} caractères).";
        foreach (var c in name)
        {
            if (c < 32) return "Le nom contient un caractère de contrôle.";
            if (Array.IndexOf(Invalid, c) >= 0) return $"Le caractère « {c} » est interdit dans un nom de fichier.";
        }
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Un nom ne peut pas se terminer par un point ou un espace.";
        if (name is "." or "..") return "Nom invalide.";
        var stem = name.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem)) return $"« {stem} » est un nom réservé par Windows.";
        return null;
    }

    /// <summary>Transforme un texte quelconque en nom de fichier Windows valide.</summary>
    public static string SanitizeFileName(string? name, bool allowEmpty = false, string fallback = "fichier")
    {
        if (string.IsNullOrEmpty(name)) return allowEmpty ? "" : fallback;
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c < 32 || Array.IndexOf(Invalid, c) >= 0) sb.Append('_');
            else sb.Append(c);
        }
        var s = sb.ToString();
        // Underscores consecutifs fusionnes
        while (s.Contains("__")) s = s.Replace("__", "_");
        s = s.Trim().TrimEnd('.', ' ');
        if (s.Length > MaxNameLength) s = s[..MaxNameLength].TrimEnd('.', ' ');
        if (s.Length == 0) return allowEmpty ? "" : fallback;
        var stem = s.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem)) s = "_" + s;
        return s;
    }

    /// <summary>Vrai si <paramref name="candidate"/> est <paramref name="root"/> ou un de ses descendants.</summary>
    public static bool IsWithin(string root, string candidate)
    {
        var r = NormalizeDir(Path.GetFullPath(root));
        var c = Path.GetFullPath(candidate);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(NormalizeDir(c), r, cmp) || c.StartsWith(r, cmp);
    }

    /// <summary>
    /// Resout le chemin d'une entree d'archive sous <paramref name="root"/>.
    /// Refuse (null) les chemins absolus, les lecteurs, les « .. » qui sortent du dossier,
    /// les flux alternatifs NTFS (« fichier:flux ») et les noms reserves.
    /// </summary>
    public static string? ResolveArchiveEntry(string root, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;
        var n = entryName.Replace('\\', '/');
        if (n.StartsWith('/') || (n.Length >= 2 && n[1] == ':')) return null;   // absolu ou lecteur
        if (n.StartsWith("//")) return null;                                      // UNC
        var parts = n.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safe = new List<string>();
        foreach (var p in parts)
        {
            if (p == ".") continue;
            if (p == "..") return null;
            if (p.Contains(':')) return null;                                      // flux alternatif / lecteur
            if (ValidateFileName(p) is not null)
            {
                var s = SanitizeFileName(p);
                if (s.Length == 0) return null;
                safe.Add(s);
            }
            else safe.Add(p);
        }
        if (safe.Count == 0) return null;
        var full = Path.GetFullPath(Path.Combine([root, .. safe]));
        return IsWithin(root, full) ? full : null;
    }

    private static string NormalizeDir(string p) =>
        p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;

    /// <summary>Vrai si les deux chemins designent le meme fichier (casse ignoree sous Windows).</summary>
    public static bool SamePath(string a, string b)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), cmp);
    }
}

/// <summary>Calcul de noms uniques « Nom (2).ext ».</summary>
public static class NameConflict
{
    /// <summary>Premier chemin libre de la forme « dossier\nom (n).ext ».</summary>
    public static string UniquePath(string desiredPath, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        if (!exists(desiredPath)) return desiredPath;
        var dir = Path.GetDirectoryName(desiredPath) ?? "";
        var (stem, ext) = SplitName(Path.GetFileName(desiredPath));
        // Si le nom se termine deja par « (n) », on poursuit la numerotation.
        int start = 2;
        var m = System.Text.RegularExpressions.Regex.Match(stem, @"^(.*) \((\d{1,6})\)$");
        if (m.Success) { stem = m.Groups[1].Value; start = int.Parse(m.Groups[2].Value) + 1; }
        for (int i = start; i < start + 100_000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!exists(candidate)) return candidate;
        }
        throw new IOException("Impossible de trouver un nom libre.");
    }

    /// <summary>Separe « archive.tar.gz » en (« archive », « .tar.gz »).</summary>
    public static (string stem, string ext) SplitName(string fileName)
    {
        foreach (var dbl in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (fileName.EndsWith(dbl, StringComparison.OrdinalIgnoreCase) && fileName.Length > dbl.Length)
                return (fileName[..^dbl.Length], fileName[^dbl.Length..]);
        var ext = Path.GetExtension(fileName);
        if (ext.Length == fileName.Length) ext = "";   // « .gitignore »
        return (fileName[..^ext.Length], ext);
    }
}
