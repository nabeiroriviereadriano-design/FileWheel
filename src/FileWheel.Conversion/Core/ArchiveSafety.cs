using System.Formats.Tar;
using System.IO.Compression;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Logging;

namespace FileWheel.Conversion.Core;

/// <summary>Limites appliquees a toute extraction (protection contre les « bombes » de decompression).</summary>
public sealed record ExtractionLimits(long MaxTotalBytes, int MaxEntries, int MaxRatio)
{
    public static ExtractionLimits Default => new(20L << 30, 200_000, 250);
}

/// <summary>
/// Extraction securisee des archives ZIP et TAR (integree, sans moteur externe) :
/// - chemins d'entree confines au dossier cible (« zip slip » : ../, chemins absolus, lecteurs,
///   flux NTFS refuses) ;
/// - liens symboliques et materiels des archives TAR ignores ;
/// - plafonds de taille totale, de nombre d'entrees et de taux de compression, verifies sur les
///   octets REELLEMENT ecrits (les tailles annoncees par l'archive peuvent mentir) ;
/// - extraction dans un dossier temporaire, renomme seulement en cas de succes.
/// </summary>
public static class ArchiveSafety
{
    public sealed class Report
    {
        public int Files { get; set; }
        public int Directories { get; set; }
        public long Bytes { get; set; }
        public List<string> Skipped { get; } = new();
    }

    public static Report ExtractZip(string zipPath, string destDir, ExtractionLimits limits, CancellationToken ct,
        IProgress<ConversionProgress>? progress = null, string? password = null)
    {
        if (password is not null)
            throw new ConversionException(ConversionErrorKind.NotSupported, "Les ZIP chiffrés nécessitent le moteur 7-Zip.");
        var report = new Report();
        Directory.CreateDirectory(destDir);
        long compressedTotal = Math.Max(1, new FileInfo(zipPath).Length);
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(zipPath); }
        catch (InvalidDataException ex) { throw new ConversionException(ConversionErrorKind.Corrupt, "Archive ZIP endommagée ou illisible.", ex.Message, ex); }
        using (zip)
        {
            if (zip.Entries.Count > limits.MaxEntries)
                throw new ConversionException(ConversionErrorKind.Unsafe, $"L'archive contient {zip.Entries.Count} entrées (limite : {limits.MaxEntries}). Extraction refusée par sécurité.");
            long declared = zip.Entries.Sum(e => e.Length);
            CheckTotals(declared, compressedTotal, limits, declaredOnly: true);
            int i = 0;
            var buf = new byte[1 << 16];
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                i++;
                var target = PathSafety.ResolveArchiveEntry(destDir, entry.FullName);
                if (target is null)
                {
                    report.Skipped.Add(entry.FullName);
                    Log.Warn($"Entrée d'archive refusée (chemin dangereux) : {entry.FullName}");
                    continue;
                }
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    report.Directories++;
                    continue;
                }
                // Attributs Unix : lien symbolique (0xA000) => ignore
                int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixMode == 0xA000) { report.Skipped.Add(entry.FullName + " (lien)"); continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target)) target = NameConflict.UniquePath(target);
                try
                {
                    using var input = entry.Open();
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(buf, 0, n);
                        report.Bytes += n;
                        CheckTotals(report.Bytes, compressedTotal, limits, declaredOnly: false);
                    }
                }
                catch (InvalidDataException ex)
                {
                    throw new ConversionException(ConversionErrorKind.Corrupt, $"Données endommagées dans « {entry.FullName} ».", ex.Message, ex);
                }
                try { File.SetLastWriteTime(target, entry.LastWriteTime.DateTime); } catch { }
                report.Files++;
                progress?.Report(new ConversionProgress((double)i / zip.Entries.Count, entry.Name));
            }
        }
        return report;
    }

    public static Report ExtractTar(Stream tarStream, long compressedSize, string destDir, ExtractionLimits limits, CancellationToken ct,
        IProgress<ConversionProgress>? progress = null)
    {
        var report = new Report();
        Directory.CreateDirectory(destDir);
        using var reader = new TarReader(tarStream, leaveOpen: true);
        int count = 0;
        TarEntry? entry;
        try
        {
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (++count > limits.MaxEntries)
                    throw new ConversionException(ConversionErrorKind.Unsafe, $"Trop d'entrées dans l'archive (limite : {limits.MaxEntries}).");
                var target = PathSafety.ResolveArchiveEntry(destDir, entry.Name);
                if (target is null) { report.Skipped.Add(entry.Name); Log.Warn($"Entrée TAR refusée : {entry.Name}"); continue; }
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(target);
                        report.Directories++;
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (File.Exists(target)) target = NameConflict.UniquePath(target);
                        if (entry.DataStream is not null)
                        {
                            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            var buf = new byte[1 << 16];
                            int n;
                            while ((n = entry.DataStream.Read(buf, 0, buf.Length)) > 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                output.Write(buf, 0, n);
                                report.Bytes += n;
                                CheckTotals(report.Bytes, Math.Max(1, compressedSize), limits, declaredOnly: false);
                            }
                        }
                        else File.Create(target).Dispose();
                        report.Files++;
                        progress?.Report(new ConversionProgress(null, entry.Name));
                        break;
                    default:
                        // Liens symboliques/materiels, peripheriques, FIFO : jamais recrees.
                        report.Skipped.Add($"{entry.Name} ({entry.EntryType})");
                        break;
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ConversionException(ConversionErrorKind.Corrupt, "Archive TAR endommagée.", ex.Message, ex);
        }
        return report;
    }

    private static void CheckTotals(long bytes, long compressed, ExtractionLimits limits, bool declaredOnly)
    {
        if (bytes > limits.MaxTotalBytes)
            throw new ConversionException(ConversionErrorKind.Unsafe,
                $"Le contenu décompressé dépasse la limite de {SafeFileOps.FormatSize(limits.MaxTotalBytes)}. Extraction arrêtée par sécurité (Paramètres › Conversion pour changer la limite).");
        // Le taux n'est significatif qu'au-dela de quelques Mio
        if (bytes > (16L << 20) && bytes / compressed > limits.MaxRatio)
            throw new ConversionException(ConversionErrorKind.Unsafe,
                $"Taux de compression anormal (×{bytes / compressed}) : archive potentiellement piégée (« bombe de décompression »). Extraction arrêtée.");
        _ = declaredOnly;
    }
}
