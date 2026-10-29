using FileWheel.Core.Actions;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Formats;
using FileWheel.Core.Settings;
using FileWheel.Core.Util;
using static FileWheel.Tests.T;

namespace FileWheel.Tests;

public static class FileOpsTests
{
    private static string NewDir(string name)
    {
        var d = Path.Combine(Env.Root, "fileops", name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        return Directory.CreateDirectory(d).FullName;
    }

    public static async Task RunAll()
    {
        Suite("Opérations sur les fichiers");

        await Run("Noms Windows : caractères interdits, noms réservés, points finaux", () =>
        {
            Check(PathSafety.ValidateFileName("rapport.txt") is null, "nom valide");
            Check(PathSafety.ValidateFileName("a:b.txt") is not null, "deux-points refusé");
            Check(PathSafety.ValidateFileName("CON.txt") is not null, "CON refusé");
            Check(PathSafety.ValidateFileName("lpt1") is not null, "LPT1 refusé");
            Check(PathSafety.ValidateFileName("fin.") is not null, "point final refusé");
            Check(PathSafety.ValidateFileName("Été à la plage.jpg") is null, "accents acceptés");
            Equal("_CON.txt", PathSafety.SanitizeFileName("CON.txt"), "assainissement nom réservé");
            Equal("a_b_c", PathSafety.SanitizeFileName("a<b>:c"), "assainissement");
        });
        await Run("Chemins d'archive : « zip slip » bloqué", () =>
        {
            var root = NewDir("slip");
            Check(PathSafety.ResolveArchiveEntry(root, "../x") is null, "../ refusé");
            Check(PathSafety.ResolveArchiveEntry(root, "a/../../x") is null, "remontée cachée refusée");
            Check(PathSafety.ResolveArchiveEntry(root, "/etc/passwd") is null, "absolu refusé");
            Check(PathSafety.ResolveArchiveEntry(root, "C:/Windows/x") is null, "lecteur refusé");
            Check(PathSafety.ResolveArchiveEntry(root, "x.txt:flux") is null, "flux NTFS refusé");
            Check(PathSafety.ResolveArchiveEntry(root, "dossier/é.txt") is { } ok && ok.StartsWith(root), "entrée normale acceptée");
        });
        await Run("Noms uniques « (2) », « (3) » et doubles extensions", () =>
        {
            var d = NewDir("unique");
            var p = Path.Combine(d, "photo.jpg");
            File.WriteAllText(p, "1");
            Equal(Path.Combine(d, "photo (2).jpg"), NameConflict.UniquePath(p), "premier conflit");
            File.WriteAllText(Path.Combine(d, "photo (2).jpg"), "2");
            Equal(Path.Combine(d, "photo (3).jpg"), NameConflict.UniquePath(Path.Combine(d, "photo (2).jpg")), "suite de numérotation");
            Equal(("archive", ".tar.gz"), NameConflict.SplitName("archive.tar.gz"), "double extension");
        });
        await Run("Copie : conflit => renommage, original et destination intacts", () =>
        {
            var src = NewDir("copie-src");
            var dst = NewDir("copie-dst");
            var a = Path.Combine(src, "Relevé été.txt");
            File.WriteAllText(a, "source");
            File.WriteAllText(Path.Combine(dst, "Relevé été.txt"), "existant");
            var r = SafeFileOps.CopyTo([a], dst, ConflictPolicy.Rename);
            Check(r.Success && r.Done == 1, r.Summary("copié"));
            Equal("existant", File.ReadAllText(Path.Combine(dst, "Relevé été.txt")), "destination existante intacte");
            Equal("source", File.ReadAllText(Path.Combine(dst, "Relevé été (2).txt")), "copie renommée");
            Check(File.Exists(a), "source conservée");
            Check(!Directory.EnumerateFiles(dst, ".~fw-*").Any(), "aucun temporaire restant");
        });
        await Run("Copie : politique « Ignorer » et « Demander » (appliquer à tous)", () =>
        {
            var src = NewDir("ask-src");
            var dst = NewDir("ask-dst");
            var files = Enumerable.Range(1, 3).Select(i => { var f = Path.Combine(src, $"f{i}.txt"); File.WriteAllText(f, "n" + i); File.WriteAllText(Path.Combine(dst, $"f{i}.txt"), "old"); return f; }).ToList();
            var skip = SafeFileOps.CopyTo(files, dst, ConflictPolicy.Skip);
            Equal(3, skip.Skipped, "ignorés");
            int asked = 0;
            var r = SafeFileOps.CopyTo(files, dst, ConflictPolicy.Ask, (string s, string e, out bool all) => { asked++; all = true; return ConflictChoice.Replace; });
            Equal(1, asked, "une seule question avec « appliquer à tous »");
            Equal("n2", File.ReadAllText(Path.Combine(dst, "f2.txt")), "remplacement explicite appliqué");
        });
        await Run("Déplacement : la source disparaît seulement après réussite", () =>
        {
            var src = NewDir("move-src");
            var dst = NewDir("move-dst");
            var a = Path.Combine(src, "doc.txt");
            File.WriteAllText(a, "contenu");
            var r = SafeFileOps.MoveTo([a], dst, ConflictPolicy.Rename);
            Check(r.Success, r.Summary("déplacé"));
            Check(!File.Exists(a) && File.ReadAllText(Path.Combine(dst, "doc.txt")) == "contenu", "déplacé");
            var again = SafeFileOps.MoveTo([Path.Combine(dst, "doc.txt")], dst, ConflictPolicy.Rename);
            Equal(1, again.Skipped, "déplacer dans le même dossier = ignoré");
        });
        await Run("Déplacement d'un dossier dans lui-même refusé", () =>
        {
            var d = NewDir("self");
            var inner = Directory.CreateDirectory(Path.Combine(d, "sous")).FullName;
            var r = SafeFileOps.MoveTo([d], inner, ConflictPolicy.Rename);
            Check(r.Errors.Count == 1 && Directory.Exists(d), "refusé, rien perdu");
        });
        await Run("Copie de dossier récursive (accents, espaces)", () =>
        {
            var src = NewDir("tree");
            Directory.CreateDirectory(Path.Combine(src, "Été 2025", "Sous dossier"));
            File.WriteAllText(Path.Combine(src, "Été 2025", "Sous dossier", "à.txt"), "x");
            var dst = NewDir("tree-dst");
            var r = SafeFileOps.CopyTo([Path.Combine(src, "Été 2025")], dst, ConflictPolicy.Rename);
            Check(r.Success && File.Exists(Path.Combine(dst, "Été 2025", "Sous dossier", "à.txt")), r.Summary("copié"));
        });
        await Run("Annulation pendant une grosse copie : pas de fichier partiel", async () =>
        {
            var src = NewDir("big-src");
            var dst = NewDir("big-dst");
            var big = Path.Combine(src, "gros.bin");
            using (var fs = new FileStream(big, FileMode.Create)) { fs.SetLength(400L << 20); fs.WriteByte(1); }
            using var cts = new CancellationTokenSource();
            var progress = new Progress<FileOpProgress>(p => { if (p.BytesDone > 50L << 20) cts.Cancel(); });
            var r = await Task.Run(() => SafeFileOps.CopyTo([big], dst, ConflictPolicy.Rename, null, new SyncProgress(p => { if (p.BytesDone > 50L << 20) cts.Cancel(); }), cts.Token));
            Check(r.Cancelled, "annulé");
            Check(!Directory.EnumerateFileSystemEntries(dst).Any(), "aucun fichier partiel ni temporaire : " + string.Join(", ", Directory.EnumerateFileSystemEntries(dst)));
        });
        await Run("Gros fichier (1 Gio) copié et vérifié", async () =>
        {
            var src = NewDir("huge-src");
            var dst = NewDir("huge-dst");
            var big = Path.Combine(src, "1Gio.bin");
            using (var fs = new FileStream(big, FileMode.Create)) { fs.SetLength(1L << 30); fs.Position = (1L << 30) - 1; fs.WriteByte(7); }
            var r = await Task.Run(() => SafeFileOps.CopyTo([big], dst, ConflictPolicy.Rename));
            Check(r.Success, r.Summary("copié"));
            Equal(1L << 30, new FileInfo(Path.Combine(dst, "1Gio.bin")).Length, "taille");
            File.Delete(Path.Combine(dst, "1Gio.bin"));
            File.Delete(big);
        });
        await Run("Espace disque insuffisant : refus avant de commencer", () =>
        {
            Check(!SafeFileOps.HasFreeSpace(Env.Root, long.MaxValue / 4, out _), "détection");
        });
        await Run("Renommage : validation, conflit, casse seule", () =>
        {
            var d = NewDir("rename");
            var a = Path.Combine(d, "a.txt");
            File.WriteAllText(a, "a");
            File.WriteAllText(Path.Combine(d, "b.txt"), "b");
            Throws<IOException>(() => SafeFileOps.Rename(a, "b.txt"), "conflit refusé");
            Throws<ArgumentException>(() => SafeFileOps.Rename(a, "x?.txt"), "nom invalide refusé");
            var n = SafeFileOps.Rename(a, "A.txt");
            Check(Path.GetFileName(n) == "A.txt" && File.Exists(n), "changement de casse");
        });
        await Run("Renommage en lot avec modèle {nom} et {n:000}", () =>
        {
            var d = NewDir("batch");
            var files = new[] { "x.jpg", "y.jpg", "z.png" }.Select(n => { var p = Path.Combine(d, n); File.WriteAllText(p, n); return p; }).ToList();
            var plan = SafeFileOps.PlanBatchRename(files, "Vacances {n:000}");
            Equal("Vacances 001.jpg", plan[0].NewName, "premier");
            Equal("Vacances 003.png", plan[2].NewName, "extension conservée");
        });
        await Run("Suppression définitive : éléments absents signalés, pas d'exception", () =>
        {
            var d = NewDir("delete");
            var a = Path.Combine(d, "a.txt");
            File.WriteAllText(a, "a");
            var r = SafeFileOps.DeletePermanently([a, Path.Combine(d, "absent.txt")]);
            Check(r.Done == 1 && r.Errors.Count == 1 && !File.Exists(a), r.Summary("supprimé"));
        });

        Suite("Actions personnalisées — sécurité");
        await Run("Arguments : un jeton = un argument, aucune interprétation par un shell", () =>
        {
            var a = new CustomAction { Arguments = "--input \"{path}\" --out {dir} {paths}" };
            var args = a.ExpandArguments(["/tmp/a b & c.txt", "/tmp/d;rm -rf.txt"]);
            Equal("--input", args[0], "jeton 1");
            Equal("/tmp/a b & c.txt", args[1], "chemin avec espaces et & = un seul argument");
            Equal("/tmp/d;rm -rf.txt", args[^1], "chemin avec ; = un seul argument");
        });
        await Run("Modèle mal formé refusé ; programme relatif refusé", () =>
        {
            Throws<FormatException>(() => CustomAction.TokenizeTemplate("\"{path}"), "guillemet non fermé");
            var a = new CustomAction { Type = CustomActionType.RunProgram, ProgramPath = "notepad.exe" };
            Check(a.Validate().Any(e => e.Contains("chemin complet")), "chemin relatif refusé");
        });
        await Run("Programme modifié depuis l'enregistrement : détecté", () =>
        {
            var d = NewDir("prog");
            var exe = Path.Combine(d, "outil.sh");
            File.WriteAllText(exe, "v1");
            var a = new CustomAction { Type = CustomActionType.RunProgram, ProgramPath = exe, ProgramSha256 = CustomAction.ComputeSha256(exe) };
            Check(!a.ProgramChanged(), "inchangé");
            File.WriteAllText(exe, "v2 modifié");
            Check(a.ProgramChanged(), "modification détectée");
        });
        await Run("Interpréteur de commandes signalé", () =>
            Check(new CustomAction { Type = CustomActionType.RunProgram, ProgramPath = @"C:\Windows\System32\cmd.exe" }.Warnings().Count == 1, "avertissement"));
        await Run("Filtre d'extensions et de dossiers", () =>
        {
            var a = new CustomAction { Extensions = ["jpg", ".png"], AllowMultiple = false };
            Check(a.Accepts("x.PNG", false, 1) && !a.Accepts("x.gif", false, 1) && !a.Accepts("d", true, 1) && !a.Accepts("x.jpg", false, 2), "filtres");
        });

        Suite("Paramètres et stockage");
        await Run("Fichier de paramètres corrompu : valeurs par défaut + copie de sauvegarde", () =>
        {
            File.WriteAllText(AppPaths.SettingsFile, "{ ceci n'est pas du json");
            var s = SettingsStore.Load();
            Check(s.Trigger.Alt && s.WheelEnabled, "valeurs par défaut");
            Check(Directory.EnumerateFiles(AppPaths.ConfigDir, "settings.json.corrompu-*").Any(), "copie de sauvegarde conservée");
        });
        await Run("Valeurs hors bornes corrigées", () =>
        {
            var s = new AppSettings();
            s.Wheel.DiameterDip = 5000;
            s.Conversion.MaxConcurrent = 99;
            s.Conversion.Suffix = "a/b:c";
            s.Normalize();
            Equal(600, s.Wheel.DiameterDip, "diamètre");
            Equal(8, s.Conversion.MaxConcurrent, "concurrence");
            Equal("a_b_c", s.Conversion.Suffix, "suffixe assaini");
        });
        await Run("Enregistrement atomique puis relecture", () =>
        {
            var s = new AppSettings { StartWithWindows = true };
            s.SendToFolders.Add("/tmp");
            SettingsStore.Save(s);
            var r = SettingsStore.Load();
            Check(r.StartWithWindows == true && r.SendToFolders.Contains("/tmp"), "relu");
            Check(!Directory.EnumerateFiles(AppPaths.ConfigDir, ".settings.json.*.tmp").Any(), "pas de temporaire");
        });

        Suite("Détection des formats");
        (string file, string expected)[] cases =
        [
            ("photo exif.jpg", "jpg"), ("logo transparent.png", "png"), ("anime.gif", "gif"), ("dégradé.bmp", "bmp"), ("vert.tiff", "tiff"),
            ("cercle.webp", "webp"), ("clip vidéo.mp4", "mp4"), ("film.mkv", "mkv"), ("ancien.avi", "avi"), ("tonalité.wav", "wav"),
            ("chanson.mp3", "mp3"), ("piste.flac", "flac"), ("photos été.zip", "zip"), ("liens.tar.gz", "tgz"), ("livre.epub", "epub"),
            ("rapport annuel.docx", "docx"), ("présentation.pptx", "pptx"), ("rapport.pdf", "pdf"), ("cube.stl", "stl"), ("police.ttf", "ttf"),
            ("données.json", "json"), ("config.yaml", "yaml"), ("inventaire.csv", "csv"), ("catalogue.xml", "xml"), ("lisez-moi.md", "md"),
            ("page.html", "html"), ("message.eml", "eml"), ("contacts.vcf", "vcf"), ("agenda.ics", "ics"), ("boite.mbox", "mbox"),
        ];
        foreach (var (file, expected) in cases)
        {
            await Run($"{file} -> {expected}", () =>
            {
                var p = Env.Sample(file);
                if (!File.Exists(p)) Skip("échantillon non généré (outil absent)");
                var d = FormatDetector.Detect(p);
                Equal(expected, d.Format?.Id, "format détecté");
                Check(!d.Mismatch, "pas de fausse alerte : " + d.Warning);
            });
        }
        await Run("Extension trompeuse : PNG nommé .jpg => contenu réel + avertissement", () =>
        {
            var d = FormatDetector.Detect(Env.Sample("png déguisé.jpg"));
            Equal("png", d.Format?.Id, "format réel");
            Check(d.Mismatch && d.Warning is not null, "avertissement");
        });
        await Run("Fichier sans extension identifié par signature", () =>
            Equal("png", FormatDetector.Detect(Env.Sample("image_sans_extension")).Format?.Id, "format"));
        await Run("Programme déguisé en image : signalé comme exécutable", () =>
        {
            var d = FormatDetector.Detect(Env.Sample("vacances.jpg"));
            Check(d.IsExecutable && d.Warning!.Contains("programme"), "exécutable détecté");
        });
        await Run("Fichier vide et contenu incohérent", () =>
        {
            var e = FormatDetector.Detect(Env.Sample("vide.png"));
            Check(e.SuspectedCorrupt && e.Warning is not null, "vide signalé");
            var f = FormatDetector.Detect(Env.Sample("faux.png"));
            Check(f.Mismatch, "texte nommé .png signalé : " + f.Warning);
        });
        await Run("PDF tronqué signalé", () =>
        {
            if (!File.Exists(Env.Sample("rapport.pdf"))) Skip("LibreOffice absent");
            var bytes = File.ReadAllBytes(Env.Sample("rapport.pdf"));
            var p = Path.Combine(Env.Work, "tronqué.pdf");
            File.WriteAllBytes(p, bytes[..(bytes.Length / 2)]);
            Check(FormatDetector.Detect(p).SuspectedCorrupt, "PDF tronqué détecté");
        });
        await Run("Lecture limitée à l'en-tête (fichier de 2 Gio détecté instantanément)", () =>
        {
            var p = Path.Combine(Env.Work, "énorme.mp4");
            var head = File.ReadAllBytes(Env.Sample("clip vidéo.mp4"))[..4096];
            using (var fs = new FileStream(p, FileMode.Create)) { fs.Write(head); fs.SetLength(2L << 30); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var d = FormatDetector.Detect(p);
            Equal("mp4", d.Format?.Id, "format");
            Check(sw.ElapsedMilliseconds < 500, $"{sw.ElapsedMilliseconds} ms");
            File.Delete(p);
        });
    }

    private sealed class SyncProgress(Action<FileOpProgress> a) : IProgress<FileOpProgress>
    {
        public void Report(FileOpProgress value) => a(value);
    }
}
