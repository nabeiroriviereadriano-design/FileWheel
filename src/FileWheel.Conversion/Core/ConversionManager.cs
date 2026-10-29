using System.Diagnostics;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Formats;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;
using FileWheel.Core.Util;

namespace FileWheel.Conversion.Core;

/// <summary>Tout ce qu'il faut pour lancer une conversion.</summary>
public sealed class ConversionOrder
{
    public required IReadOnlyList<string> Inputs { get; init; }
    public required string CapabilityKey { get; init; }
    public Dictionary<string, string> Options { get; init; } = new();
    /// <summary>Dossier de sortie (null = parametre par defaut).</summary>
    public string? OutputDir { get; init; }
    public string? OutputName { get; init; }
    public ConflictPolicy? Conflicts { get; init; }
}

/// <summary>Preparation calculee avant le lancement (affichee dans la fenetre de conversion).</summary>
public sealed class PreparedConversion
{
    public required ConversionOrder Order { get; init; }
    public required ConversionCapability Capability { get; init; }
    public required IConversionProvider Provider { get; init; }
    public required DetectionResult Detection { get; init; }
    public required ConversionAnalysis Analysis { get; init; }
    public required ConversionRequest Request { get; init; }
}

/// <summary>
/// Orchestre une conversion de bout en bout : verification des entrees, detection du format,
/// choix du fournisseur, plan de sortie, controle de l'espace disque, execution, validation
/// du resultat, mise en place atomique, nettoyage, historique.
///
/// Le fichier source n'est jamais ouvert en ecriture ; sa taille et sa date sont controlees
/// apres la conversion (alerte si un moteur l'avait modifie).
/// </summary>
public sealed class ConversionManager
{
    public ConversionRegistry Registry { get; }
    public ConversionHistory History { get; }
    public Func<ConversionSettings> Settings { get; set; }

    public ConversionManager(ConversionRegistry registry, ConversionHistory history, Func<ConversionSettings> settings)
    {
        Registry = registry;
        History = history;
        Settings = settings;
    }

    public PreparedConversion Prepare(ConversionOrder order)
    {
        if (order.Inputs.Count == 0) throw new ConversionException(ConversionErrorKind.InvalidInput, "Aucun fichier à convertir.");
        var cfg = Settings();
        foreach (var i in order.Inputs)
        {
            if (Directory.Exists(i)) throw new ConversionException(ConversionErrorKind.InvalidInput, $"« {Path.GetFileName(i)} » est un dossier : seuls les fichiers se convertissent.");
            if (!File.Exists(i)) throw new ConversionException(ConversionErrorKind.InvalidInput, $"Fichier introuvable : {Path.GetFileName(i)}");
            if (cfg.MaxInputMiB > 0 && new FileInfo(i).Length > cfg.MaxInputMiB << 20)
                throw new ConversionException(ConversionErrorKind.TooLarge, $"« {Path.GetFileName(i)} » dépasse la taille maximale réglée ({cfg.MaxInputMiB} Mio).");
        }

        var cap = Registry.ByKey(order.CapabilityKey)
                  ?? throw new ConversionException(ConversionErrorKind.NotSupported, "Cette conversion n'est pas proposée par les moteurs installés.");
        var detection = FormatDetector.Detect(order.Inputs[0]);
        if (detection.IsExecutable)
            throw new ConversionException(ConversionErrorKind.Unsafe, "Les programmes et scripts ne sont jamais convertis.");
        var src = detection.Format ?? throw new ConversionException(ConversionErrorKind.UnknownFormat, "Format non reconnu : conversion impossible.");
        if (!cap.From.Contains(src.Id))
            throw new ConversionException(ConversionErrorKind.NotSupported,
                $"Le contenu réel de « {Path.GetFileName(order.Inputs[0])} » est {src.Label} : la conversion choisie ne s'applique pas à ce format.");
        if (order.Inputs.Count > 1 && !cap.MultiInput)
            throw new ConversionException(ConversionErrorKind.InvalidInput, "Cette conversion traite un fichier à la fois (utilisez la conversion par lot).");
        if (order.Inputs.Count > 1)
        {
            foreach (var other in order.Inputs.Skip(1))
            {
                var d = FormatDetector.Detect(other);
                if (d.IsExecutable || d.Format is null || !cap.From.Contains(d.Format.Id))
                    throw new ConversionException(ConversionErrorKind.NotSupported, $"« {Path.GetFileName(other)} » n'est pas compatible avec cette conversion.");
            }
        }
        if (Registry.MissingEngine(cap) is { } missing)
            Registry.Dependencies.Require(missing); // leve l'erreur « moteur absent »

        var provider = Registry.Provider(cap.ProviderId);
        var options = new Dictionary<string, string>(order.Options);
        ValidateOptions(cap, options);
        var request = new ConversionRequest { Inputs = order.Inputs, SourceFormat = src, Capability = cap, Options = options };
        var analysis = provider.Analyze(request);
        if (detection.Warning is not null) analysis.Warnings.Insert(0, detection.Warning);
        return new PreparedConversion { Order = order, Capability = cap, Provider = provider, Detection = detection, Analysis = analysis, Request = request };
    }

    private static void ValidateOptions(ConversionCapability cap, Dictionary<string, string> options)
    {
        foreach (var key in options.Keys.ToList())
        {
            var def = cap.Options.FirstOrDefault(o => o.Key == key);
            if (def is null) { options.Remove(key); continue; } // reglage inconnu : ignore
            var v = options[key] ?? "";
            switch (def.Kind)
            {
                case OptionKind.Int:
                    if (v.Length == 0) { options[key] = def.Default; break; }
                    if (!int.TryParse(v, out var n) || n < def.Min || n > def.Max)
                        throw new ConversionException(ConversionErrorKind.InvalidOptions, $"« {def.Label} » doit être un nombre entre {def.Min} et {def.Max}.");
                    break;
                case OptionKind.Choice:
                    if (!def.Choices.Any(c => c.Value == v))
                        throw new ConversionException(ConversionErrorKind.InvalidOptions, $"Valeur invalide pour « {def.Label} ».");
                    break;
                case OptionKind.Bool:
                    if (v is not ("true" or "false")) options[key] = def.Default;
                    break;
                case OptionKind.Time:
                    if (v.Length > 0 && TimeParser.Parse(v) is null)
                        throw new ConversionException(ConversionErrorKind.InvalidOptions, $"« {def.Label} » : durée invalide (exemples : 90, 1:30, 00:01:30.5).");
                    break;
                case OptionKind.Text:
                    if (v.Length > 500 || v.Contains('\0'))
                        throw new ConversionException(ConversionErrorKind.InvalidOptions, $"« {def.Label} » : valeur trop longue.");
                    break;
            }
        }
    }

    /// <summary>Execute une conversion preparee. Ne leve que des ConversionException (ou OperationCanceledException).</summary>
    public async Task<ConversionResult> RunAsync(PreparedConversion prep, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var cfg = Settings();
        var order = prep.Order;
        var cap = prep.Capability;
        var sw = Stopwatch.StartNew();
        var history = new HistoryEntry
        {
            Inputs = order.Inputs.ToList(), SourceFormat = prep.Request.SourceFormat.Id, TargetFormat = cap.To, CapabilityKey = cap.Key,
            Options = new Dictionary<string, string>(order.Options),
        };
        var snapshots = order.Inputs.Select(i => (Path: i, Size: new FileInfo(i).Length, Time: File.GetLastWriteTimeUtc(i))).ToList();
        OutputPlan? plan = null;
        string workDir = Path.Combine(AppPaths.TempDir, "job-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            var outDir = order.OutputDir ?? (string.IsNullOrWhiteSpace(cfg.DefaultOutputDir) ? null : cfg.DefaultOutputDir);
            plan = OutputPlanner.Plan(order.Inputs, cap.OutputIsFolder ? KindLabel(cap) : cap.To, cap.OutputIsFolder, outDir,
                cfg.Suffix, order.Conflicts ?? cfg.Conflicts, order.OutputName);

            long inputSize = snapshots.Sum(s => s.Size);
            long estimate = prep.Analysis.EstimatedBytes ?? Math.Max(inputSize * (cap.Kind is ConversionKind.Extract or ConversionKind.PdfPages or ConversionKind.ImageSequence ? 4 : 2), 8L << 20);
            if (!SafeFileOps.HasFreeSpace(Path.GetDirectoryName(plan.FinalPath)!, estimate, out var free, cfg.MinFreeSpaceMiB << 20))
                throw new ConversionException(ConversionErrorKind.DiskFull,
                    $"Espace disque insuffisant sur la destination : environ {SafeFileOps.FormatSize(estimate)} nécessaires (+ {cfg.MinFreeSpaceMiB} Mio de marge), {SafeFileOps.FormatSize(free)} disponibles.");

            Directory.CreateDirectory(workDir);
            if (plan.IsFolder) Directory.CreateDirectory(plan.TempPath);
            prep.Request.TempOutput = plan.TempPath;
            prep.Request.WorkDir = workDir;
            prep.Request.FinalOutput = plan.FinalPath;

            progress.Report(new ConversionProgress(null, "Démarrage…"));
            Log.Info($"Conversion {cap.Key} : {string.Join(", ", order.Inputs.Select(Path.GetFileName))} -> {Path.GetFileName(plan.FinalPath)}");
            ConversionResult result;
            try
            {
                result = await prep.Provider.ConvertAsync(prep.Request, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw Translate(ex);
            }
            ct.ThrowIfCancellationRequested();

            // Validation puis mise en place atomique
            progress.Report(new ConversionProgress(1, "Vérification du résultat…"));
            if (plan.IsFolder) OutputValidator.ValidateFolder(plan.TempPath);
            else OutputValidator.ValidateFile(plan.TempPath, cap.To);

            var finalPath = plan.FinalPath;
            if (plan.IsFolder)
            {
                if (Directory.Exists(finalPath) || File.Exists(finalPath)) finalPath = NameConflict.UniquePath(finalPath);
                Directory.Move(plan.TempPath, finalPath);
            }
            else if (plan.ReplaceExisting && File.Exists(finalPath))
            {
                var backup = finalPath + ".~fw-old";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(finalPath, backup);
                try { File.Move(plan.TempPath, finalPath); }
                catch { File.Move(backup, finalPath); throw; }
                File.Delete(backup);
            }
            else
            {
                if (File.Exists(finalPath)) finalPath = NameConflict.UniquePath(finalPath); // apparu pendant la conversion
                File.Move(plan.TempPath, finalPath);
            }

            var outputs = plan.IsFolder
                ? Directory.EnumerateFiles(finalPath, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                : [finalPath];
            CheckSourcesUntouched(snapshots);
            var warnings = prep.Analysis.Warnings.Concat(result.Warnings).Distinct().ToList();
            history.Result = "Réussie";
            history.Outputs = plan.IsFolder ? [finalPath] : outputs;
            Log.Info($"Conversion réussie en {sw.Elapsed.TotalSeconds:0.0} s : {finalPath}");
            return new ConversionResult(plan.IsFolder ? [finalPath] : outputs, warnings);
        }
        catch (OperationCanceledException)
        {
            history.Result = "Annulée";
            Log.Info("Conversion annulée par l'utilisateur.");
            throw;
        }
        catch (ConversionException ex)
        {
            history.Result = ex.Kind == ConversionErrorKind.NameConflict ? "Ignorée" : "Échec";
            history.Error = ex.Message;
            Log.Warn($"Échec de conversion ({ex.Kind}) : {ex.Message}{(ex.Technical is null ? "" : " | " + ex.Technical)}");
            throw;
        }
        catch (Exception ex)
        {
            var t = Translate(ex);
            history.Result = "Échec";
            history.Error = t.Message;
            Log.Error("Échec de conversion inattendu", ex);
            throw t;
        }
        finally
        {
            sw.Stop();
            history.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
            // Nettoyage : temporaire de sortie (sauf s'il a ete mis en place) et dossier de travail
            if (plan is not null)
            {
                try
                {
                    if (plan.IsFolder && Directory.Exists(plan.TempPath)) Directory.Delete(plan.TempPath, true);
                    else if (!plan.IsFolder && File.Exists(plan.TempPath)) File.Delete(plan.TempPath);
                }
                catch (Exception ex) { Log.Warn("Temporaire non supprimé : " + ex.Message); }
            }
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
            History.Add(history);
        }
    }

    private static string KindLabel(ConversionCapability cap) => cap.Kind switch
    {
        ConversionKind.Extract => "extract",
        ConversionKind.PdfPages => "pages",
        ConversionKind.ImageSequence => "images",
        ConversionKind.Split => "fichiers",
        _ => cap.To,
    };

    private static void CheckSourcesUntouched(List<(string Path, long Size, DateTime Time)> snapshots)
    {
        foreach (var s in snapshots)
        {
            try
            {
                var fi = new FileInfo(s.Path);
                if (!fi.Exists || fi.Length != s.Size || fi.LastWriteTimeUtc != s.Time)
                    Log.Error($"ALERTE : le fichier source a changé pendant la conversion : {s.Path}");
            }
            catch { }
        }
    }

    public static ConversionException Translate(Exception ex)
    {
        if (ex is ConversionException ce) return ce;
        if (ex is UnauthorizedAccessException)
            return new ConversionException(ConversionErrorKind.PermissionDenied, "Accès refusé : droits insuffisants sur le fichier ou le dossier de destination.", ex.Message, ex);
        if (ex is IOException io)
        {
            int code = io.HResult & 0xFFFF;
            if (code is 0x70 or 0x27) return new ConversionException(ConversionErrorKind.DiskFull, "Le disque est plein.", io.Message, io);
            if (code is 32 or 33) return new ConversionException(ConversionErrorKind.FileLocked, "Fichier verrouillé par une autre application.", io.Message, io);
            if (io is FileNotFoundException or DirectoryNotFoundException) return new ConversionException(ConversionErrorKind.InvalidInput, "Fichier ou dossier introuvable.", io.Message, io);
            return new ConversionException(ConversionErrorKind.WriteError, "Erreur d'écriture : " + io.Message, io.ToString(), io);
        }
        if (ex is InvalidDataException or FormatException)
            return new ConversionException(ConversionErrorKind.Corrupt, "Le fichier semble endommagé ou n'est pas au format attendu.", ex.Message, ex);
        if (ex is OutOfMemoryException)
            return new ConversionException(ConversionErrorKind.TooLarge, "Mémoire insuffisante pour ce fichier.", ex.Message, ex);
        return new ConversionException(ConversionErrorKind.Unknown, "Erreur inattendue : " + ex.Message, ex.ToString(), ex);
    }
}

/// <summary>Analyse de durees saisies par l'utilisateur : « 90 », « 1:30 », « 00:01:30.5 ».</summary>
public static class TimeParser
{
    public static TimeSpan? Parse(string s)
    {
        s = s.Trim().Replace(',', '.');
        if (s.Length == 0) return null;
        var parts = s.Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 0) return null;
            total = total * 60 + v;
        }
        return total > 360_000 ? null : TimeSpan.FromSeconds(total);
    }

    public static string ToFfmpeg(TimeSpan t) => t.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
