using FileWheel.Core.Logging;
using FileWheel.Core.Settings;

namespace FileWheel.Core.FileOperations;

public enum ConflictChoice { Replace, Rename, Skip, Cancel }

/// <summary>Question posee a l'utilisateur lors d'un conflit (null = politique automatique).</summary>
public delegate ConflictChoice ConflictResolver(string source, string existingDestination, out bool applyToAll);

public sealed record FileOpProgress(string CurrentItem, long BytesDone, long BytesTotal, int ItemsDone, int ItemsTotal);

public sealed class FileOpResult
{
    public int Done { get; set; }
    public int Skipped { get; set; }
    public bool Cancelled { get; set; }
    public List<(string Path, string Error)> Errors { get; } = new();
    public List<string> Created { get; } = new();
    public bool Success => Errors.Count == 0 && !Cancelled;

    public string Summary(string verb)
    {
        var parts = new List<string>();
        if (Done > 0) parts.Add($"{Done} élément(s) {verb}");
        if (Skipped > 0) parts.Add($"{Skipped} ignoré(s)");
        if (Errors.Count > 0) parts.Add($"{Errors.Count} erreur(s)");
        if (Cancelled) parts.Add("opération annulée");
        return parts.Count == 0 ? "Aucun élément traité." : string.Join(", ", parts) + ".";
    }
}

/// <summary>
/// Copie, deplacement, renommage et suppression avec des garanties strictes :
/// - jamais d'ecrasement implicite : la politique de conflit est explicite (renommer par defaut) ;
/// - ecriture dans un fichier temporaire voisin puis renommage : pas de fichier a moitie ecrit
///   sous le nom final, meme en cas d'interruption ;
/// - un deplacement entre volumes copie, VERIFIE la taille, puis seulement supprime la source ;
///   si la suppression echoue, les deux exemplaires restent (aucune perte) ;
/// - un echec sur un element n'arrete pas les autres, et n'est jamais silencieux.
/// </summary>
public static class SafeFileOps
{
    private const int BufferSize = 1 << 20;

    public static FileOpResult CopyTo(IReadOnlyList<string> sources, string destDir, ConflictPolicy policy,
        ConflictResolver? ask = null, IProgress<FileOpProgress>? progress = null, CancellationToken ct = default)
        => Transfer(sources, destDir, policy, ask, progress, ct, move: false);

    public static FileOpResult MoveTo(IReadOnlyList<string> sources, string destDir, ConflictPolicy policy,
        ConflictResolver? ask = null, IProgress<FileOpProgress>? progress = null, CancellationToken ct = default)
        => Transfer(sources, destDir, policy, ask, progress, ct, move: true);

    private static FileOpResult Transfer(IReadOnlyList<string> sources, string destDir, ConflictPolicy policy,
        ConflictResolver? ask, IProgress<FileOpProgress>? progress, CancellationToken ct, bool move)
    {
        var result = new FileOpResult();
        destDir = Path.GetFullPath(destDir);
        if (!Directory.Exists(destDir))
        {
            result.Errors.Add((destDir, "Le dossier de destination n'existe pas."));
            return result;
        }

        long total = 0;
        foreach (var s in sources) total += SizeOf(s);

        // Espace disque : on refuse d'avance plutot que d'echouer a mi-chemin (copie ou deplacement entre volumes).
        bool needsSpace = !move || sources.Any(s => !SameVolume(s, destDir));
        if (needsSpace && !HasFreeSpace(destDir, total, out var free))
        {
            result.Errors.Add((destDir, $"Espace disque insuffisant : {FormatSize(total)} nécessaires, {FormatSize(free)} disponibles."));
            return result;
        }

        ConflictChoice? remembered = null;
        long doneBytes = 0;
        int index = 0;
        foreach (var src0 in sources)
        {
            if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
            index++;
            var src = Path.GetFullPath(src0);
            try
            {
                bool isDir = Directory.Exists(src);
                if (!isDir && !File.Exists(src)) { result.Errors.Add((src, "Élément introuvable (déplacé ou supprimé entre-temps).")); continue; }

                var name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
                var target = Path.Combine(destDir, name);

                if (isDir && PathSafety.IsWithin(src, destDir))
                {
                    result.Errors.Add((src, "Impossible de copier ou déplacer un dossier dans lui-même."));
                    continue;
                }
                if (PathSafety.SamePath(src, target))
                {
                    if (move) { result.Skipped++; continue; }   // deja dans ce dossier
                    target = NameConflict.UniquePath(target);   // « copier ici » = doublon numerote
                }
                else if (File.Exists(target) || Directory.Exists(target))
                {
                    var choice = Resolve(policy, ask, src, target, ref remembered);
                    if (choice == ConflictChoice.Cancel) { result.Cancelled = true; break; }
                    if (choice == ConflictChoice.Skip) { result.Skipped++; continue; }
                    if (choice == ConflictChoice.Rename) target = NameConflict.UniquePath(target);
                    if (choice == ConflictChoice.Replace && (isDir || Directory.Exists(target)))
                    {
                        // Remplacer un dossier entier est trop dangereux : on renomme.
                        Log.Info($"Remplacement de dossier refusé, renommage : {target}");
                        target = NameConflict.UniquePath(target);
                    }
                }

                progress?.Report(new FileOpProgress(name, doneBytes, total, index - 1, sources.Count));
                if (move && SameVolume(src, destDir))
                {
                    if (isDir) Directory.Move(src, target);
                    else if (File.Exists(target)) ReplaceFileAtomic(src, target, moveSource: true);
                    else File.Move(src, target);
                }
                else
                {
                    if (isDir) CopyDirectory(src, target, ct, ref doneBytes, total, progress);
                    else CopyFileSafe(src, target, ct, ref doneBytes, total, progress, name);
                    if (move)
                    {
                        try
                        {
                            if (isDir) Directory.Delete(src, recursive: true);
                            else File.Delete(src);
                        }
                        catch (Exception ex)
                        {
                            // La copie est complete et verifiee : on garde les deux exemplaires.
                            result.Errors.Add((src, $"Copié, mais l'original n'a pas pu être supprimé : {ex.Message}"));
                            result.Created.Add(target);
                            continue;
                        }
                    }
                }
                result.Created.Add(target);
                result.Done++;
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                break;
            }
            catch (UnauthorizedAccessException)
            {
                result.Errors.Add((src, "Accès refusé (droits insuffisants ou fichier en lecture seule)."));
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                result.Errors.Add((src, "Fichier verrouillé par une autre application."));
            }
            catch (Exception ex)
            {
                result.Errors.Add((src, ex.Message));
            }
        }
        progress?.Report(new FileOpProgress("", doneBytes, total, index, sources.Count));
        Log.Info($"{(move ? "Déplacement" : "Copie")} vers {destDir} : {result.Summary(move ? "déplacé(s)" : "copié(s)")}");
        return result;
    }

    private static ConflictChoice Resolve(ConflictPolicy policy, ConflictResolver? ask, string src, string target, ref ConflictChoice? remembered)
    {
        if (remembered is { } r) return r;
        switch (policy)
        {
            case ConflictPolicy.Rename: return ConflictChoice.Rename;
            case ConflictPolicy.Skip: return ConflictChoice.Skip;
            case ConflictPolicy.Overwrite: return ConflictChoice.Replace;
            default:
                if (ask is null) return ConflictChoice.Rename;
                var c = ask(src, target, out var all);
                if (all) remembered = c;
                return c;
        }
    }

    /// <summary>Copie un fichier via un temporaire voisin, avec verification de taille.</summary>
    public static void CopyFileSafe(string src, string target, CancellationToken ct, ref long doneBytes, long total,
        IProgress<FileOpProgress>? progress, string label)
    {
        var dir = Path.GetDirectoryName(target)!;
        var tmp = Path.Combine(dir, $".~fw-{Guid.NewGuid():N}.tmp");
        long expected;
        try
        {
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            {
                expected = input.Length;
                output.SetLength(expected);
                output.Position = 0;
                var buf = new byte[BufferSize];
                int n;
                long lastReport = 0;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buf, 0, n);
                    doneBytes += n;
                    if (progress is not null && doneBytes - lastReport > 8 * BufferSize)
                    {
                        lastReport = doneBytes;
                        progress.Report(new FileOpProgress(label, doneBytes, total, 0, 0));
                    }
                }
            }
            var written = new FileInfo(tmp).Length;
            if (written != expected) throw new IOException($"Copie incomplète ({written} / {expected} octets).");
            try { File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(src)); } catch { }
            if (File.Exists(target)) ReplaceFileAtomic(tmp, target, moveSource: true);
            else File.Move(tmp, target);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } }
        }
    }

    /// <summary>
    /// Remplace <paramref name="target"/> par <paramref name="source"/> : l'ancienne cible est d'abord
    /// mise de cote, puis supprimee seulement une fois la nouvelle en place (restauree sinon).
    /// </summary>
    private static void ReplaceFileAtomic(string source, string target, bool moveSource)
    {
        var backup = target + $".~fw-old-{Guid.NewGuid():N}";
        File.Move(target, backup);
        try
        {
            if (moveSource) File.Move(source, target);
            else File.Copy(source, target);
        }
        catch
        {
            if (!File.Exists(target)) File.Move(backup, target);
            throw;
        }
        try { File.Delete(backup); } catch { Log.Warn($"Ancienne version conservée : {backup}"); }
    }

    private static void CopyDirectory(string src, string target, CancellationToken ct, ref long doneBytes, long total, IProgress<FileOpProgress>? progress)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            ct.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget is not null) continue; // jonctions / liens : non suivis (boucles)
            CopyDirectory(dir, Path.Combine(target, info.Name), ct, ref doneBytes, total, progress);
        }
        foreach (var file in Directory.EnumerateFiles(src))
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(dest)) dest = NameConflict.UniquePath(dest);
            CopyFileSafe(file, dest, ct, ref doneBytes, total, progress, Path.GetFileName(file));
        }
    }

    /// <summary>Renomme un fichier ou dossier. Retourne le nouveau chemin.</summary>
    public static string Rename(string path, string newName)
    {
        var err = PathSafety.ValidateFileName(newName);
        if (err is not null) throw new ArgumentException(err);
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar))!;
        var target = Path.Combine(dir, newName);
        if (PathSafety.SamePath(full, target) && string.Equals(Path.GetFileName(full), newName, StringComparison.Ordinal))
            return full;
        bool caseOnly = PathSafety.SamePath(full, target);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
            throw new IOException($"« {newName} » existe déjà dans ce dossier.");
        if (Directory.Exists(full))
        {
            if (caseOnly) { var tmp = full + ".~fw-ren"; Directory.Move(full, tmp); Directory.Move(tmp, target); }
            else Directory.Move(full, target);
        }
        else
        {
            if (caseOnly) { var tmp = full + ".~fw-ren"; File.Move(full, tmp); File.Move(tmp, target); }
            else File.Move(full, target);
        }
        Log.Info($"Renommé : {Path.GetFileName(full)} -> {newName}");
        return target;
    }

    /// <summary>
    /// Noms pour un renommage par lot. Le modele accepte {nom} (nom d'origine sans extension),
    /// {n} (numero), {n:000} (numero sur 3 chiffres) ; l'extension d'origine est conservee.
    /// </summary>
    public static List<(string Source, string NewName)> PlanBatchRename(IReadOnlyList<string> paths, string pattern, int start = 1)
    {
        var plan = new List<(string, string)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int n = start;
        foreach (var p in paths)
        {
            var file = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar));
            var (stem, ext) = Directory.Exists(p) ? (file, "") : NameConflict.SplitName(file);
            var name = System.Text.RegularExpressions.Regex.Replace(pattern, @"\{n(?::(0+))?\}", m =>
                m.Groups[1].Success ? n.ToString(new string('0', m.Groups[1].Value.Length)) : n.ToString());
            name = name.Replace("{nom}", stem).Replace("{name}", stem);
            name = PathSafety.SanitizeFileName(name) + ext;
            var dir = Path.GetDirectoryName(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar))!;
            var candidate = Path.Combine(dir, name);
            while (used.Contains(candidate) || ((File.Exists(candidate) || Directory.Exists(candidate)) && !PathSafety.SamePath(candidate, p)))
                candidate = NameConflict.UniquePath(candidate, c => used.Contains(c) || File.Exists(c) || Directory.Exists(c));
            used.Add(candidate);
            plan.Add((p, Path.GetFileName(candidate)));
            n++;
        }
        return plan;
    }

    /// <summary>Suppression definitive (apres confirmation explicite cote interface).</summary>
    public static FileOpResult DeletePermanently(IReadOnlyList<string> paths)
    {
        var r = new FileOpResult();
        foreach (var p in paths)
        {
            try
            {
                if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
                else if (File.Exists(p)) File.Delete(p);
                else { r.Errors.Add((p, "Élément introuvable.")); continue; }
                r.Done++;
            }
            catch (UnauthorizedAccessException) { r.Errors.Add((p, "Accès refusé ou lecture seule.")); }
            catch (IOException ex) when (IsSharingViolation(ex)) { r.Errors.Add((p, "Fichier verrouillé par une autre application.")); }
            catch (Exception ex) { r.Errors.Add((p, ex.Message)); }
        }
        Log.Info($"Suppression définitive : {r.Summary("supprimé(s)")}");
        return r;
    }

    // ------------------------------------------------------------ utilitaires

    public static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path))
                return new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Sum(f => f.Length);
        }
        catch { }
        return 0;
    }

    public static bool SameVolume(string a, string b)
    {
        var ra = Path.GetPathRoot(Path.GetFullPath(a));
        var rb = Path.GetPathRoot(Path.GetFullPath(b));
        return string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasFreeSpace(string dir, long needed, out long free, long marginBytes = 64L << 20)
    {
        free = long.MaxValue;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (string.IsNullOrEmpty(root)) return true;
            free = new DriveInfo(root).AvailableFreeSpace;
            return free >= needed + marginBytes;
        }
        catch { return true; } // lecteur reseau sans information : on tente
    }

    public static bool IsSharingViolation(IOException ex) =>
        (ex.HResult & 0xFFFF) is 32 or 33; // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION

    public static string FormatSize(long bytes)
    {
        if (bytes == long.MaxValue) return "?";
        string[] u = ["o", "Kio", "Mio", "Gio", "Tio"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} o" : $"{v:0.#} {u[i]}";
    }
}
