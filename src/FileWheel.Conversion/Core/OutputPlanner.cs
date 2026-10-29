using FileWheel.Core.FileOperations;
using FileWheel.Core.Formats;
using FileWheel.Core.Settings;

namespace FileWheel.Conversion.Core;

public sealed record OutputPlan(string FinalPath, string TempPath, bool IsFolder, bool ReplaceExisting);

/// <summary>
/// Calcule le chemin de sortie d'une conversion et son temporaire.
/// Garanties :
/// - la sortie n'est JAMAIS un des fichiers d'entree (meme format, meme dossier => suffixe force) ;
/// - un fichier existant n'est remplace que si la politique « Ecraser » a ete choisie explicitement ;
/// - le temporaire est dans le dossier de destination (meme volume => renommage atomique a la fin).
/// </summary>
public static class OutputPlanner
{
    public const string TempPrefix = ".~fw-";

    public static OutputPlan Plan(IReadOnlyList<string> inputs, string targetFormatId, bool folderOutput, string? outputDir,
        string suffix, ConflictPolicy policy, string? nameOverride = null)
    {
        var first = Path.GetFullPath(inputs[0]);
        var dir = string.IsNullOrWhiteSpace(outputDir) ? Path.GetDirectoryName(first)! : Path.GetFullPath(outputDir);
        if (!Directory.Exists(dir))
            throw new ConversionException(ConversionErrorKind.WriteError, $"Le dossier de destination n'existe pas : {dir}");

        var (stem, _) = NameConflict.SplitName(Path.GetFileName(first));
        if (nameOverride is { Length: > 0 }) stem = PathSafety.SanitizeFileName(nameOverride);
        if (inputs.Count > 1 && nameOverride is null) stem += " (fusion)";
        var safeSuffix = PathSafety.SanitizeFileName(suffix, allowEmpty: true);

        string name;
        if (folderOutput)
        {
            var label = targetFormatId switch
            {
                "extract" => "",
                _ => " - " + targetFormatId.ToUpperInvariant(),
            };
            name = stem + safeSuffix + label;
        }
        else
        {
            name = stem + safeSuffix + "." + FormatCatalog.OutputExtension(targetFormatId);
        }
        name = PathSafety.SanitizeFileName(name);
        var final = Path.Combine(dir, name);

        // Ne jamais viser un fichier source
        if (inputs.Any(i => PathSafety.SamePath(i, final)))
        {
            name = folderOutput ? $"{stem}{safeSuffix} (converti)" : $"{stem}{safeSuffix} (converti).{FormatCatalog.OutputExtension(targetFormatId)}";
            final = Path.Combine(dir, PathSafety.SanitizeFileName(name));
        }

        bool replace = false;
        if (File.Exists(final) || Directory.Exists(final))
        {
            switch (policy)
            {
                case ConflictPolicy.Skip:
                    throw new ConversionException(ConversionErrorKind.NameConflict, $"« {Path.GetFileName(final)} » existe déjà : fichier ignoré (politique « Ignorer »).");
                case ConflictPolicy.Overwrite when !folderOutput && !inputs.Any(i => PathSafety.SamePath(i, final)):
                    replace = true;
                    break;
                default:
                    final = NameConflict.UniquePath(final);
                    break;
            }
        }

        var ext = folderOutput ? "" : "." + FormatCatalog.OutputExtension(targetFormatId);
        var temp = Path.Combine(dir, $"{TempPrefix}{Guid.NewGuid():N}{ext}");
        return new OutputPlan(final, temp, folderOutput, replace);
    }

    /// <summary>Supprime les temporaires FileWheel abandonnes (arret brutal) plus vieux que <paramref name="maxAge"/>.</summary>
    public static int CleanupStale(string dir, TimeSpan maxAge)
    {
        int n = 0;
        try
        {
            foreach (var p in Directory.EnumerateFileSystemEntries(dir, TempPrefix + "*"))
            {
                try
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(p);
                    if (age < maxAge) continue;
                    if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p);
                    n++;
                }
                catch { }
            }
        }
        catch { }
        return n;
    }
}
