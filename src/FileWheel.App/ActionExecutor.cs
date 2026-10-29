using System.IO;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using FileWheel.App.Ui;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Archives;
using FileWheel.Core.Actions;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Formats;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;
using FileWheel.Core.Wheel;
using FileWheel.Windows.Shell;

namespace FileWheel.App;

/// <summary>
/// Execution des commandes choisies dans la roue. Les operations longues partent en arriere-plan
/// (fenetre des taches + bulle de notification a la fin) ; l'interface ne se fige jamais.
/// </summary>
public sealed class ActionExecutor
{
    private readonly FileWheelApp _app;

    public ActionExecutor(FileWheelApp app) => _app = app;

    private ConversionEngine Engine => _app.Engine;
    private AppSettings Settings => _app.Settings;

    public void Execute(WheelItem item, IReadOnlyList<string> files)
    {
        try
        {
            files = files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
            if (files.Count == 0) { _app.Notify("FileWheel", "Les éléments glissés n'existent plus.", error: true); return; }
            Log.Info($"Action « {item.Command} » sur {files.Count} élément(s)");
            Run(item, files);
        }
        catch (Exception ex)
        {
            Log.Error($"Action {item.Command}", ex);
            Ui.Ui.Message(ConversionManager.Translate(ex).Message, image: MessageBoxImage.Error);
        }
    }

    private void Run(WheelItem item, IReadOnlyList<string> files)
    {
        switch (item.Command)
        {
            case ActionIds.ConvertQuickTarget: ConvertNow(files, item.Argument!); break;
            case ActionIds.ImageCrop: OpenTool(files[0], l => new Tools.CropWindow(l)); break;
            case ActionIds.ImageBackground: OpenTool(files[0], l => new Tools.BackgroundWindow(l)); break;
            case ActionIds.ImageAnnotate: OpenTool(files[0], l => new Tools.AnnotateWindow(l)); break;
            case ActionIds.ImageMetadata: OpenTool(files[0], l => new Tools.MetadataWindow(l)); break;
            case ActionIds.ImageCompress: OpenTool(files[0], l => new Tools.CompressWindow(files.Where(File.Exists).ToList(), l)); break;
            case ActionIds.ConvertDialog: _app.OpenQuickConvert(files, null); break;
            case ActionIds.ConvertAdvanced: _app.OpenConverter(files); break;
            case ActionIds.ImageResize:
            {
                var fmt = FormatDetector.Detect(files[0]).Format?.Id ?? "png";
                var cap = Engine.Registry.Best(fmt, fmt) ?? Engine.Registry.Best(fmt, "png");
                _app.OpenQuickConvert(files, cap?.Key);
                break;
            }
            case ActionIds.Copy: CopyToClipboard(files); break;
            case ActionIds.CopyPath: Clipboard.SetText(string.Join(Environment.NewLine, files)); _app.Notify("Chemin copié", files.Count == 1 ? files[0] : $"{files.Count} chemins"); break;
            case ActionIds.CopyName: Clipboard.SetText(string.Join(Environment.NewLine, files.Select(f => Path.GetFileName(f.TrimEnd('\\'))))); break;
            case ActionIds.MoveTo: Transfer(files, item.Argument, move: true); break;
            case ActionIds.CopyTo:
            case ActionIds.SendTo: Transfer(files, item.Argument, move: false); break;
            case ActionIds.Open: Open(files); break;
            case ActionIds.OpenWith: ShellOps.OpenWith(files[0], IntPtr.Zero); break;
            case ActionIds.Rename: Rename(files); break;
            case ActionIds.Zip: Compress(files, "zip", 6, null); break;
            case ActionIds.CompressAs: CompressAs(files, item.Argument ?? ""); break;
            case ActionIds.ExtractHere: Extract(files, null); break;
            case ActionIds.ExtractTo:
            {
                var dest = Pickers.Folder("Extraire vers", Path.GetDirectoryName(files[0]));
                if (dest is not null) Extract(files, dest);
                break;
            }
            case ActionIds.TestArchive: TestArchives(files); break;
            case ActionIds.Share:
                if (!ShellOps.Share(files, IntPtr.Zero))
                {
                    Ui.Ui.Message("Le partage Windows n'a pas pu être ouvert pour ces éléments. L'Explorateur va s'ouvrir sur eux : utilisez « Partager » dans son menu.", image: MessageBoxImage.Information);
                    ShellOps.RevealInExplorer(files);
                }
                break;
            case ActionIds.OpenParent: ShellOps.RevealInExplorer(files); break;
            case ActionIds.Terminal: ShellOps.OpenTerminal(Path.GetDirectoryName(Path.GetFullPath(files[0]).TrimEnd('\\'))!); break;
            case ActionIds.Properties: ShellOps.Properties(files, IntPtr.Zero); break;
            case ActionIds.Shortcut:
                foreach (var f in files) ShellOps.CreateShortcut(f);
                _app.Notify("Raccourci créé", files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} raccourcis");
                break;
            case ActionIds.Recycle: Recycle(files); break;
            case ActionIds.DeletePermanently: DeletePermanently(files); break;
            case ActionIds.AddFavorite:
                foreach (var f in files) if (!Settings.Favorites.Contains(f, StringComparer.OrdinalIgnoreCase)) Settings.Favorites.Add(f);
                _app.SaveSettings();
                _app.Notify("Ajouté aux favoris", "Accessible depuis l'icône FileWheel de la zone de notification.");
                break;
            case ActionIds.NewCustomAction: _app.OpenSettings("Actions personnalisées"); break;
            case ActionIds.Settings: _app.OpenSettings(null); break;
            case ActionIds.Custom: RunCustom(item.Argument!, files); break;
            default: Log.Warn("Commande inconnue : " + item.Command); break;
        }
    }

    // ------------------------------------------------------------------ outils image

    private void OpenTool(string path, Func<Tools.ImageIO.Loaded, Tools.ToolWindow> create)
    {
        if (!Tools.ToolWindow.TryLoad(path, null, out var loaded)) return;
        var w = create(loaded);
        w.Closed += (_, _) =>
        {
            if (w.Result is { } created) _app.ReportCreated(created, w.Title.Split(" — ")[0]);
        };
        w.Show();
        w.Activate();
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (h != IntPtr.Zero) FileWheel.Windows.SystemIntegration.WindowStyles.ForceForeground(h);
    }

    // ------------------------------------------------------------------ conversion directe

    private void ConvertNow(IReadOnlyList<string> files, string capabilityKey)
    {
        var cap = Engine.Registry.ByKey(capabilityKey);
        if (cap is null) { _app.OpenQuickConvert(files, null); return; }
        if (Engine.Registry.MissingEngine(cap) is { } missing)
        {
            Ui.Ui.Message($"Cette conversion nécessite {missing}, qui n'est pas installé. Vous pouvez l'installer dans Paramètres › Moteurs (téléchargement vérifié, avec votre accord).", image: MessageBoxImage.Information);
            _app.OpenSettings("Moteurs");
            return;
        }
        foreach (var f in files)
        {
            var job = Engine.Queue.Enqueue(new ConversionOrder { Inputs = [f], CapabilityKey = cap.Key });
            _app.WatchJob(job);
        }
        _app.Notify("Conversion lancée", files.Count == 1 ? $"{Path.GetFileName(files[0])} → {cap.DisplayLabel}" : $"{files.Count} fichiers → {cap.DisplayLabel}");
    }

    // ------------------------------------------------------------------ presse-papiers

    private void CopyToClipboard(IReadOnlyList<string> files)
    {
        var col = new StringCollection();
        col.AddRange(files.ToArray());
        var data = new DataObject();
        data.SetFileDropList(col);
        // « Preferred DropEffect » = COPIER (et non couper) : coller ne deplace jamais l'original.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
        Clipboard.SetDataObject(data, copy: true);
        _app.Notify("Copié dans le presse-papiers", files.Count == 1 ? Path.GetFileName(files[0]) + " — collez avec Ctrl+V" : $"{files.Count} éléments — collez avec Ctrl+V");
    }

    // ------------------------------------------------------------------ copie / deplacement

    private void Transfer(IReadOnlyList<string> files, string? folder, bool move)
    {
        folder ??= Pickers.Folder(move ? "Déplacer vers" : "Copier vers", Settings.LastMoveFolder ?? Path.GetDirectoryName(files[0]));
        if (folder is null) return;
        if (move) { Settings.LastMoveFolder = folder; _app.SaveSettings(); }
        var verb = move ? "Déplacement" : "Copie";
        _app.Operations.Start($"{verb} de {Describe(files)} vers {Path.GetFileName(folder.TrimEnd('\\'))}", op => Task.Run(() =>
        {
            var progress = new SyncProgress<FileOpProgress>(p => _app.Operations.Report(op, p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : null, p.CurrentItem));
            var r = move
                ? SafeFileOps.MoveTo(files, folder, ConflictPolicy.Ask, ConflictDialog.Resolver(), progress, op.Token)
                : SafeFileOps.CopyTo(files, folder, ConflictPolicy.Ask, ConflictDialog.Resolver(), progress, op.Token);
            op.Outputs.AddRange(r.Created);
            return r.Errors.Count == 0 ? null : r.Summary(move ? "déplacé(s)" : "copié(s)") + "\n" + string.Join("\n", r.Errors.Take(5).Select(e => $"{Path.GetFileName(e.Path)} : {e.Error}"));
        }), op => _app.NotifyOperation(op));
    }

    private void Open(IReadOnlyList<string> files)
    {
        if (files.Count > 10 && !ConfirmDialog.Ask("Ouvrir", $"Ouvrir {files.Count} éléments ?", "Ouvrir")) return;
        foreach (var f in files)
        {
            var det = File.Exists(f) ? FormatDetector.Detect(f) : null;
            if (det is not null && (det.IsExecutable || FormatCatalog.IsExecutable(f)))
            {
                if (!ConfirmDialog.Ask("Programme", $"« {Path.GetFileName(f)} » est un programme ou un script. L'exécuter ?", "Exécuter", destructive: true)) continue;
            }
            ShellOps.Open(f, IntPtr.Zero);
        }
    }

    private void Rename(IReadOnlyList<string> files)
    {
        if (files.Count == 1)
        {
            var path = files[0];
            var name = Path.GetFileName(path.TrimEnd('\\'));
            var stemLen = Directory.Exists(path) ? name.Length : NameConflict.SplitName(name).stem.Length;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd('\\'))!;
            var newName = TextPromptDialog.Ask("Renommer", "Nouveau nom :", name, n =>
            {
                var err = PathSafety.ValidateFileName(n);
                if (err is not null) return err;
                var target = Path.Combine(dir, n);
                if (!PathSafety.SamePath(target, path) && (File.Exists(target) || Directory.Exists(target))) return "Un élément porte déjà ce nom.";
                return null;
            }, stemLen);
            if (newName is null || newName == name) return;
            try { SafeFileOps.Rename(path, newName); }
            catch (Exception ex) { Ui.Ui.Message(ConversionManager.Translate(ex).Message, image: MessageBoxImage.Warning); }
            return;
        }
        var dlg = new BatchRenameDialog(files);
        if (!dlg.Run()) return;
        var errors = new List<string>();
        foreach (var (src, newName) in dlg.Plan)
        {
            try { SafeFileOps.Rename(src, newName); }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(src)} : {ex.Message}"); }
        }
        if (errors.Count > 0) Ui.Ui.Message("Certains éléments n'ont pas été renommés :\n" + string.Join("\n", errors.Take(10)), image: MessageBoxImage.Warning);
    }

    // ------------------------------------------------------------------ archives

    private void CompressAs(IReadOnlyList<string> files, string arg)
    {
        switch (arg)
        {
            case "7z-recompress":
            case "zip-recompress":
            {
                var target = arg.StartsWith("7z") ? "7z" : "zip";
                foreach (var f in files)
                {
                    var key = Engine.Registry.Best(FormatDetector.Detect(f).Format?.Id ?? "zip", target, ConversionKind.Recompress)?.Key;
                    if (key is null) continue;
                    if (Engine.Registry.MissingEngine(Engine.Registry.ByKey(key)!) is not null) { _app.OpenSettings("Moteurs"); return; }
                    _app.WatchJob(Engine.Queue.Enqueue(new ConversionOrder { Inputs = [f], CapabilityKey = key }));
                }
                break;
            }
            case "":
            {
                var d = new CompressDialog(files.Count, Engine.Archives.SevenZipAvailable);
                if (d.Run()) Compress(files, d.Format, d.Level, d.Password);
                break;
            }
            default:
                if (ArchiveService.CreateFormats.FirstOrDefault(f => f.Id == arg) is { NeedsSevenZip: true } && !Engine.Archives.SevenZipAvailable)
                {
                    Ui.Ui.Message("Ce format nécessite le moteur 7-Zip (Paramètres › Moteurs).", image: MessageBoxImage.Information);
                    _app.OpenSettings("Moteurs");
                    return;
                }
                Compress(files, arg, 6, null);
                break;
        }
    }

    private void Compress(IReadOnlyList<string> files, string format, int level, string? password)
    {
        _app.Operations.Start($"Compression de {Describe(files)} ({format.ToUpperInvariant()})", async op =>
        {
            var path = await Engine.Archives.CreateAsync(files, format, level, password, null, null,
                new SyncProgress<ConversionProgress>(p => _app.Operations.Report(op, p.Fraction, p.Stage)), op.Token);
            op.Outputs.Add(path);
            return null;
        }, op => _app.NotifyOperation(op));
    }

    private void Extract(IReadOnlyList<string> files, string? dest)
    {
        foreach (var f in files)
        {
            var archive = f;
            int skipped = 0;
            _app.Operations.Start($"Extraction de {Path.GetFileName(archive)}", async op =>
            {
                string? password = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var dir = await Engine.Archives.ExtractAsync(archive, dest, password,
                            new SyncProgress<ConversionProgress>(p => _app.Operations.Report(op, p.Fraction, p.Stage)), op.Token);
                        skipped = Engine.Archives.LastSkipped.Count;
                        op.Outputs.Add(dir);
                        return null;
                    }
                    catch (ConversionException ex) when (ex.Kind == ConversionErrorKind.Protected)
                    {
                        password = Application.Current.Dispatcher.Invoke(() => TextPromptDialog.Ask("Archive protégée",
                            $"« {Path.GetFileName(archive)} » est protégée par un mot de passe" + (attempt > 0 ? " (le précédent était incorrect)" : "") + " :",
                            "", s => s.Length == 0 ? "Mot de passe requis." : null));
                        if (password is null) return "Extraction annulée (mot de passe non fourni).";
                    }
                }
                return "Mot de passe incorrect.";
            }, op =>
            {
                if (op.Status == OperationStatus.Succeeded && skipped > 0)
                {
                    _app.Notify("Extraction terminée avec précautions", $"{skipped} entrée(s) dangereuse(s) ignorée(s) (chemin hors du dossier, lien, nom réservé). Détails dans le journal.");
                }
                else _app.NotifyOperation(op);
            });
        }
    }

    private void TestArchives(IReadOnlyList<string> files)
    {
        _app.Operations.Start($"Vérification de {Describe(files)}", async op =>
        {
            var lines = new List<string>();
            bool allOk = true;
            foreach (var f in files)
            {
                var r = await Engine.Archives.TestAsync(f, null, op.Token);
                allOk &= r.Ok;
                lines.Add($"{Path.GetFileName(f)} : {(r.Ok ? "intègre" : "ERREURS")} — {r.Message}");
            }
            Application.Current.Dispatcher.Invoke(() => Ui.Ui.Message(string.Join("\n", lines), "Vérification d'archive", image: allOk ? MessageBoxImage.Information : MessageBoxImage.Warning));
            return allOk ? null : "Des erreurs ont été détectées.";
        });
    }

    // ------------------------------------------------------------------ suppression

    private void Recycle(IReadOnlyList<string> files)
    {
        if (Settings.Wheel.ConfirmRecycle && !ConfirmDialog.Ask("Corbeille", $"Placer {Describe(files)} dans la Corbeille ?", "Placer dans la Corbeille", details: files.Select(f => Path.GetFileName(f.TrimEnd('\\')))))
            return;
        var r = ShellOps.Recycle(files, IntPtr.Zero, confirm: false);
        if (r.Errors.Count > 0) Ui.Ui.Message(r.Summary("placé(s) dans la Corbeille") + "\n" + string.Join("\n", r.Errors.Take(5).Select(e => $"{Path.GetFileName(e.Path)} : {e.Error}")), image: MessageBoxImage.Warning);
    }

    private void DeletePermanently(IReadOnlyList<string> files)
    {
        if (!ConfirmDialog.Ask("Suppression définitive",
                $"Supprimer définitivement {Describe(files)} ? Cette action est irréversible : les éléments ne passeront pas par la Corbeille.",
                "Supprimer définitivement", destructive: true, details: files.Select(f => Path.GetFileName(f.TrimEnd('\\')))))
            return;
        _app.Operations.Start($"Suppression définitive de {Describe(files)}", op => Task.Run(() =>
        {
            var r = SafeFileOps.DeletePermanently(files);
            return r.Errors.Count == 0 ? null : r.Summary("supprimé(s)") + "\n" + string.Join("\n", r.Errors.Take(5).Select(e => $"{Path.GetFileName(e.Path)} : {e.Error}"));
        }), op => _app.NotifyOperation(op));
    }

    // ------------------------------------------------------------------ actions personnalisees

    private void RunCustom(string id, IReadOnlyList<string> files)
    {
        var a = _app.CustomActions.FirstOrDefault(x => x.Id == id);
        if (a is null) { Ui.Ui.Message("Cette action n'existe plus.", image: MessageBoxImage.Warning); return; }
        var refused = files.FirstOrDefault(f => !a.Accepts(f, Directory.Exists(f), files.Count));
        if (refused is not null) { Ui.Ui.Message($"« {a.Name} » ne s'applique pas à {Path.GetFileName(refused)}.", image: MessageBoxImage.Information); return; }
        switch (a.Type)
        {
            case CustomActionType.CopyToFolder: Transfer(files, a.Folder, move: false); break;
            case CustomActionType.MoveToFolder: Transfer(files, a.Folder, move: true); break;
            case CustomActionType.ConvertTo:
            {
                var key = a.ConversionOptions.GetValueOrDefault("__capability") ?? "";
                if (Engine.Registry.ByKey(key) is null) { Ui.Ui.Message("La conversion de cette action n'est plus disponible.", image: MessageBoxImage.Warning); return; }
                ConvertNow(files, key);
                break;
            }
            case CustomActionType.RunProgram:
            {
                var errors = a.Validate();
                if (errors.Count > 0) { Ui.Ui.Message(string.Join("\n", errors), image: MessageBoxImage.Warning); return; }
                if (a.ProgramChanged())
                {
                    if (!ConfirmDialog.Ask("Programme modifié", $"Le programme de l'action « {a.Name} » a changé depuis son enregistrement :\n{a.ProgramPath}\n\nLancer quand même et mémoriser cette nouvelle version ?", "Lancer", destructive: true)) return;
                    a.ProgramSha256 = CustomAction.ComputeSha256(a.ProgramPath);
                    _app.SaveCustomActions();
                }
                var batches = a.SingleInvocation ? [files] : files.Select(f => (IReadOnlyList<string>)[f]).ToList();
                foreach (var batch in batches)
                {
                    var psi = new ProcessStartInfo(a.ProgramPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(batch[0]).TrimEnd('\\'))! };
                    foreach (var arg in a.ExpandArguments(batch)) psi.ArgumentList.Add(arg);
                    Log.Info($"Action perso « {a.Name} » : {Path.GetFileName(a.ProgramPath)} ({psi.ArgumentList.Count} argument(s))");
                    Process.Start(psi);
                }
                break;
            }
        }
    }

    private static string Describe(IReadOnlyList<string> files) =>
        files.Count == 1 ? $"« {Path.GetFileName(files[0].TrimEnd('\\'))} »" : $"{files.Count} éléments";

    /// <summary>IProgress qui rappelle immediatement (pas de capture de contexte).</summary>
    private sealed class SyncProgress<T>(Action<T> a) : IProgress<T>
    {
        public void Report(T value) => a(value);
    }
}
