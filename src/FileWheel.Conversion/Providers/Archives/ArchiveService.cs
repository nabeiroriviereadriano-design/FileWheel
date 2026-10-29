using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Formats;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;

namespace FileWheel.Conversion.Providers.Archives;

public sealed record ArchiveTestResult(bool Ok, int Entries, string Message);

public sealed record ArchiveFormatInfo(string Id, string Label, bool NeedsSevenZip, bool SupportsPassword, bool SingleFileOnly);

/// <summary>
/// Creation, extraction, verification et conversion d'archives.
/// ZIP, TAR, TAR.GZ et GZ sont traites par le code integre (.NET) ; 7Z, XZ, BZ2, les ZIP chiffres
/// et la lecture RAR passent par 7-Zip. Toutes les extractions appliquent les memes controles
/// (chemins confines, liens ignores, plafonds de taille et de taux de compression).
/// </summary>
public sealed class ArchiveService
{
    private readonly DependencyManager _deps;
    private readonly Func<ConversionSettings> _settings;

    public ArchiveService(DependencyManager deps, Func<ConversionSettings> settings)
    {
        _deps = deps;
        _settings = settings;
    }

    public static readonly IReadOnlyList<ArchiveFormatInfo> CreateFormats =
    [
        new("zip", "ZIP", false, true, false),
        new("7z", "7-Zip (7Z)", true, true, false),
        new("tgz", "TAR + GZip (TAR.GZ)", false, false, false),
        new("tar", "TAR (sans compression)", false, false, false),
        new("gz", "GZip (un seul fichier)", false, false, true),
        new("xz", "XZ (un seul fichier)", true, false, true),
        new("bz2", "BZip2 (un seul fichier)", true, false, true),
    ];

    public static readonly string[] ExtractFormats = ["zip", "7z", "tar", "tgz", "gz", "bz2", "xz", "rar"];

    public bool SevenZipAvailable => _deps.IsAvailable(EngineIds.SevenZip);

    /// <summary>La version installee de 7-Zip (7z.exe) sait lire le RAR ; la version autonome 7za ne le sait pas.</summary>
    public bool CanExtractRar
    {
        get
        {
            var p = _deps.PathOf(EngineIds.SevenZip);
            if (p is null) return false;
            var n = Path.GetFileName(p).ToLowerInvariant();
            return n is "7z.exe" or "7z" or "7zz" or "7zz.exe";
        }
    }

    private ExtractionLimits Limits()
    {
        var s = _settings();
        return new ExtractionLimits(s.ExtractMaxGiB << 30, s.ExtractMaxEntries, s.ExtractMaxRatio);
    }

    // ------------------------------------------------------------------ creation

    /// <summary>Cree une archive a cote du premier element (ou dans <paramref name="destDir"/>). Retourne son chemin.</summary>
    public async Task<string> CreateAsync(IReadOnlyList<string> items, string format, int level, string? password, string? destDir,
        string? name, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        if (items.Count == 0) throw new ConversionException(ConversionErrorKind.InvalidInput, "Rien à compresser.");
        var fmt = CreateFormats.FirstOrDefault(f => f.Id == format) ?? throw new ConversionException(ConversionErrorKind.NotSupported, "Format d'archive inconnu.");
        if (fmt.SingleFileOnly && (items.Count > 1 || Directory.Exists(items[0])))
        {
            // Plusieurs elements en GZ/XZ/BZ2 : on passe par une archive TAR (format .tar.gz etc.)
            if (format == "gz") { format = "tgz"; fmt = CreateFormats.First(f => f.Id == "tgz"); }
            else throw new ConversionException(ConversionErrorKind.NotSupported, $"{fmt.Label} ne contient qu'un seul fichier : choisissez 7Z, ZIP ou TAR.GZ pour plusieurs éléments.");
        }
        if (!string.IsNullOrEmpty(password) && !fmt.SupportsPassword)
            throw new ConversionException(ConversionErrorKind.NotSupported, $"{fmt.Label} ne permet pas de mot de passe : choisissez ZIP ou 7Z.");
        bool use7z = fmt.NeedsSevenZip || !string.IsNullOrEmpty(password);
        if (use7z) _deps.Require(EngineIds.SevenZip);

        var first = Path.GetFullPath(items[0].TrimEnd(Path.DirectorySeparatorChar));
        var dir = destDir ?? Path.GetDirectoryName(first)!;
        var baseName = name ?? (items.Count == 1 ? Path.GetFileName(first) : Path.GetFileName(Path.GetDirectoryName(first)!));
        if (string.IsNullOrEmpty(baseName)) baseName = "archive";
        if (format is "gz" or "xz" or "bz2") baseName = Path.GetFileName(first); // fichier.ext.gz
        else if (items.Count == 1 && File.Exists(first)) baseName = NameConflict.SplitName(Path.GetFileName(first)).stem;
        var ext = format switch { "tgz" => ".tar.gz", _ => "." + format };
        var final = NameConflict.UniquePath(Path.Combine(dir, PathSafety.SanitizeFileName(baseName) + ext));
        var temp = Path.Combine(dir, $"{OutputPlanner.TempPrefix}{Guid.NewGuid():N}{ext}");

        long total = items.Sum(SafeFileOps.SizeOf);
        if (!SafeFileOps.HasFreeSpace(dir, total / 2, out var free, _settings().MinFreeSpaceMiB << 20))
            throw new ConversionException(ConversionErrorKind.DiskFull, $"Espace disque insuffisant ({SafeFileOps.FormatSize(free)} disponibles).");
        try
        {
            if (use7z) await Create7z(items, format, level, password, temp, progress, ct).ConfigureAwait(false);
            else await Task.Run(() => CreateBuiltin(items, format, level, temp, total, progress, ct), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ConversionProgress(1, "Vérification de l'archive…"));
            var test = await TestAsync(temp, password, ct).ConfigureAwait(false);
            if (!test.Ok) throw new ConversionException(ConversionErrorKind.WriteError, "L'archive créée ne passe pas la vérification : " + test.Message);
            File.Move(temp, final);
            Log.Info($"Archive créée : {final} ({items.Count} élément(s), {format})");
            return final;
        }
        finally
        {
            if (File.Exists(temp)) { try { File.Delete(temp); } catch { } }
        }
    }

    private static void CreateBuiltin(IReadOnlyList<string> items, string format, int level, string temp, long total,
        IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var files = ExpandItems(items);
        long done = 0;
        void Report(string name) { if (total > 0) progress?.Report(new ConversionProgress(Math.Min(0.99, (double)done / total), name)); }

        switch (format)
        {
            case "zip":
            {
                var lvl = level switch { 0 => CompressionLevel.NoCompression, <= 3 => CompressionLevel.Fastest, <= 6 => CompressionLevel.Optimal, _ => CompressionLevel.SmallestSize };
                using var fs = new FileStream(temp, FileMode.CreateNew);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
                foreach (var (path, entryName, isDir) in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (isDir) { zip.CreateEntry(entryName.TrimEnd('/') + "/"); continue; }
                    var e = zip.CreateEntry(entryName, lvl);
                    try { e.LastWriteTime = File.GetLastWriteTime(path); } catch { }
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = e.Open();
                    CopyWithCancel(input, output, ct, ref done);
                    Report(entryName);
                }
                break;
            }
            case "tar":
            case "tgz":
            {
                using var fs = new FileStream(temp, FileMode.CreateNew);
                Stream outer = format == "tgz" ? new GZipStream(fs, level >= 7 ? CompressionLevel.SmallestSize : CompressionLevel.Optimal) : fs;
                using (outer)
                using (var tar = new TarWriter(outer, TarEntryFormat.Pax, leaveOpen: false))
                {
                    foreach (var (path, entryName, isDir) in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (isDir) { tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, entryName.TrimEnd('/') + "/")); continue; }
                        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = input };
                        try { entry.ModificationTime = File.GetLastWriteTimeUtc(path); } catch { }
                        tar.WriteEntry(entry);
                        done += input.Length;
                        Report(entryName);
                    }
                }
                break;
            }
            case "gz":
            {
                using var input = new FileStream(files[0].path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var fs = new FileStream(temp, FileMode.CreateNew);
                using var gz = new GZipStream(fs, level >= 7 ? CompressionLevel.SmallestSize : level == 0 ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                CopyWithCancel(input, gz, ct, ref done);
                break;
            }
            default:
                throw new ConversionException(ConversionErrorKind.NotSupported, "Format non pris en charge par le moteur intégré.");
        }
    }

    /// <summary>Liste plate (chemin, nom d'entree, dossier ?) ; liens et jonctions ignores.</summary>
    internal static List<(string path, string entryName, bool isDir)> ExpandItems(IReadOnlyList<string> items)
    {
        var list = new List<(string, string, bool)>();
        foreach (var item in items)
        {
            var full = Path.GetFullPath(item.TrimEnd(Path.DirectorySeparatorChar));
            var root = Path.GetFileName(full);
            if (File.Exists(full)) { list.Add((full, root, false)); continue; }
            if (!Directory.Exists(full)) continue;
            list.Add((full, root + "/", true));
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var d in Directory.EnumerateDirectories(full, "*", opts))
                list.Add((d, root + "/" + Path.GetRelativePath(full, d).Replace('\\', '/') + "/", true));
            foreach (var f in Directory.EnumerateFiles(full, "*", opts))
                list.Add((f, root + "/" + Path.GetRelativePath(full, f).Replace('\\', '/'), false));
        }
        return list;
    }

    private static void CopyWithCancel(Stream input, Stream output, CancellationToken ct, ref long done)
    {
        var buf = new byte[1 << 16];
        int n;
        while ((n = input.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            output.Write(buf, 0, n);
            done += n;
        }
    }

    private async Task Create7z(IReadOnlyList<string> items, string format, int level, string? password, string temp,
        IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var exe = _deps.Require(EngineIds.SevenZip);
        var type = format switch { "zip" => "zip", "7z" => "7z", "tar" => "tar", "gz" => "gzip", "bz2" => "bzip2", "xz" => "xz", _ => throw new ConversionException(ConversionErrorKind.NotSupported, "Format non pris en charge par 7-Zip.") };
        if (format == "tgz") throw new ConversionException(ConversionErrorKind.NotSupported, "TAR.GZ est créé par le moteur intégré.");
        var args = new List<string> { "a", "-t" + type, "-mx=" + Math.Clamp(level, 0, 9), "-y", "-bd", "-bb0", "-bsp1", "-ssw" };
        if (!string.IsNullOrEmpty(password))
        {
            args.Add("-p" + password);
            if (format == "7z") args.Add("-mhe=on");
            if (format == "zip") args.Add("-mem=AES256");
        }
        args.Add(temp);
        args.Add("--");
        args.AddRange(items.Select(i => Path.GetFullPath(i.TrimEnd(Path.DirectorySeparatorChar))));
        var r = await ProcessRunner.RunAsync(exe, args, ct, onStdOutLine: l => ParseSevenZipProgress(l, progress)).ConfigureAwait(false);
        if (r.ExitCode > 1) throw SevenZipError(r);
        if (r.ExitCode == 1) Log.Warn("7-Zip : avertissements pendant la compression : " + ProcessRunner.LastLines(r.StdErrTail + r.StdOutTail, 4));
    }

    // ------------------------------------------------------------------ extraction

    /// <summary>
    /// Extrait dans un NOUVEAU dossier (nom de l'archive, numerote si besoin) sous <paramref name="parentDir"/>.
    /// Rien n'est ecrase. Retourne le dossier cree.
    /// </summary>
    public async Task<string> ExtractAsync(string archive, string? parentDir, string? password, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var det = FormatDetector.Detect(archive);
        var fmt = det.Format?.Id;
        if (fmt is null || !ExtractFormats.Contains(fmt))
            throw new ConversionException(ConversionErrorKind.UnknownFormat, "Ce fichier n'est pas une archive reconnue.");
        if (det.IsExecutable) throw new ConversionException(ConversionErrorKind.Unsafe, "Les programmes (y compris auto-extractibles) ne sont pas extraits.");
        var dir = parentDir ?? Path.GetDirectoryName(Path.GetFullPath(archive))!;
        var stem = NameConflict.SplitName(Path.GetFileName(archive)).stem;
        if (fmt is "gz" or "bz2" or "xz" && stem.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
        var final = NameConflict.UniquePath(Path.Combine(dir, PathSafety.SanitizeFileName(stem)));
        var staging = Path.Combine(dir, $"{OutputPlanner.TempPrefix}{Guid.NewGuid():N}");
        long size = new FileInfo(archive).Length;
        if (!SafeFileOps.HasFreeSpace(dir, size * 2, out var free, _settings().MinFreeSpaceMiB << 20))
            throw new ConversionException(ConversionErrorKind.DiskFull, $"Espace disque probablement insuffisant ({SafeFileOps.FormatSize(free)} disponibles).");
        Directory.CreateDirectory(staging);
        try
        {
            var limits = Limits();
            bool builtin = string.IsNullOrEmpty(password) && fmt is "zip" or "tar" or "tgz" || (fmt == "gz" && string.IsNullOrEmpty(password));
            ArchiveSafety.Report? report = null;
            if (builtin)
            {
                try
                {
                report = await Task.Run(() =>
                {
                    switch (fmt)
                    {
                        case "zip":
                            try { return ArchiveSafety.ExtractZip(archive, staging, limits, ct, progress); }
                            catch (ConversionException ex) when (ex.Kind == ConversionErrorKind.Corrupt && SevenZipAvailable) { throw new Retry7z(); }
                            catch (NotSupportedException) when (SevenZipAvailable) { throw new Retry7z(); } // methode de compression non geree (Deflate64, chiffrement)
                        case "tar":
                        {
                            using var fs = File.OpenRead(archive);
                            return ArchiveSafety.ExtractTar(fs, size, staging, limits, ct, progress);
                        }
                        default:
                        {
                            using var fs = File.OpenRead(archive);
                            using var gz = new GZipStream(fs, CompressionMode.Decompress);
                            if (fmt == "tgz" || IsTarInsideGzip(archive))
                                return ArchiveSafety.ExtractTar(gz, size, staging, limits, ct, progress);
                            // Fichier .gz simple : un seul fichier
                            var outName = PathSafety.SanitizeFileName(stem);
                            using var output = new FileStream(Path.Combine(staging, outName), FileMode.CreateNew);
                            var buf = new byte[1 << 16];
                            long written = 0;
                            int n;
                            while ((n = gz.Read(buf, 0, buf.Length)) > 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                output.Write(buf, 0, n);
                                written += n;
                                if (written > limits.MaxTotalBytes || (written > (16L << 20) && written / Math.Max(1, size) > limits.MaxRatio))
                                    throw new ConversionException(ConversionErrorKind.Unsafe, "Décompression anormale (taille ou taux excessif) : arrêtée par sécurité.");
                            }
                            return new ArchiveSafety.Report { Files = 1, Bytes = written };
                        }
                    }
                }, ct).ConfigureAwait(false);
                }
                catch (Retry7z)
                {
                    // Methode de compression non geree par .NET (Deflate64, chiffrement...) : 7-Zip prend le relais.
                    report = null;
                    foreach (var e in Directory.EnumerateFileSystemEntries(staging)) { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); }
                }
            }
            if (report is null)
            {
                if (fmt == "rar" && !CanExtractRar)
                    throw new ConversionException(ConversionErrorKind.EngineMissing, "L'extraction RAR nécessite 7-Zip installé sur le PC (la version autonome ne lit pas le RAR).");
                report = await Extract7z(archive, staging, password, limits, progress, ct).ConfigureAwait(false);
            }

            // Si l'archive contient un unique dossier racine, on evite « dossier/dossier »
            var entries = Directory.GetFileSystemEntries(staging);
            if (entries.Length == 0) throw new ConversionException(ConversionErrorKind.Corrupt, "L'archive est vide.");
            string? single = entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : null;
            if (single is not null)
            {
                final = NameConflict.UniquePath(Path.Combine(dir, Path.GetFileName(single)));
                Directory.Move(single, final);
            }
            else Directory.Move(staging, final);
            if (report.Skipped.Count > 0)
                Log.Warn($"Extraction : {report.Skipped.Count} entrée(s) ignorée(s) par sécurité : {string.Join(", ", report.Skipped.Take(10))}");
            Log.Info($"Archive extraite : {archive} -> {final} ({report.Files} fichier(s), {SafeFileOps.FormatSize(report.Bytes)})");
            LastSkipped = report.Skipped.ToList();
            return final;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    /// <summary>Entrees ignorees lors de la derniere extraction (chemins dangereux, liens).</summary>
    public List<string> LastSkipped { get; private set; } = new();

    private sealed class Retry7z : Exception;

    private static bool IsTarInsideGzip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            var head = new byte[512];
            int read = 0;
            while (read < 512) { int n = gz.Read(head, read, 512 - read); if (n == 0) break; read += n; }
            return read == 512 && System.Text.Encoding.ASCII.GetString(head, 257, 5) == "ustar";
        }
        catch { return false; }
    }

    private async Task<ArchiveSafety.Report> Extract7z(string archive, string staging, string? password, ExtractionLimits limits,
        IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var exe = _deps.Require(EngineIds.SevenZip);
        // 1. Inventaire : chemins, tailles, liens
        var list = await ProcessRunner.RunAsync(exe, ["l", "-slt", "-ba", "-p" + (password ?? ""), "--", archive], ct, tailChars: 8_000_000).ConfigureAwait(false);
        if (list.ExitCode > 1) throw SevenZipError(list);
        var report = new ArchiveSafety.Report();
        long declared = 0;
        int count = 0;
        foreach (var block in Regex.Split(list.StdOutTail, @"\r?\n\r?\n"))
        {
            var path = Regex.Match(block, @"^Path = (.+)$", RegexOptions.Multiline);
            if (!path.Success) continue;
            count++;
            var entryName = path.Groups[1].Value.Trim();
            if (PathSafety.ResolveArchiveEntry(staging, entryName) is null)
                throw new ConversionException(ConversionErrorKind.Unsafe, $"L'archive contient un chemin dangereux (« {entryName} ») : extraction refusée.");
            var sz = Regex.Match(block, @"^Size = (\d+)", RegexOptions.Multiline);
            if (sz.Success && long.TryParse(sz.Groups[1].Value, out var s)) declared += s;
            if (Regex.IsMatch(block, @"^Symbolic Link = .+", RegexOptions.Multiline) || Regex.IsMatch(block, @"^Attributes = .*\bl", RegexOptions.Multiline))
                report.Skipped.Add(entryName + " (lien)");
        }
        if (count > limits.MaxEntries)
            throw new ConversionException(ConversionErrorKind.Unsafe, $"Trop d'entrées ({count}, limite {limits.MaxEntries}) : extraction refusée.");
        if (declared > limits.MaxTotalBytes)
            throw new ConversionException(ConversionErrorKind.Unsafe, $"Contenu annoncé de {SafeFileOps.FormatSize(declared)}, au-delà de la limite de {SafeFileOps.FormatSize(limits.MaxTotalBytes)} : extraction refusée.");
        long size = Math.Max(1, new FileInfo(archive).Length);
        if (declared > (16L << 20) && declared / size > limits.MaxRatio)
            throw new ConversionException(ConversionErrorKind.Unsafe, $"Taux de compression anormal (×{declared / size}) : archive potentiellement piégée, extraction refusée.");

        // 2. Extraction dans le dossier de travail
        var x = await ProcessRunner.RunAsync(exe, ["x", "-y", "-bd", "-bb0", "-bsp1", "-p" + (password ?? ""), "-o" + staging, "--", archive], ct,
            onStdOutLine: l => ParseSevenZipProgress(l, progress)).ConfigureAwait(false);
        if (x.ExitCode > 1) throw SevenZipError(x);

        // 3. Controle a posteriori : tout est sous le dossier, pas de lien, taille reelle bornee
        foreach (var p in Directory.EnumerateFileSystemEntries(staging, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            var info = new FileInfo(p);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (Directory.Exists(p)) Directory.Delete(p); else File.Delete(p);
                report.Skipped.Add(Path.GetRelativePath(staging, p) + " (lien supprimé)");
                continue;
            }
            if (!PathSafety.IsWithin(staging, p)) throw new ConversionException(ConversionErrorKind.Unsafe, "Écriture hors du dossier détectée : extraction annulée.");
            if (File.Exists(p)) { report.Files++; report.Bytes += info.Length; }
            else report.Directories++;
        }
        return report;
    }

    // ------------------------------------------------------------------ verification

    public async Task<ArchiveTestResult> TestAsync(string archive, string? password, CancellationToken ct)
    {
        var fmt = FormatDetector.Detect(archive).Format?.Id;
        if (string.IsNullOrEmpty(password) && fmt is "zip" or "tar" or "tgz" or "gz")
        {
            try { return await Task.Run(() => TestBuiltin(archive, fmt!, ct), ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is NotSupportedException && SevenZipAvailable) { /* methode non geree : 7-Zip */ }
        }
        var exe = _deps.Require(EngineIds.SevenZip);
        var r = await ProcessRunner.RunAsync(exe, ["t", "-bd", "-bb0", "-p" + (password ?? ""), "--", archive], ct).ConfigureAwait(false);
        var outText = r.StdOutTail + r.StdErrTail;
        if (r.ExitCode == 0) return new ArchiveTestResult(true, CountFrom7z(outText), "Aucune erreur détectée.");
        if (outText.Contains("Wrong password", StringComparison.OrdinalIgnoreCase))
            return new ArchiveTestResult(false, 0, "Mot de passe incorrect ou manquant.");
        return new ArchiveTestResult(false, 0, "Erreurs détectées : " + ProcessRunner.LastLines(outText, 3));
    }

    private static ArchiveTestResult TestBuiltin(string archive, string fmt, CancellationToken ct)
    {
        var buf = new byte[1 << 16];
        int count = 0;
        try
        {
            if (fmt == "zip")
            {
                using var zip = ZipFile.OpenRead(archive);
                foreach (var e in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    count++;
                    if (e.FullName.EndsWith('/')) continue;
                    uint crc = 0;
                    using var s = e.Open();
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) crc = Crc32.Update(crc, buf.AsSpan(0, n));
                    if (crc != e.Crc32) return new ArchiveTestResult(false, count, $"Somme de contrôle incorrecte pour « {e.FullName} ».");
                }
                return new ArchiveTestResult(true, count, "Aucune erreur détectée (sommes de contrôle CRC-32 vérifiées).");
            }
            using var fs = File.OpenRead(archive);
            Stream src = fmt == "tar" ? fs : new GZipStream(fs, CompressionMode.Decompress);
            using (src)
            {
                if (fmt == "gz" && !IsTarInsideGzip(archive))
                {
                    while (src.Read(buf, 0, buf.Length) > 0) ct.ThrowIfCancellationRequested();
                    return new ArchiveTestResult(true, 1, "Aucune erreur détectée (contrôle GZip vérifié).");
                }
                using var tar = new TarReader(src);
                while (tar.GetNextEntry() is { } e)
                {
                    ct.ThrowIfCancellationRequested();
                    count++;
                    if (e.DataStream is not null) while (e.DataStream.Read(buf, 0, buf.Length) > 0) { }
                }
            }
            return new ArchiveTestResult(true, count, "Aucune erreur détectée.");
        }
        catch (InvalidDataException ex) { return new ArchiveTestResult(false, count, "Archive endommagée : " + ex.Message); }
        catch (EndOfStreamException) { return new ArchiveTestResult(false, count, "Archive tronquée."); }
    }

    private static int CountFrom7z(string text)
    {
        var m = Regex.Match(text, @"Files: (\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ------------------------------------------------------------------ conversion d'archive

    public async Task<string> RecompressAsync(string archive, string targetFormat, int level, string? password, string? sourcePassword,
        IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var workRoot = Path.Combine(FileWheel.Core.Util.AppPaths.TempDir, "recompress-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workRoot);
        try
        {
            progress?.Report(new ConversionProgress(null, "Extraction…"));
            var extracted = await ExtractAsync(archive, workRoot, sourcePassword, null, ct).ConfigureAwait(false);
            var items = Directory.Exists(extracted) && Path.GetFileName(extracted) == NameConflict.SplitName(Path.GetFileName(archive)).stem
                ? Directory.GetFileSystemEntries(extracted).ToList()
                : [extracted];
            progress?.Report(new ConversionProgress(null, "Recompression…"));
            var stem = NameConflict.SplitName(Path.GetFileName(archive)).stem;
            if (stem.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
            return await CreateAsync(items, targetFormat, level, password, Path.GetDirectoryName(Path.GetFullPath(archive)), stem, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            try { Directory.Delete(workRoot, true); } catch { }
        }
    }

    private static void ParseSevenZipProgress(string line, IProgress<ConversionProgress>? progress)
    {
        var m = Regex.Match(line, @"^\s*(\d{1,3})%");
        if (m.Success && progress is not null) progress.Report(new ConversionProgress(int.Parse(m.Groups[1].Value) / 100.0, "7-Zip…"));
    }

    private static ConversionException SevenZipError(ProcessResult r)
    {
        var text = r.StdErrTail + "\n" + r.StdOutTail;
        if (text.Contains("Wrong password", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.Protected, "Mot de passe incorrect ou manquant.", ProcessRunner.LastLines(text, 3));
        if (text.Contains("Can not open the file as archive", StringComparison.OrdinalIgnoreCase) || text.Contains("Unexpected end", StringComparison.OrdinalIgnoreCase) || text.Contains("Data Error", StringComparison.OrdinalIgnoreCase) || text.Contains("CRC Failed", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.Corrupt, "Archive endommagée ou illisible.", ProcessRunner.LastLines(text, 3));
        if (text.Contains("There is not enough space", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.DiskFull, "Disque plein.", ProcessRunner.LastLines(text, 3));
        if (text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.PermissionDenied, "Accès refusé.", ProcessRunner.LastLines(text, 3));
        if (r.ExitCode == 8) return new ConversionException(ConversionErrorKind.TooLarge, "Mémoire insuffisante pour 7-Zip.");
        return new ConversionException(ConversionErrorKind.EngineCrashed, $"7-Zip a échoué (code {r.ExitCode}).", ProcessRunner.LastLines(text, 4));
    }
}
