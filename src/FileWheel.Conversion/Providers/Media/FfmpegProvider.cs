using System.Globalization;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Logging;

namespace FileWheel.Conversion.Providers.Media;

/// <summary>
/// Fournisseur audio/video fonde sur FFmpeg (programme externe).
/// Les capacites sont calculees a partir des encodeurs REELLEMENT presents dans le FFmpeg
/// detecte (« ffmpeg -encoders ») : un format dont l'encodeur manque n'est pas propose.
/// </summary>
public sealed class FfmpegProvider : ProviderBase
{
    private readonly DependencyManager _deps;
    private (string? path, HashSet<string> encoders)? _encoderCache;
    private IReadOnlyList<ConversionCapability>? _caps;
    private string? _capsFor;

    public FfmpegProvider(DependencyManager deps)
    {
        _deps = deps;
        deps.Changed += () => { _caps = null; _encoderCache = null; };
    }

    public override string Id => "ffmpeg";
    public override string DisplayName => "FFmpeg (audio et vidéo)";

    public HashSet<string> Encoders()
    {
        var path = _deps.PathOf(EngineIds.FFmpeg);
        if (_encoderCache is { } c && c.path == path) return c.encoders;
        HashSet<string> set;
        if (path is null) set = new HashSet<string>(FfmpegSpecs.AssumedEncoders);
        else
        {
            set = new HashSet<string>();
            try
            {
                var r = ProcessRunner.RunAsync(path, ["-hide_banner", "-encoders"], CancellationToken.None, timeout: TimeSpan.FromSeconds(20), tailChars: 200_000)
                    .GetAwaiter().GetResult();
                foreach (var line in r.StdOutTail.Split('\n'))
                {
                    var t = line.Trim();
                    // Format : « V....D libx264   description »
                    if (t.Length > 8 && t[6] == ' ' && "VAS".Contains(t[0]))
                    {
                        var name = t[7..].TrimStart().Split(' ', 2)[0];
                        if (name.Length > 0 && name != "=") set.Add(name);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Lecture des encodeurs FFmpeg impossible : " + ex.Message); }
            if (set.Count == 0) set = new HashSet<string>(FfmpegSpecs.AssumedEncoders);
        }
        _encoderCache = (path, set);
        return set;
    }

    public override IReadOnlyList<ConversionCapability> Capabilities
    {
        get
        {
            var key = _deps.PathOf(EngineIds.FFmpeg) ?? "";
            if (_caps is not null && _capsFor == key) return _caps;
            _caps = BuildCapabilities(Encoders());
            _capsFor = key;
            return _caps;
        }
    }

    private List<ConversionCapability> BuildCapabilities(HashSet<string> enc)
    {
        var caps = new List<ConversionCapability>();
        var videoSrc = FfmpegSpecs.VideoSources;
        var audioSrc = FfmpegSpecs.AudioSources;
        string E = EngineIds.FFmpeg;

        var timeOpts = new List<OptionDef>
        {
            OptionDef.Time("start", "Début", "Laisser vide pour le début. Exemples : 30, 1:30, 00:01:30.5") with { Advanced = true },
            OptionDef.Time("end", "Fin", "Laisser vide pour la fin du fichier.") with { Advanced = true },
        };

        foreach (var (target, spec) in FfmpegSpecs.Containers)
        {
            if (!spec.VideoEncoders.Any(enc.Contains)) continue;
            var opts = new List<OptionDef>
            {
                OptionDef.Choice("profile", "Profil", "balanced", FfmpegCommand.VideoProfiles),
                OptionDef.Choice("mode", "Mode", "auto", ("auto", "Automatique (copie sans perte si possible)"), ("reencode", "Toujours réencoder"), ("copy", "Copie sans réencodage uniquement")),
                OptionDef.Choice("resolution", "Résolution", "orig", ("orig", "Originale"), ("2160", "2160p (4K)"), ("1440", "1440p"), ("1080", "1080p"), ("720", "720p"), ("480", "480p"), ("360", "360p")) with { Help = "Réduction uniquement : une petite vidéo n'est jamais agrandie." },
                OptionDef.Choice("fps", "Images par seconde", "orig", ("orig", "Originale"), ("60", "60"), ("50", "50"), ("30", "30"), ("25", "25"), ("24", "24")) with { Advanced = true },
                OptionDef.Choice("vcodec", "Codec vidéo", "auto", [("auto", "Automatique"), .. spec.VideoEncoders.Where(enc.Contains).Select(e => (e, e))]) with { Advanced = true },
                OptionDef.Int("vbitrate", "Débit vidéo (0 = selon le profil)", 0, 0, 200_000, "kbit/s") with { Advanced = true },
                OptionDef.Int("crf", "Qualité constante (-1 = selon le profil)", -1, -1, 63, "", "Plus petit = meilleure qualité.") with { Advanced = true },
                OptionDef.Choice("audio", "Audio", "all", ("all", "Toutes les pistes"), ("first", "Première piste seulement"), ("none", "Supprimer l'audio")),
                OptionDef.Choice("acodec", "Codec audio", "auto", [("auto", "Automatique"), .. spec.AudioEncoders.Where(enc.Contains).Select(e => (e, e))]) with { Advanced = true },
                OptionDef.Int("abitrate", "Débit audio (0 = selon le profil)", 0, 0, 1536, "kbit/s") with { Advanced = true },
                OptionDef.Choice("subtitles", "Sous-titres", "keep", ("keep", "Conserver si possible"), ("drop", "Supprimer")),
                OptionDef.Choice("rotate", "Rotation", "0", ("0", "Aucune"), ("90", "90° horaire"), ("180", "180°"), ("270", "90° anti-horaire")) with { Advanced = true },
                OptionDef.Bool("metadata", "Conserver les métadonnées", true) with { Advanced = true },
            };
            opts.AddRange(timeOpts);
            caps.Add(Cap(Id, videoSrc, target, ConversionKind.Convert, opts, LossKind.Lossy,
                "Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement.",
                engine: E, priority: 10));
        }

        if (enc.Contains("gif"))
        {
            caps.Add(Cap(Id, videoSrc.Where(v => v != "gif"), "gif", ConversionKind.Convert,
            [
                OptionDef.Choice("gif_fps", "Images par seconde", "12", ("8", "8"), ("10", "10"), ("12", "12"), ("15", "15"), ("20", "20"), ("25", "25")),
                OptionDef.Choice("gif_width", "Largeur", "480", ("240", "240 px"), ("320", "320 px"), ("480", "480 px"), ("640", "640 px"), ("800", "800 px"), ("orig", "Originale")),
                OptionDef.Bool("loop", "Lecture en boucle", true),
                .. timeOpts,
            ], LossKind.Lossy, "GIF : palette de 256 couleurs, pas de son.", engine: E, priority: 10, label: "Convertir en GIF animé"));
        }

        foreach (var img in new[] { "jpg", "png" })
        {
            var encName = img == "jpg" ? "mjpeg" : "png";
            if (!enc.Contains(encName)) continue;
            caps.Add(Cap(Id, videoSrc, img, ConversionKind.Thumbnail,
            [
                OptionDef.Time("at", "Position", "Laisser vide : 10 % de la durée."),
                OptionDef.Choice("thumb_width", "Largeur", "orig", ("orig", "Originale"), ("1920", "1920 px"), ("1280", "1280 px"), ("640", "640 px"), ("320", "320 px")),
            ], img == "jpg" ? LossKind.Lossy : LossKind.None, engine: E, priority: 20, label: $"Miniature ({img.ToUpperInvariant()})"));
            caps.Add(Cap(Id, videoSrc, img, ConversionKind.ImageSequence,
            [
                OptionDef.Choice("seq_fps", "Cadence", "1", ("1", "1 image par seconde"), ("1/5", "1 image toutes les 5 s"), ("1/10", "1 image toutes les 10 s"), ("5", "5 images par seconde"), ("all", "Toutes les images")),
                OptionDef.Choice("thumb_width", "Largeur", "orig", ("orig", "Originale"), ("1920", "1920 px"), ("1280", "1280 px"), ("640", "640 px")),
                .. timeOpts,
            ], img == "jpg" ? LossKind.Lossy : LossKind.None, engine: E, priority: 30, folder: true, label: $"Séquence d'images ({img.ToUpperInvariant()})"));
        }

        foreach (var (target, spec) in FfmpegSpecs.AudioFormats)
        {
            if (!spec.Encoders.Any(enc.Contains)) continue;
            var audioOpts = new List<OptionDef>
            {
                OptionDef.Choice("profile", "Profil", spec.Lossless ? "lossless" : "music", FfmpegCommand.AudioProfiles),
                OptionDef.Int("abitrate", "Débit (0 = selon le profil)", 0, 0, 1536, "kbit/s") with { Advanced = true },
                OptionDef.Choice("samplerate", "Fréquence", "orig", ("orig", "Originale"), ("48000", "48 kHz"), ("44100", "44,1 kHz"), ("32000", "32 kHz"), ("22050", "22 kHz"), ("16000", "16 kHz")) with { Advanced = true },
                OptionDef.Choice("channels", "Canaux", "orig", ("orig", "Originaux"), ("stereo", "Stéréo"), ("mono", "Mono")),
                OptionDef.Bool("normalize", "Normaliser le volume", false),
                OptionDef.Bool("copy", "Copier sans réencodage si possible", true) with { Advanced = true },
                OptionDef.Bool("cover", "Conserver la pochette", true) with { Advanced = true },
                OptionDef.Bool("metadata", "Conserver les métadonnées (titre, artiste…)", true) with { Advanced = true },
            };
            if (target is "wav" or "aiff") audioOpts.Add(OptionDef.Choice("bits", "Résolution", "16", ("16", "16 bits"), ("24", "24 bits")));
            audioOpts.AddRange(timeOpts);
            var loss = spec.Lossless ? LossKind.None : LossKind.Lossy;
            caps.Add(Cap(Id, audioSrc, target, ConversionKind.Convert, audioOpts, loss, engine: E, priority: 10));
            caps.Add(Cap(Id, videoSrc.Where(v => v != "gif"), target, ConversionKind.ExtractAudio, audioOpts, loss, engine: E, priority: 15,
                label: $"Extraire l'audio ({FileWheel.Core.Formats.FormatCatalog.Get(target)?.Label})"));
            if (target is not "amr")
                caps.Add(Cap(Id, audioSrc, target, ConversionKind.Merge,
                [
                    OptionDef.Choice("profile", "Profil", spec.Lossless ? "lossless" : "music", FfmpegCommand.AudioProfiles),
                    OptionDef.Int("abitrate", "Débit (0 = selon le profil)", 0, 0, 1536, "kbit/s") with { Advanced = true },
                    OptionDef.Bool("metadata", "Conserver les métadonnées du premier fichier", true) with { Advanced = true },
                ], loss, engine: E, priority: 40, multi: true, batch: false, label: $"Fusionner en un seul {FileWheel.Core.Formats.FormatCatalog.Get(target)?.Label}"));
        }
        return caps;
    }

    public override ConversionAnalysis Analyze(ConversionRequest request)
    {
        var a = base.Analyze(request);
        var ffmpeg = _deps.PathOf(EngineIds.FFmpeg);
        if (ffmpeg is null) return a;
        try
        {
            var info = MediaProbe.ProbeAsync(_deps.PathOf(EngineIds.FFprobe), ffmpeg, request.Input, CancellationToken.None).GetAwaiter().GetResult();
            var plan = BuildPlan(request, info, Encoders(), forceReencode: false, mergeInfos: null);
            a.Notes.AddRange(plan.Notes);
            a.Warnings.AddRange(plan.Warnings);
            if (info.Duration is { } d)
            {
                long? bitrate = info.BitRate;
                if (plan.Remux && bitrate is { } br) a.EstimatedBytes = (long)(br / 8.0 * (plan.ExpectedDuration ?? d));
            }
        }
        catch (ConversionException ex)
        {
            a.Warnings.Add(ex.Message);
            if (ex.Kind is ConversionErrorKind.InvalidInput or ConversionErrorKind.InvalidOptions or ConversionErrorKind.NotSupported or ConversionErrorKind.MissingCodec) a.Blocking = true;
        }
        catch (Exception ex) { Log.Debug("Analyse FFmpeg : " + ex.Message); }
        return a;
    }

    private FfmpegPlan BuildPlan(ConversionRequest req, MediaInfo info, HashSet<string> enc, bool forceReencode, IReadOnlyList<MediaInfo>? mergeInfos)
    {
        var cap = req.Capability;
        return cap.Kind switch
        {
            ConversionKind.Convert when cap.To == "gif" => FfmpegCommand.VideoToGif(req, info),
            ConversionKind.Convert when FfmpegSpecs.Containers.TryGetValue(cap.To, out var spec) => FfmpegCommand.VideoToVideo(req, info, spec, enc, forceReencode),
            ConversionKind.Convert or ConversionKind.ExtractAudio when FfmpegSpecs.AudioFormats.TryGetValue(cap.To, out var aspec) =>
                FfmpegCommand.ToAudio(req, info, aspec, enc, cap.Kind == ConversionKind.ExtractAudio),
            ConversionKind.Merge => FfmpegCommand.MergeAudio(req, mergeInfos ?? [info], FfmpegSpecs.AudioFormats[cap.To], enc),
            ConversionKind.Thumbnail => FfmpegCommand.Thumbnail(req, info, cap.To),
            ConversionKind.ImageSequence => FfmpegCommand.ImageSequence(req, info, cap.To),
            _ => throw new ConversionException(ConversionErrorKind.NotSupported, "Conversion non prise en charge par FFmpeg."),
        };
    }

    public override async Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var ffmpeg = _deps.Require(EngineIds.FFmpeg);
        var ffprobe = _deps.PathOf(EngineIds.FFprobe);
        progress.Report(new ConversionProgress(null, "Analyse du fichier…"));
        var info = await MediaProbe.ProbeAsync(ffprobe, ffmpeg, req.Input, ct).ConfigureAwait(false);
        List<MediaInfo>? merge = null;
        if (req.Capability.Kind == ConversionKind.Merge)
        {
            merge = new List<MediaInfo>();
            foreach (var i in req.Inputs) merge.Add(await MediaProbe.ProbeAsync(ffprobe, ffmpeg, i, ct).ConfigureAwait(false));
        }
        bool needsVideo = req.Capability.Kind is ConversionKind.Thumbnail or ConversionKind.ImageSequence
            || (req.Capability.Kind == ConversionKind.Convert && (FfmpegSpecs.Containers.ContainsKey(req.TargetFormat) || req.TargetFormat == "gif"));
        if (needsVideo && !info.HasVideo)
            throw new ConversionException(ConversionErrorKind.InvalidInput, "Ce fichier ne contient pas de piste vidéo.");

        var enc = Encoders();
        var plan = BuildPlan(req, info, enc, forceReencode: false, merge);
        var warnings = new List<string>(plan.Warnings);
        var result = await RunFfmpeg(ffmpeg, plan, progress, ct).ConfigureAwait(false);

        if (result.ExitCode != 0 && plan.Remux && req.Capability.Kind == ConversionKind.Convert && req.Opt("mode") == "auto")
        {
            // Copie refusee par le conteneur (codec ou marqueur inattendu) : on reencode.
            Log.Info("Remuxage refusé par FFmpeg, nouvel essai avec réencodage : " + ProcessRunner.LastLines(result.StdErrTail, 3));
            CleanOutput(req);
            plan = BuildPlan(req, info, enc, forceReencode: true, merge);
            warnings.Add("La copie sans réencodage a échoué : la vidéo a été réencodée (avec perte).");
            warnings.AddRange(plan.Warnings);
            result = await RunFfmpeg(ffmpeg, plan, progress, ct).ConfigureAwait(false);
        }
        if (result.ExitCode != 0) throw Interpret(result);

        // Verification : le fichier produit doit etre lisible et contenir ce qui est annonce.
        if (!req.Capability.OutputIsFolder && req.Capability.Kind is not ConversionKind.Thumbnail && req.TargetFormat != "gif")
        {
            progress.Report(new ConversionProgress(1, "Vérification…"));
            var outInfo = await MediaProbe.ProbeAsync(ffprobe, ffmpeg, req.TempOutput, ct).ConfigureAwait(false);
            bool wantVideo = FfmpegSpecs.Containers.ContainsKey(req.TargetFormat);
            if (wantVideo && !outInfo.HasVideo) throw new ConversionException(ConversionErrorKind.WriteError, "Le fichier produit ne contient pas de vidéo.");
            if (!wantVideo && !outInfo.HasAudio) throw new ConversionException(ConversionErrorKind.WriteError, "Le fichier produit ne contient pas d'audio.");
            if (plan.ExpectedDuration is { } exp && exp > 2 && outInfo.Duration is { } got && got < exp * 0.9 - 1)
                warnings.Add($"Durée produite ({got:0.#} s) inférieure à la durée attendue ({exp:0.#} s) : la source est peut-être endommagée.");
        }
        return new ConversionResult([req.TempOutput], warnings.Concat(plan.Notes.Where(n => n.StartsWith("Remuxage") || n.StartsWith("Réencodage"))).ToList());
    }

    private static void CleanOutput(ConversionRequest req)
    {
        try
        {
            if (req.Capability.OutputIsFolder) { foreach (var f in Directory.EnumerateFiles(req.TempOutput)) File.Delete(f); }
            else if (File.Exists(req.TempOutput)) File.Delete(req.TempOutput);
        }
        catch { }
    }

    private static async Task<ProcessResult> RunFfmpeg(string ffmpeg, FfmpegPlan plan, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        double total = plan.ExpectedDuration ?? 0;
        var started = DateTime.UtcNow;
        progress.Report(new ConversionProgress(total > 0 ? 0 : null, plan.Remux ? "Copie des flux…" : "Conversion…"));
        return await ProcessRunner.RunAsync(ffmpeg, plan.Args, ct, onStdOutLine: line =>
        {
            // -progress pipe:1 : lignes « cle=valeur »
            if (total <= 0) return;
            if (line.StartsWith("out_time_us=") || line.StartsWith("out_time_ms="))
            {
                if (long.TryParse(line.AsSpan(line.IndexOf('=') + 1), out var us) && us > 0)
                {
                    var f = Math.Clamp(us / 1_000_000.0 / total, 0, 0.999);
                    var elapsed = (DateTime.UtcNow - started).TotalSeconds;
                    TimeSpan? remaining = f > 0.05 && elapsed > 3 ? TimeSpan.FromSeconds(elapsed * (1 - f) / f) : null;
                    progress.Report(new ConversionProgress(f, plan.Remux ? "Copie des flux…" : "Conversion…", remaining));
                }
            }
        }).ConfigureAwait(false);
    }

    private static ConversionException Interpret(ProcessResult r)
    {
        var err = r.StdErrTail;
        var tail = ProcessRunner.LastLines(err, 4);
        if (err.Contains("No space left", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.DiskFull, "Disque plein pendant la conversion.", tail);
        if (err.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase) || err.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.MissingCodec, "Codec absent de cette version de FFmpeg.", tail);
        if (err.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase) || err.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.Corrupt, "Fichier source endommagé ou incomplet.", tail);
        if (err.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.PermissionDenied, "Accès refusé au fichier ou au dossier.", tail);
        if (err.Contains("not currently supported in container", StringComparison.OrdinalIgnoreCase) || err.Contains("Could not find tag for codec", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.NotSupported, "Un des flux n'est pas accepté par le format choisi.", tail);
        if (err.Contains("Decryption", StringComparison.OrdinalIgnoreCase) || err.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            return new ConversionException(ConversionErrorKind.Protected, "Fichier protégé (DRM) : conversion impossible.", tail);
        return new ConversionException(ConversionErrorKind.EngineCrashed, $"FFmpeg a échoué (code {r.ExitCode}). Détails dans le journal.", tail);
    }

    internal static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
