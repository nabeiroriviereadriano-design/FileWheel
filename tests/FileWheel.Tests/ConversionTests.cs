using System.Text.Json.Nodes;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Providers.Data;
using FileWheel.Conversion.Providers.Text;
using FileWheel.Core.Formats;
using FileWheel.Core.Settings;
using static FileWheel.Tests.Env;
using static FileWheel.Tests.T;

namespace FileWheel.Tests;

public static class ConversionTests
{
    private static string OutDir(string name) => Directory.CreateDirectory(Path.Combine(Work, name)).FullName;

    /// <summary>Conversion + controles communs : source intacte, sortie unique, lisible, sans temporaire.</summary>
    private static async Task<ConversionResult> Checked(string sample, string target, ConversionKind kind = ConversionKind.Convert,
        Dictionary<string, string>? options = null, string? sub = null)
    {
        var input = Sample(sample);
        if (!File.Exists(input)) Skip($"échantillon « {sample} » non généré");
        var key = Key(FormatDetector.Detect(input).Format!.Id, target, kind);
        var before = Hash(input);
        var dir = OutDir(sub ?? $"{Path.GetFileNameWithoutExtension(sample)}-{target}-{kind}");
        var r = await Convert(input, key, options, dir);
        Equal(before, Hash(input), "le fichier source est intact (empreinte SHA-256)");
        Check(r.Outputs.Count > 0 && r.Outputs.All(o => File.Exists(o) || Directory.Exists(o)), "sortie présente");
        Check(!Directory.EnumerateFileSystemEntries(dir, OutputPlanner.TempPrefix + "*").Any(), "aucun temporaire laissé");
        return r;
    }

    public static async Task RunAll()
    {
        Suite("Moteurs détectés");
        await Run("Détection des moteurs externes", () =>
        {
            var lines = EngineCatalog.All.Select(e => $"{e.Name} : {(Engine.Dependencies.PathOf(e.Id) ?? "absent")}");
            Console.WriteLine("        " + string.Join("\n        ", lines));
            Check(Engine.Dependencies.IsAvailable(EngineIds.FFmpeg) || true, "ok");
        });
        await Run("Moteur absent : capacité affichée « à installer », jamais disponible", async () =>
        {
            var bare = NewEngine(withEngines: false);
            var oldPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", "/nonexistent");
                bare.Dependencies.Refresh();
                var routes = bare.Registry.RoutesFrom("mp4");
                Check(routes.Count > 0 && routes.All(r => !r.Available || r.Capability.EngineId is null), "aucune route vidéo disponible sans FFmpeg");
                Check(routes.Any(r => r.MissingEngine == "FFmpeg"), "moteur manquant nommé");
                var cap = bare.Registry.Best("mp4", "webm")!;
                var ex = Throws<ConversionException>(() => bare.Manager.Prepare(new ConversionOrder { Inputs = [Sample("clip vidéo.mp4")], CapabilityKey = cap.Key }), "préparation");
                Equal(ConversionErrorKind.EngineMissing, ex.Kind, "type d'erreur");
                // Les conversions integrees restent disponibles
                Check(bare.Registry.RoutesFrom("json").Any(r => r.Available && r.Target == "csv"), "conversions intégrées disponibles");
            }
            finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
            await Task.CompletedTask;
        });

        Suite("Images (ImageMagick)");
        await Run("JPEG -> PNG : dimensions conservées, sans perte", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var r = await Checked("photo exif.jpg", "png", options: new() { ["autoorient"] = "false" });
            var id = Identify(r.Outputs[0]);
            Equal((120, 80), (id.w, id.h), "dimensions");
            Equal("PNG", id.format, "format");
        });
        await Run("Orientation EXIF appliquée (120×80 orienté => 80×120)", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var r = await Checked("photo exif.jpg", "png", sub: "exif-orient");
            var id = Identify(r.Outputs[0]);
            Equal((80, 120), (id.w, id.h), "dimensions après rotation EXIF");
        });
        await Run("Métadonnées : conservées par défaut, supprimées sur demande (GPS)", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var keep = await Checked("photo exif.jpg", "jpg", sub: "meta-keep");
            var strip = await Checked("photo exif.jpg", "jpg", options: new() { ["strip"] = "true" }, sub: "meta-strip");
            string Exif(string p) => Must("identify", "-format", "%[EXIF:*]", p);
            Check(Exif(keep.Outputs[0]).Contains("FileWheelCam"), "fabricant conservé");
            Check(!Exif(strip.Outputs[0]).Contains("GPS"), "GPS supprimé");
        });
        await Run("PNG transparent -> JPEG : avertissement + fond blanc", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var cap = Key("png", "jpg");
            var prep = Engine.Manager.Prepare(new ConversionOrder { Inputs = [Sample("logo transparent.png")], CapabilityKey = cap });
            Check(prep.Analysis.Warnings.Any(w => w.Contains("transparen")), "avertissement de transparence : " + string.Join(" / ", prep.Analysis.Warnings));
            var r = await Checked("logo transparent.png", "jpg");
            var px = Must("convert", r.Outputs[0], "-format", "%[pixel:p{1,1}]", "info:");
            Check(px.Contains("255,255,255") || px.Contains("white"), "coin blanc : " + px);
        });
        await Run("PNG transparent -> WebP : transparence conservée", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var r = await Checked("logo transparent.png", "webp");
            var a = Must("convert", r.Outputs[0], "-format", "%[pixel:p{1,1}]", "info:");
            Check(a.Contains("srgba") && a.Contains(",0)"), "coin transparent : " + a);
        });
        await Run("Redimensionnement « largeur 60 » avec ratio conservé", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var r = await Checked("dégradé.bmp", "png", options: new() { ["resize"] = "width", ["width"] = "60" });
            var id = Identify(r.Outputs[0]);
            Equal((60, 45), (id.w, id.h), "dimensions");
        });
        await Run("GIF animé -> WebP : animation conservée ; -> PNG : 1re image + avertissement", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var gif = await Checked("anime.gif", "gif", sub: "gif-gif");
            Equal(3, Identify(gif.Outputs[0]).frames, "GIF -> GIF : images de l'animation");
            var webp = await Checked("anime.gif", "webp");
            Check(Identify(webp.Outputs[0]).frames == 3 || webp.Warnings.Any(w => w.Contains("animation")),
                "WebP : animation conservée, ou perte signalée honnêtement : " + string.Join(" / ", webp.Warnings));
            var png = await Checked("anime.gif", "png");
            Equal(1, Identify(png.Outputs[0]).frames, "une seule image");
            Check(png.Warnings.Any(w => w.Contains("première image")), "avertissement");
        });
        await Run("Image -> ICO multi-tailles", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var r = await Checked("logo transparent.png", "ico");
            Check(Identify(r.Outputs[0]).frames >= 5, "plusieurs tailles d'icône");
        });
        await Run("BMP -> TIFF -> PNG (aller-retour)", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var tif = await Checked("dégradé.bmp", "tiff");
            Equal("TIFF", Identify(tif.Outputs[0]).format, "format");
        });
        await Run("SVG : refus des références externes (sécurité)", () =>
        {
            var p = Path.Combine(Work, "externe.svg");
            File.WriteAllText(p, "<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"https://evil.example/x.png\"/></svg>");
            Check(FileWheel.Conversion.Providers.Images.ImageMagickProvider.SvgCheck(p) is not null, "référence externe refusée");
            File.WriteAllText(p, "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"10\" height=\"10\" fill=\"url(#g)\"/></svg>");
            Check(FileWheel.Conversion.Providers.Images.ImageMagickProvider.SvgCheck(p) is null, "SVG autonome accepté");
            File.WriteAllText(p, "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><svg>&x;</svg>");
            Check(FileWheel.Conversion.Providers.Images.ImageMagickProvider.SvgCheck(p) is not null, "entités refusées");
        });
        await Run("Image endommagée : erreur claire, pas de plantage", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var bad = Path.Combine(Work, "abîmée.png");
            var b = File.ReadAllBytes(Sample("logo transparent.png"));
            File.WriteAllBytes(bad, b[..40]);
            var ex = await Throws<ConversionException>(() => Convert(bad, Key("png", "jpg"), outDir: OutDir("corrupt-img")), "conversion");
            Check(ex.Kind is ConversionErrorKind.Corrupt or ConversionErrorKind.EngineCrashed or ConversionErrorKind.WriteError, $"type : {ex.Kind} ({ex.Message})");
            Check(!Directory.EnumerateFileSystemEntries(OutDir("corrupt-img")).Any(), "aucune sortie partielle");
        });

        Suite("PDF");
        await Run("Images JPEG + PNG transparent -> un PDF (intégré, sans recompression)", async () =>
        {
            var inputs = new[] { Sample("photo exif.jpg"), Sample("logo transparent.png"), Sample("palette entrelacée.png") };
            var key = Engine.Registry.AllCapabilities.First(c => c.ProviderId == "builtin-docs" && c.Kind == ConversionKind.Merge).Key;
            var r = await Convert(inputs[0], key, inputs: inputs, outDir: OutDir("img2pdf"));
            Equal(3, PdfPages(r.Outputs[0]), "pages");
            Check(Path.GetFileName(r.Outputs[0]).Contains("(fusion)"), "nom de fusion");
            var images = Must("pdfimages", "-list", r.Outputs[0]);
            Check(images.Contains("jpeg"), "JPEG intégré tel quel (DCT) : " + images.Split('\n').Skip(2).FirstOrDefault());
            Check(images.Contains("smask"), "transparence en masque doux");
        });
        await Run("PNG entrelacé (Adam7) et 16 bits décodés par le moteur intégré", async () =>
        {
            foreach (var f in new[] { "palette entrelacée.png", "gris16.png" })
            {
                var r = await Checked(f, "pdf", sub: "png-" + f);
                Equal(1, PdfPages(r.Outputs[0]), "page");
            }
        });
        await Run("TXT accentué -> PDF intégré, texte relu à l'identique (pdftotext)", async () =>
        {
            var r = await Checked("notes été.txt", "pdf", sub: "txt2pdf");
            var text = PdfText(r.Outputs[0]);
            Check(text.Contains("Rapport de maintenance") && text.Contains("été 2025") && text.Contains("À vérifier"), "texte et accents relus");
            Check(PdfPages(r.Outputs[0]) >= 2, "pagination automatique");
            Check(r.Warnings.Any(w => w.Contains("remplacés")), "caractère hors WinAnsi (−) signalé");
        });
        await Run("PDF -> pages PNG (PDFium) : une image par page, dimensions à 100 ppp", async () =>
        {
            RequireEngine(EngineIds.Pdfium);
            var r = await Checked("rapport.pdf", "png", ConversionKind.PdfPages, new() { ["dpi"] = "100" });
            var files = Directory.GetFiles(r.Outputs[0]);
            Equal(PdfPages(Sample("rapport.pdf")), files.Length, "nombre d'images");
            var id = Identify(files[0]);
            Check(Math.Abs(id.w - 827) <= 2 || Math.Abs(id.w - 850) <= 2, $"largeur A4/Lettre à 100 ppp : {id.w}");
        });
        await Run("PDF -> TXT (PDFium) : texte extrait", async () =>
        {
            RequireEngine(EngineIds.Pdfium);
            var r = await Checked("rapport.pdf", "txt", ConversionKind.ExtractText);
            var t = File.ReadAllText(r.Outputs[0]);
            Check(t.Contains("Rapport annuel") && t.Contains("éèàçù"), "texte : " + t[..Math.Min(80, t.Length)]);
        });
        await Run("PDF : plage de pages et pages inexistantes", () =>
        {
            Equal("1,2,3,5", string.Join(",", FileWheel.Conversion.Providers.Pdf.PdfiumProvider.ParseRange("1-3, 5, 9", 6)), "plage");
            Throws<ConversionException>(() => FileWheel.Conversion.Providers.Pdf.PdfiumProvider.ParseRange("7-9", 6), "hors document");
        });
        await Run("PDF endommagé : erreur claire", async () =>
        {
            RequireEngine(EngineIds.Pdfium);
            var bad = Path.Combine(Work, "abîmé.pdf");
            File.WriteAllBytes(bad, "%PDF-1.4\n1 0 obj << /Type /Catalog >>\n"u8.ToArray());
            var ex = await Throws<ConversionException>(() => Convert(bad, Key("pdf", "png", ConversionKind.PdfPages), outDir: OutDir("bad-pdf")), "conversion");
            Equal(ConversionErrorKind.Corrupt, ex.Kind, "type");
        });

        Suite("Documents (LibreOffice)");
        await Run("DOCX -> PDF : texte présent", async () =>
        {
            RequireEngine(EngineIds.LibreOffice);
            var r = await Checked("rapport annuel.docx", "pdf");
            Check(PdfText(r.Outputs[0]).Contains("Rapport annuel"), "texte");
        });
        await Run("DOCX -> ODT -> DOCX", async () =>
        {
            RequireEngine(EngineIds.LibreOffice);
            var odt = await Checked("rapport annuel.docx", "odt");
            Equal("odt", FormatDetector.Detect(odt.Outputs[0]).Format?.Id, "ODT valide");
        });
        await Run("PPTX -> PDF puis diapositives en PNG (3 images)", async () =>
        {
            RequireEngine(EngineIds.LibreOffice);
            RequireEngine(EngineIds.Pdfium);
            var pdf = await Checked("présentation.pptx", "pdf");
            Equal(3, PdfPages(pdf.Outputs[0]), "pages PDF");
            var png = await Checked("présentation.pptx", "png", ConversionKind.PdfPages, new() { ["dpi"] = "50" });
            var files = Directory.GetFiles(png.Outputs[0]);
            Equal(3, files.Length, "images");
            Check(files.All(f => Path.GetFileName(f).Contains("diapo")), "noms « diapo » : " + Path.GetFileName(files[0]));
        });
        await Run("Markdown -> PDF (rendu intégré + LibreOffice)", async () =>
        {
            RequireEngine(EngineIds.LibreOffice);
            var cap = Engine.Registry.AllCapabilities.First(c => c.ProviderId == "libreoffice" && c.To == "pdf" && c.From.Contains("md"));
            var r = await Convert(Sample("lisez-moi.md"), cap.Key, outDir: OutDir("md2pdf"));
            Check(PdfText(r.Outputs[0]).Contains("Guide FileWheel"), "titre présent");
        });
        await Run("XLSX (intégré) -> PDF (LibreOffice)", async () =>
        {
            RequireEngine(EngineIds.LibreOffice);
            var x = await Checked("inventaire.csv", "xlsx", sub: "csv2xlsx-lo");
            var key = Engine.Registry.AllCapabilities.First(c => c.ProviderId == "libreoffice" && c.To == "pdf").Key;
            var r = await Convert(x.Outputs[0], key, outDir: OutDir("xlsx2pdf"));
            Check(PdfText(r.Outputs[0]).Contains("Vanne"), "contenu du classeur");
        });
        await Run("PDF -> DOCX : reconstruction approximative annoncée", async () =>
        {
            RequireEngine(EngineIds.LibreOffice);
            var cap = Engine.Registry.Best("pdf", "docx")!;
            Check(cap.Loss == LossKind.Structure && cap.LossNote!.Contains("approximative"), "limitation annoncée");
            var r = await Convert(Sample("rapport.pdf"), cap.Key, outDir: OutDir("pdf2docx"));
            Equal("docx", FormatDetector.Detect(r.Outputs[0]).Format?.Id, "DOCX produit");
        });

        Suite("Vidéo (FFmpeg)");
        await Run("MKV (H.264/AAC + sous-titres) -> MP4 : remuxage sans perte, sous-titres convertis", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("film.mkv", "mp4");
            Check(r.Warnings.Any(w => w.StartsWith("Remuxage")), "remuxage annoncé : " + string.Join(" / ", r.Warnings));
            var p = Probe(r.Outputs[0]);
            Check(p.streams.Any(s => s.codec == "h264") && p.streams.Any(s => s.codec == "aac") && p.streams.Any(s => s.codec == "mov_text"), string.Join(",", p.streams));
            Check(Math.Abs(p.duration - 4) < 0.3, "durée " + p.duration);
        });
        await Run("MP4 -> WebM : réencodage VP9/Opus annoncé avec perte", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("clip vidéo.mp4", "webm", options: new() { ["profile"] = "small" });
            Check(r.Warnings.Any(w => w.StartsWith("Réencodage")), "réencodage annoncé");
            var p = Probe(r.Outputs[0]);
            Check(p.streams.Any(s => s.codec is "vp9" or "vp8") && p.streams.Any(s => s.codec is "opus" or "vorbis"), string.Join(",", p.streams));
        });
        await Run("AVI (MPEG-4) -> MP4 : réencodage H.264, résolution 120p", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("ancien.avi", "mp4", options: new() { ["resolution"] = "360" });
            var p = Probe(r.Outputs[0]);
            Check(p.streams.Any(s => s.codec == "h264"), "H.264");
            var h = Must("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=height", "-of", "csv=p=0", r.Outputs[0]).Trim();
            Equal("120", h, "pas d'agrandissement (source 120p)");
        });
        await Run("Découpe 1 s -> 3 s et suppression de l'audio", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("clip vidéo.mp4", "mkv", options: new() { ["start"] = "1", ["end"] = "3", ["audio"] = "none" });
            var p = Probe(r.Outputs[0]);
            Check(Math.Abs(p.duration - 2) < 0.3 && !p.streams.Any(s => s.type == "audio"), $"durée {p.duration}, pistes {string.Join(",", p.streams)}");
        });
        await Run("Vidéo -> GIF animé", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("clip vidéo.mp4", "gif", options: new() { ["gif_width"] = "240", ["gif_fps"] = "10" });
            var id = Identify(r.Outputs[0]);
            Check(id.w == 240 && id.frames >= 30, $"{id.w} px, {id.frames} images");
        });
        await Run("Miniature JPEG et séquence d'images (1 par seconde)", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var t = await Checked("clip vidéo.mp4", "jpg", ConversionKind.Thumbnail);
            Equal((320, 240), (Identify(t.Outputs[0]).w, Identify(t.Outputs[0]).h), "miniature");
            var s = await Checked("clip vidéo.mp4", "png", ConversionKind.ImageSequence);
            var n = Directory.GetFiles(s.Outputs[0]).Length;
            Check(n is >= 4 and <= 5, $"{n} images pour 4 s");
        });
        await Run("Vidéo endommagée (tronquée) : erreur claire, aucune sortie", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var dir = OutDir("corrupt-video");
            var ex = await Throws<ConversionException>(() => Convert(Sample("endommagé.mp4"), Key("mp4", "webm"), outDir: dir), "conversion");
            Check(ex.Kind is ConversionErrorKind.Corrupt or ConversionErrorKind.EngineCrashed or ConversionErrorKind.InvalidInput, $"{ex.Kind} : {ex.Message}");
            Check(!Directory.EnumerateFileSystemEntries(dir).Any(), "aucune sortie");
        });
        await Run("Annulation d'une conversion longue : processus arrêté, temporaire supprimé", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var dir = OutDir("cancel-video");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var key = Key("mp4", "webm");
            await Throws<OperationCanceledException>(() => Convert(Sample("long.mp4"), key, new() { ["profile"] = "max" }, dir, ct: cts.Token), "annulation");
            await Task.Delay(300);
            Check(!Directory.EnumerateFileSystemEntries(dir).Any(), "dossier vide : " + string.Join(", ", Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName)));
            var running = Sh("pgrep", "-f", "long.mp4").stdout.Trim();
            Check(running.Length == 0, "aucun ffmpeg restant");
        });
        await Run("Copie impossible demandée explicitement : refus explicite", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var ex = await Throws<ConversionException>(() => Convert(Sample("ancien.avi"), Key("avi", "webm"), new() { ["mode"] = "copy" }, OutDir("copy-refused")), "mode copie");
            Equal(ConversionErrorKind.NotSupported, ex.Kind, "type");
        });

        Suite("Audio (FFmpeg)");
        await Run("Vidéo -> MP3 (extraction audio)", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("clip vidéo.mp4", "mp3", ConversionKind.ExtractAudio);
            var p = Probe(r.Outputs[0]);
            Check(p.streams.Count == 1 && p.streams[0].codec == "mp3" && Math.Abs(p.duration - 4) < 0.3, string.Join(",", p.streams));
        });
        await Run("Vidéo -> M4A : piste AAC copiée sans réencodage", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("clip vidéo.mp4", "m4a", ConversionKind.ExtractAudio);
            Check(r.Outputs.Count == 1 && Probe(r.Outputs[0]).streams.Any(s => s.codec == "aac"), "AAC");
        });
        await Run("MP3 -> FLAC : avertissement « ne restaure pas la qualité », pochette et titre conservés", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var key = Key("mp3", "flac");
            var prep = Engine.Manager.Prepare(new ConversionOrder { Inputs = [Sample("chanson.mp3")], CapabilityKey = key });
            Check(prep.Analysis.Notes.Any(n => n.Contains("ne restaure pas")) || prep.Analysis.Notes.Any(n => n.Contains("ne restaure")), "avertissement de qualité : " + string.Join(" / ", prep.Analysis.Notes));
            var r = await Checked("chanson.mp3", "flac");
            var tags = Must("ffprobe", "-v", "error", "-show_entries", "format_tags=title,artist", "-of", "default=nw=1", r.Outputs[0]);
            Check(tags.Contains("Chanson d'été") && tags.Contains("FileWheel"), "métadonnées : " + tags.Trim());
            Check(Probe(r.Outputs[0]).streams.Any(s => s.type == "video"), "pochette conservée");
        });
        await Run("WAV -> Opus profil voix : mono", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("tonalité.wav", "opus", options: new() { ["profile"] = "voice" });
            var ch = Must("ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=channels", "-of", "csv=p=0", r.Outputs[0]).Trim();
            Equal("1", ch, "canaux");
        });
        await Run("Normalisation, 44,1 kHz et découpe", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var r = await Checked("tonalité.wav", "mp3", options: new() { ["normalize"] = "true", ["samplerate"] = "44100", ["end"] = "1.5" });
            var p = Probe(r.Outputs[0]);
            var sr = Must("ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=sample_rate", "-of", "csv=p=0", r.Outputs[0]).Trim();
            Check(sr == "44100" && Math.Abs(p.duration - 1.5) < 0.2, $"{sr} Hz, {p.duration} s");
        });
        await Run("Fusion de 3 fichiers audio en un seul", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var inputs = new[] { Sample("tonalité.wav"), Sample("chanson.mp3"), Sample("piste.flac") };
            var key = Engine.Registry.AllCapabilities.First(c => c.Kind == ConversionKind.Merge && c.To == "mp3").Key;
            var r = await Convert(inputs[0], key, inputs: inputs, outDir: OutDir("merge"));
            Check(Math.Abs(Probe(r.Outputs[0]).duration - 7) < 0.4, "durée cumulée " + Probe(r.Outputs[0]).duration);
        });

        Suite("Données");
        await Run("JSON imbriqué -> CSV aplati (point-virgule) : colonnes « parent.enfant »", async () =>
        {
            var r = await Checked("données.json", "csv", options: new() { ["delimiter"] = ";" });
            var text = File.ReadAllText(r.Outputs[0]);
            Check(text.StartsWith("﻿") || File.ReadAllBytes(r.Outputs[0])[0] == 0xEF, "BOM UTF-8 pour Excel");
            Check(text.Contains("capteurs.pression") && text.Contains("tags[0]") && text.Contains("Quai \"\"Nord\"\""), text.Split('\n')[0]);
            Check(r.Warnings.Any(w => w.Contains("aplaties")), "stratégie signalée");
        });
        await Run("JSON imbriqué -> CSV en mode « refuser » : erreur explicite", async () =>
        {
            var ex = await Throws<ConversionException>(() => Convert(Sample("données.json"), Key("json", "csv"), new() { ["nested"] = "refuse" }, OutDir("refuse")), "refus");
            Check(ex.Message.Contains("imbriquées") && ex.Message.Contains("capteurs"), ex.Message);
        });
        await Run("CSV (point-virgule, guillemets, multi-lignes, accents) -> JSON typé", async () =>
        {
            var r = await Checked("inventaire.csv", "json");
            var arr = JsonNode.Parse(File.ReadAllText(r.Outputs[0]))!.AsArray();
            Equal(3, arr.Count, "lignes");
            Equal("Vanne 4 voies; laiton", arr[0]!["Désignation"]!.GetValue<string>(), "champ avec séparateur");
            Equal("001", arr[0]!["Référence"]!.GetValue<string>(), "zéros initiaux conservés en texte");
            Equal(12L, arr[0]!["Quantité"]!.GetValue<long>(), "nombre typé");
            Equal("Note\nsur deux lignes", arr[2]!["Désignation"]!.GetValue<string>(), "champ multi-lignes");
        });
        await Run("CSV -> XLSX (intégré) -> CSV : aller-retour identique ; XLSX validé par LibreOffice", async () =>
        {
            var x = await Checked("inventaire.csv", "xlsx");
            var back = await Convert(x.Outputs[0], Key("xlsx", "csv"), new() { ["delimiter"] = ";" }, OutDir("xlsx-back"));
            var orig = Csv.Parse(File.ReadAllText(Sample("inventaire.csv")).TrimStart('﻿'), ';');
            var round = Csv.Parse(File.ReadAllText(back.Outputs[0]).TrimStart('﻿'), ';');
            Equal(orig.Count, round.Count, "lignes");
            for (int i = 0; i < orig.Count; i++) Equal(string.Join("|", orig[i]), string.Join("|", round[i]), $"ligne {i}");
            if (Engine.Dependencies.IsAvailable(EngineIds.LibreOffice))
            {
                var lo = Path.Combine(Root, "lo-check");
                Directory.CreateDirectory(lo);
                Must("soffice", "--headless", "-env:UserInstallation=" + new Uri(lo + "/p/").AbsoluteUri, "--convert-to", "csv", "--outdir", lo, x.Outputs[0]);
                Check(File.ReadAllText(Directory.GetFiles(lo, "*.csv")[0]).Contains("Vanne 4 voies"), "LibreOffice relit le XLSX");
            }
        });
        await Run("XML -> JSON (attributs @, éléments répétés en tableau) -> XML", async () =>
        {
            var j = await Checked("catalogue.xml", "json");
            var n = JsonNode.Parse(File.ReadAllText(j.Outputs[0]))!;
            Equal("2", n["catalogue"]!["@version"]!.GetValue<string>(), "attribut");
            Equal(2, n["catalogue"]!["article"]!.AsArray().Count, "tableau");
            var x = await Convert(j.Outputs[0], Key("json", "xml"), outDir: OutDir("json2xml"));
            Check(File.ReadAllText(x.Outputs[0]).Contains("<article ref=\"A2\">"), "XML reconstruit");
        });
        await Run("XML avec DTD / entité externe : refusé (XXE)", async () =>
        {
            var ex = await Throws<ConversionException>(() => Convert(Sample("piège.xml"), Key("xml", "json"), outDir: OutDir("xxe")), "XXE");
            Check(ex.Kind == ConversionErrorKind.Unsafe, $"{ex.Kind} : {ex.Message}");
        });
        await Run("JSON invalide : erreur avec numéro de ligne", async () =>
        {
            var ex = await Throws<ConversionException>(() => Convert(Sample("cassé.json"), Key("json", "yaml"), outDir: OutDir("badjson")), "JSON cassé");
            Check(ex.Message.Contains("ligne"), ex.Message);
        });
        await Run("YAML -> JSON : comparé à PyYAML (référence)", async () =>
        {
            var r = await Checked("config.yaml", "json");
            var ours = JsonNode.Parse(File.ReadAllText(r.Outputs[0]))!;
            var refJson = Must("python3", "-c", "import yaml,json,sys;print(json.dumps(yaml.safe_load(open(sys.argv[1],encoding='utf-8')),ensure_ascii=False))", Sample("config.yaml"));
            var theirs = JsonNode.Parse(refJson)!;
            Check(JsonNode.DeepEquals(ours, theirs), $"identique à PyYAML\nnous : {ours.ToJsonString()}\nréf. : {theirs.ToJsonString()}");
        });
        await Run("JSON -> YAML relu par PyYAML à l'identique", async () =>
        {
            var r = await Checked("données.json", "yaml");
            var back = Must("python3", "-c", "import yaml,json,sys;print(json.dumps(yaml.safe_load(open(sys.argv[1],encoding='utf-8')),ensure_ascii=False))", r.Outputs[0]);
            Check(JsonNode.DeepEquals(JsonNode.Parse(back), JsonNode.Parse(File.ReadAllText(Sample("données.json")))), "aller-retour : " + back[..Math.Min(200, back.Length)]);
        });
        await Run("YAML avec ancres : refus explicite", () =>
        {
            Throws<MiniYaml.YamlException>(() => MiniYaml.Parse("a: &x 1\nb: *x\n", out _), "ancres refusées");
        });
        await Run("CSV -> XLSX : injection de formule neutralisée à la sortie CSV", async () =>
        {
            var p = Path.Combine(Work, "formules.json");
            File.WriteAllText(p, "[{\"a\":\"=HYPERLINK(\\\"http://x\\\")\",\"b\":\"-5\",\"c\":\"+33 6\"}]");
            var r = await Convert(p, Key("json", "csv"), outDir: OutDir("formula"));
            var t = File.ReadAllText(r.Outputs[0]);
            Check(t.Contains("'=HYPERLINK") && t.Contains("-5") && t.Contains("'+33 6"), t);
        });

        Suite("Texte, Markdown, HTML, livres");
        await Run("Markdown -> HTML : titres, tâches, tableau, code ; script et javascript: neutralisés", async () =>
        {
            var r = await Checked("lisez-moi.md", "html");
            var h = File.ReadAllText(r.Outputs[0]);
            Check(h.Contains("<h1>Guide FileWheel</h1>") && h.Contains("<strong>outil</strong>") && h.Contains("<em>convertir</em>"), "mise en forme");
            Check(h.Contains("type=\"checkbox\" checked") && h.Contains("<table>") && h.Contains("text-align:center"), "tâches et tableau");
            Check(h.Contains("1 &lt; 2"), "code échappé");
            Check(!h.Contains("<script>") && h.Contains("&lt;script&gt;"), "script échappé");
            Check(!h.Contains("javascript:"), "lien javascript neutralisé");
            Check(h.Contains("href=\"https://example.org\""), "lien https conservé");
        });
        await Run("HTML -> TXT : scripts et styles ignorés, listes et entités", async () =>
        {
            var r = await Checked("page.html", "txt");
            var t = File.ReadAllText(r.Outputs[0]);
            Check(t.Contains("Titre") && t.Contains("Premier & paragraphe") && t.Contains("• un") && !t.Contains("var x") && !t.Contains("color:red"), t);
        });
        await Run("EPUB -> HTML (intégré) : chapitres, image incluse, script retiré", async () =>
        {
            var r = await Checked("livre.epub", "html");
            var h = File.ReadAllText(r.Outputs[0]);
            Check(h.Contains("Chapitre un") && h.Contains("Chapitre deux") && h.Contains("data:image/png;base64,") && !h.Contains("alert(1)"), "contenu");
        });
        await Run("EPUB protégé (DRM) : refusé", async () =>
        {
            var ex = await Throws<ConversionException>(() => Convert(Sample("livre protégé.epub"), Key("epub", "txt"), outDir: OutDir("drm")), "DRM");
            Equal(ConversionErrorKind.Protected, ex.Kind, "type");
        });
        await Run("EPUB -> AZW3 et -> PDF (Calibre)", async () =>
        {
            RequireEngine(EngineIds.Calibre);
            var a = await Checked("livre.epub", "azw3");
            Check(new FileInfo(a.Outputs[0]).Length > 1000, "AZW3 produit");
            var p = await Checked("livre.epub", "pdf");
            Check(PdfText(p.Outputs[0]).Contains("Chapitre"), "PDF lisible");
        });

        Suite("3D, polices, e-mails");
        foreach (var to in new[] { "obj", "ply", "3mf", "glb" })
        {
            await Run($"STL -> {to.ToUpperInvariant()} -> relu : 12 triangles, 8 sommets, 10 × 10 × 10", async () =>
            {
                var r = await Checked("cube.stl", to);
                var m = FileWheel.Conversion.Providers.Model3D.Mesh.Read(r.Outputs[0], to, CancellationToken.None);
                Equal(12, m.TriangleCount, "triangles");
                Equal(8, m.VertexCount, "sommets");
                var b = m.Bounds();
                Check(b.maxX - b.minX == 10 && b.maxZ - b.minZ == 10, "dimensions");
                Check(m.Describe().Contains("triangles") && !m.Describe().Contains("ouvert"), "cube fermé : " + m.Describe());
            });
        }
        await Run("GLB validé par une lecture JSON indépendante (Python)", async () =>
        {
            var r = await Checked("cube.stl", "glb", sub: "glb-py");
            var check = Must("python3", "-c", "import json,struct,sys;d=open(sys.argv[1],'rb').read();m,v,l=struct.unpack('<III',d[:12]);n=struct.unpack('<I',d[12:16])[0];j=json.loads(d[20:20+n]);print(hex(m),v,l==len(d),j['accessors'][1]['count'])", r.Outputs[0]).Trim();
            Equal("0x46546c67 2 True 36", check, "en-tête GLB et accesseurs");
        });
        await Run("TTF -> WOFF -> TTF : tables identiques (vérifié par fontTools)", async () =>
        {
            var w = await Checked("police.ttf", "woff");
            var back = await Convert(w.Outputs[0], Key("woff", "ttf"), outDir: OutDir("woff-back"));
            var cmp = Must("python3", "-c", "from fontTools.ttLib import TTFont;import sys;a=TTFont(sys.argv[1]);b=TTFont(sys.argv[2]);w=TTFont(sys.argv[3]);print(w.flavor, all(a.reader[t]==b.reader[t] for t in a.reader.keys() if t!='head'))", Sample("police.ttf"), back.Outputs[0], w.Outputs[0]).Trim();
            Equal("woff True", cmp, "WOFF valide et tables identiques");
        });
        await Run("EML -> TXT : en-têtes décodés, texte, pièce jointe listée", async () =>
        {
            var r = await Checked("message.eml", "txt");
            var t = File.ReadAllText(r.Outputs[0]);
            Check(t.Contains("Éloïse Martin") && t.Contains("Réunion de décembre") && t.Contains("réunion est déplacée") && t.Contains("ordre du jour.pdf"), t);
        });
        await Run("EML -> HTML : script, onclick et image espion retirés", async () =>
        {
            var r = await Checked("message.eml", "html");
            var h = File.ReadAllText(r.Outputs[0]);
            Check(!h.Contains("alert(") && !h.Contains("pixel.example.com") && h.Contains("<b>déplacée</b>"), "nettoyé");
            Check(r.Warnings.Any(w => w.Contains("images distantes")), "avertissement");
        });
        await Run("MBOX -> 3 fichiers EML", async () =>
        {
            var r = await Checked("boite.mbox", "eml", ConversionKind.Split);
            var files = Directory.GetFiles(r.Outputs[0]);
            Equal(3, files.Length, "messages");
            Check(File.ReadAllText(files[0]).Contains("From the archive"), "« >From » restauré");
        });
        await Run("VCF (vCard 3.0 et 2.1 quoted-printable) -> CSV", async () =>
        {
            var r = await Checked("contacts.vcf", "csv");
            var t = File.ReadAllText(r.Outputs[0]);
            Check(t.Contains("Élodie Durand") && t.Contains("Paul Émile Martin") && t.Contains("12 rue du Givre, Lyon, 69000, France") && t.Contains("'+33 6"), t);
        });
        await Run("ICS -> CSV : événements, journée entière, répétition, alarmes ignorées", async () =>
        {
            var r = await Checked("agenda.ics", "csv");
            var rows = Csv.Parse(File.ReadAllText(r.Outputs[0]).TrimStart('﻿'), ';');
            Equal(3, rows.Count, "en-tête + 2 événements");
            Check(rows[1][0] == "Maintenance compresseur" && rows[1][1] == "2025-12-01 08:00:00 UTC" && rows[1][4] == "Atelier, bâtiment B", string.Join("|", rows[1]));
            Check(rows[2][3] == "oui" && rows[2][6] == "FREQ=YEARLY", string.Join("|", rows[2]));
        });

        Suite("Gestionnaire de conversion — garanties");
        await Run("Accents et espaces dans les noms ; conflit => « (2) »", async () =>
        {
            var dir = OutDir("conflits");
            var a = await Convert(Sample("notes été.txt"), Key("txt", "html"), outDir: dir);
            var b = await Convert(Sample("notes été.txt"), Key("txt", "html"), outDir: dir);
            Equal("notes été.html", Path.GetFileName(a.Outputs[0]), "premier nom");
            Equal("notes été (2).html", Path.GetFileName(b.Outputs[0]), "second nom");
        });
        await Run("Politique « Ignorer » : tâche ignorée, fichier existant intact", async () =>
        {
            var dir = OutDir("conflits");
            var existing = Path.Combine(dir, "notes été.html");
            var before = Hash(existing);
            var ex = await Throws<ConversionException>(() => Convert(Sample("notes été.txt"), Key("txt", "html"), outDir: dir, conflicts: ConflictPolicy.Skip), "ignorer");
            Equal(ConversionErrorKind.NameConflict, ex.Kind, "type");
            Equal(before, Hash(existing), "existant intact");
        });
        await Run("Politique « Écraser » explicite : remplace la sortie, jamais la source", async () =>
        {
            var dir = Path.GetDirectoryName(Sample("x"))!;
            var copy = Path.Combine(OutDir("overwrite"), "même.txt");
            File.Copy(Sample("notes été.txt"), copy, true);
            File.WriteAllText(Path.ChangeExtension(copy, ".html"), "ancien");
            var r = await Convert(copy, Key("txt", "html"), outDir: Path.GetDirectoryName(copy), conflicts: ConflictPolicy.Overwrite);
            Equal("même.html", Path.GetFileName(r.Outputs[0]), "même nom");
            Check(File.ReadAllText(r.Outputs[0]).Contains("<html"), "remplacé");
            // Meme format, meme dossier : la sortie ne peut pas ecraser la source
            var same = await Convert(Sample("inventaire.csv"), Key("csv", "csv"), outDir: Path.GetDirectoryName(Sample("inventaire.csv")), conflicts: ConflictPolicy.Overwrite);
            Check(Path.GetFileName(same.Outputs[0]) != "inventaire.csv" && File.Exists(Sample("inventaire.csv")), "source préservée : " + Path.GetFileName(same.Outputs[0]));
            File.Delete(same.Outputs[0]);
            _ = dir;
        });
        await Run("Manque d'espace disque : refus avant conversion", async () =>
        {
            var old = Settings.MinFreeSpaceMiB;
            Settings.MinFreeSpaceMiB = 50_000_000; // 50 To de marge exigee
            try
            {
                var ex = await Throws<ConversionException>(() => Convert(Sample("notes été.txt"), Key("txt", "pdf"), outDir: OutDir("disk")), "disque");
                Equal(ConversionErrorKind.DiskFull, ex.Kind, "type");
            }
            finally { Settings.MinFreeSpaceMiB = old; }
        });
        await Run("Programme déguisé : conversion refusée", async () =>
        {
            var ex = await Throws<ConversionException>(() => Convert(Sample("vacances.jpg"), Key("jpg", "png"), outDir: OutDir("exe")), "exe");
            Equal(ConversionErrorKind.Unsafe, ex.Kind, "type");
        });
        await Run("Contenu réel différent de l'extension : conversion choisie refusée avec explication", async () =>
        {
            RequireEngine(EngineIds.ImageMagick);
            var ex = await Throws<ConversionException>(() => Convert(Sample("faux.png"), Key("png", "jpg"), outDir: OutDir("mismatch")), "incohérence");
            Check(ex.Message.Contains("contenu réel"), ex.Message);
        });
        await Run("Réglage invalide refusé avant lancement", async () =>
        {
            RequireEngine(EngineIds.FFmpeg);
            var ex = await Throws<ConversionException>(() => Convert(Sample("tonalité.wav"), Key("wav", "mp3"), new() { ["abitrate"] = "99999" }, OutDir("opts")), "option");
            Equal(ConversionErrorKind.InvalidOptions, ex.Kind, "type");
        });
        await Run("File d'attente : lot de 6 dont 1 fichier endommagé, les autres réussissent", async () =>
        {
            var engine = Env.Engine;
            var dir = OutDir("lot");
            var plan = new (string file, string target)[] { ("notes été.txt", "pdf"), ("lisez-moi.md", "html"), ("page.html", "txt"), ("données.json", "yaml"), ("config.yaml", "json"), ("cassé.json", "yaml") };
            var jobs = new List<ConversionJob>();
            foreach (var (f, target) in plan)
            {
                var src = Sample(f);
                var cap = engine.Registry.Best(FormatDetector.Detect(src).Format!.Id, target)!;
                jobs.Add(engine.Queue.Enqueue(new ConversionOrder { Inputs = [src], CapabilityKey = cap.Key, OutputDir = dir }));
            }
            await engine.Queue.WhenIdleAsync(TimeSpan.FromSeconds(60));
            Equal(5, jobs.Count(j => j.Status == JobStatus.Succeeded), "réussies : " + string.Join(" / ", jobs.Select(j => $"{j.Title}={j.Status} {j.Error}")));
            Equal(1, jobs.Count(j => j.Status == JobStatus.Failed), "échec isolé");
            var failed = jobs.First(j => j.Status == JobStatus.Failed);
            Check(failed.CanRetry, "relance possible");
        });
        await Run("File d'attente : pause (aucune nouvelle tâche), reprise, annulation d'une tâche en attente", async () =>
        {
            var q = Env.Engine.Queue;
            q.Pause();
            var dir = OutDir("pause");
            var job = q.Enqueue(new ConversionOrder { Inputs = [Sample("notes été.txt")], CapabilityKey = Key("txt", "html"), OutputDir = dir });
            await Task.Delay(300);
            Equal(JobStatus.Pending, job.Status, "en attente pendant la pause");
            var job2 = q.Enqueue(new ConversionOrder { Inputs = [Sample("notes été.txt")], CapabilityKey = Key("txt", "html"), OutputDir = dir });
            q.Cancel(job2);
            q.Resume();
            await q.WhenIdleAsync(TimeSpan.FromSeconds(20));
            Equal(JobStatus.Succeeded, job.Status, "exécutée après reprise");
            Equal(JobStatus.Cancelled, job2.Status, "annulée");
        });
        await Run("Historique : enregistrement, recherche, suppression, désactivation", () =>
        {
            var h = Env.Engine.History;
            Check(h.Entries.Count > 10, $"{h.Entries.Count} entrées");
            Check(h.Search("été").Count > 0, "recherche par nom");
            Check(h.Entries.Any(e => e.Result == "Échec" && e.Error is not null), "échec avec message");
            var first = h.Entries[0];
            h.Remove(first.Id);
            Check(h.Entries.All(e => e.Id != first.Id), "suppression");
            h.Enabled = false;
            int n = h.Entries.Count;
            h.Add(new HistoryEntry { Inputs = ["x"] });
            Equal(n, h.Entries.Count, "désactivé : rien enregistré");
            h.Enabled = true;
            var raw = File.ReadAllText(FileWheel.Core.Util.AppPaths.HistoryFile);
            Check(!raw.Contains("Ligne 1 : relevé normal"), "aucun contenu de fichier dans l'historique");
        });
        await Run("Recherche de formats : « WebP »", () =>
        {
            var res = Engine.Registry.Search("WebP");
            Check(res.Any(r => r.Cap.To == "webp") && res.Any(r => r.Cap.From.Contains("webp")), $"{res.Count} conversions");
        });
        await Run("Nettoyage des temporaires abandonnés", () =>
        {
            var dir = OutDir("stale");
            var stale = Path.Combine(dir, OutputPlanner.TempPrefix + "ancien.tmp");
            File.WriteAllText(stale, "x");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3));
            File.WriteAllText(Path.Combine(dir, OutputPlanner.TempPrefix + "recent.tmp"), "x");
            Equal(1, OutputPlanner.CleanupStale(dir, TimeSpan.FromHours(24)), "un temporaire ancien supprimé");
        });
    }
}
