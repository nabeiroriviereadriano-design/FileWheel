using System.Security.Cryptography;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Util;

namespace FileWheel.Core.Actions;

public enum CustomActionType
{
    /// <summary>Lancer un programme choisi par l'utilisateur avec des arguments.</summary>
    RunProgram,
    /// <summary>Copier vers un dossier fixe.</summary>
    CopyToFolder,
    /// <summary>Deplacer vers un dossier fixe.</summary>
    MoveToFolder,
    /// <summary>Convertir vers un format avec des options enregistrees.</summary>
    ConvertTo,
}

public enum CustomActionPlacement { Submenu, Root }

/// <summary>
/// Action personnalisee definie par l'utilisateur.
///
/// Securite :
/// - le programme est un chemin absolu choisi par l'utilisateur dans l'interface ; on ne lit
///   jamais une action depuis un fichier glisse ou une archive ;
/// - son empreinte SHA-256 est enregistree a la creation ; si l'executable change, l'execution
///   est suspendue jusqu'a confirmation ;
/// - les arguments sont passes un par un (ProcessStartInfo.ArgumentList), sans interpreteur de
///   commandes : un nom de fichier contenant « & » ou « ; » ne peut rien injecter.
/// </summary>
public sealed class CustomAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Nouvelle action";
    public string Glyph { get; set; } = Wheel.Glyphs.Lightning;
    public CustomActionType Type { get; set; } = CustomActionType.RunProgram;
    public CustomActionPlacement Placement { get; set; } = CustomActionPlacement.Submenu;
    public int Order { get; set; }
    /// <summary>Extensions acceptees (sans point). Vide = toutes.</summary>
    public List<string> Extensions { get; set; } = new();
    public bool AllowFolders { get; set; }
    public bool AllowMultiple { get; set; } = true;
    /// <summary>Un seul lancement avec tous les fichiers (sinon un lancement par fichier).</summary>
    public bool SingleInvocation { get; set; }
    public string ProgramPath { get; set; } = "";
    public string? ProgramSha256 { get; set; }
    /// <summary>Modele d'arguments, separes par des espaces ; un groupe entre guillemets forme un seul argument.
    /// Jetons : {path} {dir} {name} {stem} {ext} {paths} (tous les chemins, un argument chacun).</summary>
    public string Arguments { get; set; } = "{path}";
    public string Folder { get; set; } = "";
    public string TargetFormat { get; set; } = "";
    public Dictionary<string, string> ConversionOptions { get; set; } = new();

    public bool Accepts(string path, bool isDirectory, int selectionCount)
    {
        if (selectionCount > 1 && !AllowMultiple) return false;
        if (isDirectory) return AllowFolders;
        if (Extensions.Count == 0) return true;
        var ext = Path.GetExtension(path).TrimStart('.');
        return Extensions.Any(e => string.Equals(e.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Erreurs bloquantes de la definition (affichees dans l'editeur).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Le nom est obligatoire.");
        switch (Type)
        {
            case CustomActionType.RunProgram:
                if (string.IsNullOrWhiteSpace(ProgramPath) || !Path.IsPathFullyQualified(ProgramPath))
                    errors.Add("Le programme doit être un chemin complet choisi avec le bouton Parcourir.");
                else if (!File.Exists(ProgramPath))
                    errors.Add("Le programme est introuvable.");
                try { TokenizeTemplate(Arguments); }
                catch (FormatException ex) { errors.Add(ex.Message); }
                break;
            case CustomActionType.CopyToFolder:
            case CustomActionType.MoveToFolder:
                if (string.IsNullOrWhiteSpace(Folder) || !Directory.Exists(Folder)) errors.Add("Le dossier cible est introuvable.");
                break;
            case CustomActionType.ConvertTo:
                if (Formats.FormatCatalog.Get(TargetFormat) is null) errors.Add("Format cible inconnu.");
                break;
        }
        return errors;
    }

    /// <summary>Avertissements non bloquants (interpreteurs de commandes, etc.).</summary>
    public List<string> Warnings()
    {
        var w = new List<string>();
        if (Type != CustomActionType.RunProgram) return w;
        var exe = ProgramPath.Split('\\', '/').Last().ToLowerInvariant();
        if (exe is "cmd.exe" or "powershell.exe" or "pwsh.exe" or "wscript.exe" or "cscript.exe" or "mshta.exe" or "bash.exe" or "wsl.exe")
            w.Add("Ce programme est un interpréteur de commandes : il exécutera ce que vous écrivez dans les arguments. À n'utiliser qu'avec des commandes que vous comprenez.");
        return w;
    }

    public static string? ComputeSha256(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { return null; }
    }

    /// <summary>L'executable a-t-il change depuis l'enregistrement de l'action ?</summary>
    public bool ProgramChanged() =>
        Type == CustomActionType.RunProgram && ProgramSha256 is not null &&
        !string.Equals(ComputeSha256(ProgramPath), ProgramSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Decoupe le modele en jetons (guillemets pour grouper). Lance FormatException si mal forme.</summary>
    public static List<string> TokenizeTemplate(string template)
    {
        var tokens = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false, any = false;
        foreach (var c in template)
        {
            if (c == '"') { inQuotes = !inQuotes; any = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any) { tokens.Add(cur.ToString()); cur.Clear(); any = false; }
                continue;
            }
            cur.Append(c);
            any = true;
        }
        if (inQuotes) throw new FormatException("Guillemet non fermé dans les arguments.");
        if (any) tokens.Add(cur.ToString());
        return tokens;
    }

    /// <summary>
    /// Arguments concrets pour un lancement. Chaque jeton devient UN argument ;
    /// {paths} se developpe en autant d'arguments que de fichiers.
    /// </summary>
    public List<string> ExpandArguments(IReadOnlyList<string> paths)
    {
        var first = paths.Count > 0 ? Path.GetFullPath(paths[0]) : "";
        var result = new List<string>();
        foreach (var tok in TokenizeTemplate(Arguments))
        {
            if (tok == "{paths}")
            {
                result.AddRange(paths.Select(Path.GetFullPath));
                continue;
            }
            var (stem, ext) = NameConflict.SplitName(Path.GetFileName(first));
            result.Add(tok
                .Replace("{path}", first)
                .Replace("{dir}", Path.GetDirectoryName(first) ?? "")
                .Replace("{name}", Path.GetFileName(first))
                .Replace("{stem}", stem)
                .Replace("{ext}", ext.TrimStart('.')));
        }
        return result;
    }
}

public static class CustomActionStore
{
    public static List<CustomAction> Load() =>
        JsonStore.Load(AppPaths.CustomActionsFile, () => new List<CustomAction>())
            .OrderBy(a => a.Order).ThenBy(a => a.Name).ToList();

    public static void Save(List<CustomAction> actions) => JsonStore.Save(AppPaths.CustomActionsFile, actions);
}
