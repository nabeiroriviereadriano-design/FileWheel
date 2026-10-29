using System.Globalization;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Media;

/// <summary>Plan de commande FFmpeg et explications associees (remuxage ou reencodage).</summary>
public sealed class FfmpegPlan
{
    public List<string> Args { get; } = new();
    public bool Remux { get; set; }
    public List<string> Notes { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Duree de sortie attendue (pour la progression).</summary>
    public double? ExpectedDuration { get; set; }
}

/// <summary>
/// Construction des arguments FFmpeg, sans aucune execution : testable et lisible.
/// Chaque chemin et chaque valeur est un argument distinct (aucune chaine de commande).
/// </summary>
public static class FfmpegCommand
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly (string v, string l)[] VideoProfiles =
    [
        ("balanced", "Qualité équilibrée"), ("max", "Qualité maximale"), ("small", "Taille réduite"),
        ("mobile", "Compatibilité mobile"), ("web", "Compatibilité Web"), ("custom", "Personnalisé"),
    ];

    public static readonly (string v, string l)[] AudioProfiles =
    [
        ("music", "Musique"), ("max", "Qualité maximale"), ("voice", "Voix"), ("small", "Taille réduite"), ("lossless", "Sans perte (si le format le permet)"),
    ];

    // ------------------------------------------------------------------ video -> video

    public static FfmpegPlan VideoToVideo(ConversionRequest req, MediaInfo info, ContainerSpec spec, ISet<string> encoders, bool forceReencode = false)
    {
        var plan = new FfmpegPlan();
        var a = plan.Args;
        string mode = req.Opt("mode");
        string profile = req.Opt("profile");
        var (start, dur) = Trim(req, info, plan);

        bool needsFilters = req.Opt("resolution") != "orig" || req.Opt("fps") != "orig" || req.Opt("rotate") != "0" || profile == "mobile";
        bool removeAudio = req.Opt("audio") == "none";
        bool trimmed = start > 0 || dur is not null;
        bool remuxPossible = !needsFilters && (!trimmed || mode == "copy") && RemuxCompatible(info, spec, removeAudio, req.Opt("audio") == "first");

        if (mode == "copy" && !remuxPossible && !forceReencode)
            throw new ConversionException(ConversionErrorKind.NotSupported,
                $"Copie sans réencodage impossible : les codecs de la source ne sont pas acceptés par {spec.Id.ToUpperInvariant()} ou des réglages exigent un réencodage. Choisissez le mode « Automatique ».");
        plan.Remux = remuxPossible && mode != "reencode" && !forceReencode;

        Common(a);
        if (start > 0) { a.Add("-ss"); a.Add(start.ToString("0.###", Inv)); }
        a.Add("-i"); a.Add(req.Input);
        if (dur is { } d) { a.Add("-t"); a.Add(d.ToString("0.###", Inv)); }

        // Selection des pistes
        a.Add("-map"); a.Add("0:v:0?");
        if (!removeAudio)
        {
            a.Add("-map"); a.Add(req.Opt("audio") == "first" ? "0:a:0?" : "0:a?");
        }
        bool keepSubs = req.Opt("subtitles") == "keep";
        var subs = info.Subtitles.ToList();
        if (keepSubs && subs.Count > 0)
        {
            switch (spec.Subtitles)
            {
                case SubtitleMode.Copy:
                    a.Add("-map"); a.Add("0:s?");
                    break;
                case SubtitleMode.MovText:
                case SubtitleMode.WebVtt:
                    var text = subs.Where(s => FfmpegSpecs.TextSubtitleCodecs.Contains(s.Codec)).ToList();
                    foreach (var s in text) { a.Add("-map"); a.Add("0:" + s.Index); }
                    if (text.Count < subs.Count)
                        plan.Warnings.Add($"{subs.Count - text.Count} piste(s) de sous-titres en image (DVD/Blu-ray) ne peuvent pas aller dans {spec.Id.ToUpperInvariant()} : elles sont retirées. Choisissez MKV pour les conserver.");
                    break;
                case SubtitleMode.Drop:
                    plan.Warnings.Add($"Le format {spec.Id.ToUpperInvariant()} ne transporte pas les sous-titres : ils sont retirés.");
                    break;
            }
        }

        if (plan.Remux)
        {
            a.Add("-c:v"); a.Add("copy");
            if (!removeAudio) { a.Add("-c:a"); a.Add("copy"); }
            plan.Notes.Add("Remuxage : les flux sont copiés sans réencodage, aucune perte de qualité.");
            if (trimmed) plan.Warnings.Add("Découpe sans réencodage : le début est calé sur l'image-clé la plus proche (précision de quelques secondes).");
        }
        else
        {
            var venc = PickEncoder(spec.VideoEncoders, encoders, req.Opt("vcodec"))
                       ?? throw new ConversionException(ConversionErrorKind.MissingCodec, $"Aucun encodeur vidéo compatible avec {spec.Id.ToUpperInvariant()} dans cette version de FFmpeg.");
            a.Add("-c:v"); a.Add(venc);
            VideoQuality(a, venc, profile, req.OptInt("vbitrate", 0), req.OptInt("crf", -1));
            var vf = VideoFilters(req, profile);
            if (vf.Count > 0) { a.Add("-vf"); a.Add(string.Join(",", vf)); }
            if (req.Opt("fps") is var fps && fps != "orig") { a.Add("-r"); a.Add(fps); }
            if (venc is "libx264" or "mpeg4" or "libxvid" or "h264_mf" or "libvpx-vp9" or "libvpx" or "mpeg2video") { a.Add("-pix_fmt"); a.Add("yuv420p"); }
            if (profile == "mobile" && venc == "libx264") { a.AddRange(["-profile:v", "main", "-level", "4.0"]); }

            if (!removeAudio && info.HasAudio)
            {
                bool audioCopyOk = info.Audio.All(s => spec.CopyAudio.Contains(s.Codec) || spec.CopyAudio.Contains("*"));
                if (audioCopyOk && req.Opt("acodec") == "auto" && req.OptInt("abitrate", 0) == 0 && !trimmed)
                {
                    a.Add("-c:a"); a.Add("copy");
                    plan.Notes.Add("Audio copié sans réencodage.");
                }
                else
                {
                    var aenc = PickEncoder(spec.AudioEncoders, encoders, req.Opt("acodec"))
                               ?? throw new ConversionException(ConversionErrorKind.MissingCodec, $"Aucun encodeur audio compatible avec {spec.Id.ToUpperInvariant()}.");
                    a.Add("-c:a"); a.Add(aenc);
                    AudioQuality(a, aenc, profile switch { "max" => "max", "small" => "small", _ => "music" }, req.OptInt("abitrate", 0));
                    if (aenc == "mp2" || spec.Id == "mpeg") { a.AddRange(["-ar", "48000"]); }
                }
            }
            plan.Notes.Add($"Réencodage vidéo ({venc}) : compression avec perte, la qualité dépend du profil choisi.");
        }

        if (keepSubs && subs.Count > 0)
        {
            if (spec.Subtitles == SubtitleMode.MovText) { a.Add("-c:s"); a.Add("mov_text"); }
            else if (spec.Subtitles == SubtitleMode.WebVtt) { a.Add("-c:s"); a.Add("webvtt"); }
            else if (spec.Subtitles == SubtitleMode.Copy) { a.Add("-c:s"); a.Add("copy"); }
        }
        if (removeAudio) a.Add("-an");
        if (!keepSubs || spec.Subtitles == SubtitleMode.Drop) a.Add("-sn");
        Metadata(a, req);
        a.AddRange(spec.ExtraArgs);
        Output(a, spec.Muxer, req.TempOutput);
        plan.ExpectedDuration = dur ?? (info.Duration is { } t ? t - start : null);
        return plan;
    }

    public static bool RemuxCompatible(MediaInfo info, ContainerSpec spec, bool removeAudio, bool firstAudioOnly)
    {
        var video = info.Video.Take(1).ToList();
        if (video.Count == 0) return false;
        bool any = spec.CopyVideo.Contains("*");
        if (!any && !video.All(v => spec.CopyVideo.Contains(v.Codec))) return false;
        if (!removeAudio)
        {
            var audio = firstAudioOnly ? info.Audio.Take(1) : info.Audio;
            if (!spec.CopyAudio.Contains("*") && !audio.All(s => spec.CopyAudio.Contains(s.Codec))) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ video -> audio / audio -> audio

    public static FfmpegPlan ToAudio(ConversionRequest req, MediaInfo info, AudioSpec spec, ISet<string> encoders, bool fromVideo)
    {
        var plan = new FfmpegPlan();
        var a = plan.Args;
        if (!info.HasAudio)
            throw new ConversionException(ConversionErrorKind.InvalidInput, "Ce fichier ne contient aucune piste audio.");
        var (start, dur) = Trim(req, info, plan);
        string profile = req.Opt("profile");
        bool normalize = req.OptBool("normalize");
        string channels = req.Opt("channels");
        string rate = req.Opt("samplerate");
        bool keepCover = req.OptBool("cover") && spec.SupportsCover && info.Cover is not null;

        Common(a);
        if (start > 0) { a.Add("-ss"); a.Add(start.ToString("0.###", Inv)); }
        a.Add("-i"); a.Add(req.Input);
        if (dur is { } d) { a.Add("-t"); a.Add(d.ToString("0.###", Inv)); }
        a.Add("-map"); a.Add(fromVideo && req.Opt("audio") == "first" ? "0:a:0" : "0:a:0");
        if (keepCover) { a.Add("-map"); a.Add("0:" + info.Cover!.Index); }

        var src = info.Audio.First();
        bool copyable = spec.CopyCodecs.Contains(src.Codec) && !normalize && channels == "orig" && rate == "orig"
                        && req.OptInt("abitrate", 0) == 0 && req.Opt("copy") != "false" && profile != "lossless";
        if (copyable && req.OptBool("copy"))
        {
            a.Add("-c:a"); a.Add("copy");
            plan.Remux = true;
            plan.Notes.Add("Piste audio copiée sans réencodage : aucune perte.");
        }
        else
        {
            string enc;
            if (profile == "lossless" && spec.LosslessEncoder is not null && encoders.Contains(spec.LosslessEncoder)) enc = spec.LosslessEncoder;
            else enc = PickEncoder(spec.Encoders, encoders, "auto")
                       ?? throw new ConversionException(ConversionErrorKind.MissingCodec, $"Cette version de FFmpeg ne contient pas d'encodeur {spec.Id.ToUpperInvariant()}.");
            a.Add("-c:a"); a.Add(enc);
            if (enc is "opus" or "vorbis") { a.Add("-strict"); a.Add("-2"); }
            if (spec.Id is "wav" && req.Opt("bits") == "24") { a[^1] = "pcm_s24le"; }
            if (spec.Id is "aiff" && req.Opt("bits") == "24") { a[^1] = "pcm_s24be"; }
            AudioQuality(a, enc, profile, req.OptInt("abitrate", 0));
            if (profile == "lossless" && !spec.Lossless && spec.LosslessEncoder is null)
                plan.Warnings.Add($"{spec.Id.ToUpperInvariant()} est un format avec perte : le profil « sans perte » n'est pas applicable, la qualité maximale est utilisée.");
            if (spec.Id == "amr") { a.AddRange(["-ar", "8000", "-ac", "1"]); }
            else
            {
                if (rate != "orig") { a.Add("-ar"); a.Add(rate); }
                else if (spec.Id == "opus" && src.SampleRate is not (48000 or 24000 or 16000 or 12000 or 8000)) { a.AddRange(["-ar", "48000"]); }
                if (channels == "mono" || (profile == "voice" && channels == "orig")) { a.AddRange(["-ac", "1"]); }
                else if (channels == "stereo") { a.AddRange(["-ac", "2"]); }
            }
            if (normalize)
            {
                a.Add("-af"); a.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
                plan.Notes.Add("Normalisation du volume (EBU R128, une passe).");
            }
            var srcLossy = src.Codec is "mp3" or "aac" or "vorbis" or "opus" or "wmav2" or "wmav1" or "amr_nb" or "ac3" or "eac3";
            if (spec.Lossless || enc == "alac")
                plan.Notes.Add(srcLossy
                    ? "Source compressée avec perte : le format sans perte conserve fidèlement l'audio actuel mais ne restaure pas la qualité d'origine."
                    : "Conversion sans perte.");
            else plan.Notes.Add($"Encodage {enc} : compression avec perte.");
        }
        if (keepCover)
        {
            a.AddRange(["-c:v", "copy", "-disposition:v:0", "attached_pic"]);
            if (spec.Id == "mp3") a.AddRange(["-id3v2_version", "3"]);
        }
        else
        {
            a.Add("-vn");
            if (info.Cover is not null && req.OptBool("cover") && !spec.SupportsCover)
                plan.Warnings.Add($"La pochette ne peut pas être conservée en {spec.Id.ToUpperInvariant()}.");
        }
        a.Add("-sn");
        Metadata(a, req);
        Output(a, spec.Muxer, req.TempOutput);
        plan.ExpectedDuration = dur ?? (info.Duration is { } t ? t - start : null);
        return plan;
    }

    // ------------------------------------------------------------------ fusion audio

    public static FfmpegPlan MergeAudio(ConversionRequest req, IReadOnlyList<MediaInfo> infos, AudioSpec spec, ISet<string> encoders)
    {
        var plan = new FfmpegPlan();
        var a = plan.Args;
        Common(a);
        foreach (var i in req.Inputs) { a.Add("-i"); a.Add(i); }
        var parts = new List<string>();
        for (int i = 0; i < req.Inputs.Count; i++)
        {
            if (!infos[i].HasAudio) throw new ConversionException(ConversionErrorKind.InvalidInput, $"« {Path.GetFileName(req.Inputs[i])} » ne contient pas d'audio.");
            parts.Add($"[{i}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a{i}]");
        }
        var concat = string.Concat(Enumerable.Range(0, req.Inputs.Count).Select(i => $"[a{i}]")) + $"concat=n={req.Inputs.Count}:v=0:a=1[out]";
        a.Add("-filter_complex"); a.Add(string.Join(";", parts) + ";" + concat);
        a.Add("-map"); a.Add("[out]");
        var enc = PickEncoder(spec.Encoders, encoders, "auto") ?? throw new ConversionException(ConversionErrorKind.MissingCodec, "Encodeur audio absent.");
        a.Add("-c:a"); a.Add(enc);
        if (enc is "opus" or "vorbis") { a.Add("-strict"); a.Add("-2"); }
        AudioQuality(a, enc, req.Opt("profile"), req.OptInt("abitrate", 0));
        Metadata(a, req);
        Output(a, spec.Muxer, req.TempOutput);
        plan.ExpectedDuration = infos.Sum(i => i.Duration ?? 0);
        plan.Notes.Add($"Fusion de {req.Inputs.Count} fichiers dans l'ordre de la liste (réencodage {enc}).");
        return plan;
    }

    // ------------------------------------------------------------------ GIF, miniatures, sequences

    public static FfmpegPlan VideoToGif(ConversionRequest req, MediaInfo info)
    {
        var plan = new FfmpegPlan();
        var a = plan.Args;
        var (start, dur) = Trim(req, info, plan);
        dur ??= Math.Min(info.Duration ?? 10, 30);
        if ((info.Duration ?? 0) - start > 30 && req.Opt("end").Length == 0)
            plan.Warnings.Add("GIF limité aux 30 premières secondes (réglez début/fin pour choisir un autre passage).");
        int fps = req.OptInt("gif_fps", 12);
        string width = req.Opt("gif_width");
        Common(a);
        if (start > 0) { a.Add("-ss"); a.Add(start.ToString("0.###", Inv)); }
        a.Add("-i"); a.Add(req.Input);
        a.Add("-t"); a.Add(dur.Value.ToString("0.###", Inv));
        var scale = width == "orig" ? "" : $",scale={width}:-1:flags=lanczos";
        a.Add("-vf"); a.Add($"fps={fps}{scale},split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5");
        a.AddRange(["-loop", req.OptBool("loop") ? "0" : "-1", "-an"]);
        Output(a, "gif", req.TempOutput);
        plan.ExpectedDuration = dur;
        plan.Notes.Add("GIF : 256 couleurs par image, sans son. Le fichier peut être volumineux.");
        return plan;
    }

    public static FfmpegPlan Thumbnail(ConversionRequest req, MediaInfo info, string target)
    {
        var plan = new FfmpegPlan();
        var a = plan.Args;
        double at = TimeParser.Parse(req.Opt("at")) is { } t ? t.TotalSeconds : Math.Min(Math.Max((info.Duration ?? 0) * 0.1, 0), 30);
        if (info.Duration is { } d && at >= d) at = Math.Max(0, d - 0.5);
        Common(a);
        a.Add("-ss"); a.Add(at.ToString("0.###", Inv));
        a.Add("-i"); a.Add(req.Input);
        a.AddRange(["-frames:v", "1", "-an", "-sn"]);
        if (req.Opt("thumb_width") is var w && w != "orig") { a.Add("-vf"); a.Add($"scale={w}:-2"); }
        if (target == "jpg") { a.AddRange(["-q:v", "2"]); }
        Output(a, "image2", req.TempOutput);
        a.Insert(a.Count - 1, "-update"); a.Insert(a.Count - 1, "1");
        plan.Notes.Add($"Image extraite à {at:0.#} s.");
        return plan;
    }

    public static FfmpegPlan ImageSequence(ConversionRequest req, MediaInfo info, string target)
    {
        var plan = new FfmpegPlan();
        var a = plan.Args;
        var (start, dur) = Trim(req, info, plan);
        string rate = req.Opt("seq_fps");
        Common(a);
        if (start > 0) { a.Add("-ss"); a.Add(start.ToString("0.###", Inv)); }
        a.Add("-i"); a.Add(req.Input);
        if (dur is { } d) { a.Add("-t"); a.Add(d.ToString("0.###", Inv)); }
        var filters = new List<string>();
        if (rate != "all") filters.Add("fps=" + rate);
        if (req.Opt("thumb_width") is var w && w != "orig") filters.Add($"scale={w}:-2");
        if (filters.Count > 0) { a.Add("-vf"); a.Add(string.Join(",", filters)); }
        a.AddRange(["-an", "-sn"]);
        if (target == "jpg") a.AddRange(["-q:v", "2"]);
        var stem = Path.GetFileNameWithoutExtension(req.FinalOutput.Length > 0 ? req.FinalOutput : req.Input);
        var safeStem = FileWheel.Core.FileOperations.PathSafety.SanitizeFileName(stem.Replace("%", "pct"));
        Output(a, "image2", Path.Combine(req.TempOutput, $"{safeStem}_%05d.{target}"));
        plan.ExpectedDuration = dur ?? (info.Duration is { } t ? t - start : null);
        if (rate == "all") plan.Warnings.Add("Toutes les images de la vidéo seront extraites : cela peut représenter des milliers de fichiers.");
        return plan;
    }

    // ------------------------------------------------------------------ utilitaires

    private static void Common(List<string> a) =>
        a.AddRange(["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-progress", "pipe:1", "-nostats"]);

    private static void Output(List<string> a, string muxer, string path)
    {
        a.Add("-f"); a.Add(muxer);
        a.Add(path);
    }

    private static void Metadata(List<string> a, ConversionRequest req)
    {
        if (req.Opt("metadata") == "false") { a.AddRange(["-map_metadata", "-1", "-map_chapters", "-1"]); }
        else { a.AddRange(["-map_metadata", "0"]); }
    }

    private static (double start, double? dur) Trim(ConversionRequest req, MediaInfo info, FfmpegPlan plan)
    {
        double start = TimeParser.Parse(req.Opt("start"))?.TotalSeconds ?? 0;
        double? end = TimeParser.Parse(req.Opt("end"))?.TotalSeconds;
        if (info.Duration is { } total)
        {
            if (start >= total) throw new ConversionException(ConversionErrorKind.InvalidOptions, $"Le début ({start:0.#} s) dépasse la durée du fichier ({total:0.#} s).");
            if (end is { } e && e > total) end = null;
        }
        if (end is { } en && en <= start) throw new ConversionException(ConversionErrorKind.InvalidOptions, "La fin doit être après le début.");
        double? dur = end is { } x ? x - start : null;
        if (start > 0 || dur is not null) plan.Notes.Add($"Découpe : de {start:0.##} s{(end is { } y ? $" à {y:0.##} s" : " jusqu'à la fin")}.");
        return (start, dur);
    }

    private static string? PickEncoder(string[] preferred, ISet<string> available, string requested)
    {
        if (requested is { Length: > 0 } && requested != "auto")
            return preferred.Contains(requested) && available.Contains(requested) ? requested : null;
        return preferred.FirstOrDefault(available.Contains);
    }

    private static List<string> VideoFilters(ConversionRequest req, string profile)
    {
        var vf = new List<string>();
        var res = req.Opt("resolution");
        if (res == "orig" && profile == "mobile") res = "1080";
        if (res != "orig" && int.TryParse(res, out var h))
            vf.Add($"scale=-2:'trunc(min({h},ih)/2)*2'");
        switch (req.Opt("rotate"))
        {
            case "90": vf.Add("transpose=1"); break;
            case "180": vf.Add("transpose=1,transpose=1"); break;
            case "270": vf.Add("transpose=2"); break;
        }
        return vf;
    }

    private static void VideoQuality(List<string> a, string enc, string profile, int bitrateKbps, int crfOverride)
    {
        if (bitrateKbps > 0)
        {
            a.Add("-b:v"); a.Add(bitrateKbps + "k");
            if (enc is "libvpx-vp9" or "libvpx" or "libaom-av1") { a.AddRange(["-deadline", "good", "-cpu-used", "2"]); }
            else if (enc == "libx264") { a.AddRange(["-preset", "medium", "-maxrate", (bitrateKbps * 3 / 2) + "k", "-bufsize", (bitrateKbps * 2) + "k"]); }
            return;
        }
        switch (enc)
        {
            case "libx264":
            {
                int crf = crfOverride >= 0 ? crfOverride : profile switch { "max" => 17, "small" => 27, "mobile" => 23, "web" => 23, _ => 21 };
                var preset = profile switch { "max" => "slow", "small" => "medium", _ => "medium" };
                a.AddRange(["-crf", crf.ToString(), "-preset", preset]);
                break;
            }
            case "libvpx-vp9":
            {
                int crf = crfOverride >= 0 ? crfOverride : profile switch { "max" => 24, "small" => 38, _ => 31 };
                a.AddRange(["-crf", crf.ToString(), "-b:v", "0", "-deadline", "good", "-cpu-used", profile == "max" ? "1" : "3", "-row-mt", "1"]);
                break;
            }
            case "libvpx":
                a.AddRange(["-crf", profile == "max" ? "6" : profile == "small" ? "20" : "10", "-b:v", profile == "small" ? "800k" : "2M"]);
                break;
            case "libaom-av1":
                a.AddRange(["-crf", profile == "max" ? "24" : profile == "small" ? "38" : "30", "-b:v", "0", "-cpu-used", "6", "-row-mt", "1"]);
                break;
            case "h264_mf":
                a.AddRange(["-b:v", profile == "max" ? "12M" : profile == "small" ? "2M" : "5M"]);
                break;
            default: // mpeg4, libxvid, wmv2, mpeg2video, flv
                a.AddRange(["-q:v", profile == "max" ? "2" : profile == "small" ? "8" : "4"]);
                break;
        }
    }

    private static void AudioQuality(List<string> a, string enc, string profile, int bitrateKbps)
    {
        if (enc is "flac") { a.AddRange(["-compression_level", profile == "max" || profile == "lossless" ? "8" : "5"]); return; }
        if (enc.StartsWith("pcm_") || enc == "alac") return;
        if (bitrateKbps > 0) { a.Add("-b:a"); a.Add(bitrateKbps + "k"); return; }
        switch (enc)
        {
            case "libmp3lame":
                if (profile is "max" or "lossless") a.AddRange(["-q:a", "0"]);
                else a.AddRange(["-b:a", profile switch { "voice" => "64k", "small" => "128k", _ => "192k" }]);
                break;
            case "aac":
                a.AddRange(["-b:a", profile switch { "max" or "lossless" => "320k", "voice" => "64k", "small" => "96k", _ => "192k" }]);
                break;
            case "libopus":
            case "opus":
                a.AddRange(["-b:a", profile switch { "max" or "lossless" => "256k", "voice" => "32k", "small" => "64k", _ => "128k" }]);
                if (profile == "voice" && enc == "libopus") a.AddRange(["-application", "voip"]);
                break;
            case "libvorbis":
            case "vorbis":
                a.AddRange(["-q:a", profile switch { "max" or "lossless" => "8", "voice" => "1", "small" => "2", _ => "5" }]);
                break;
            case "wmav2":
                a.AddRange(["-b:a", profile switch { "max" or "lossless" => "320k", "voice" => "64k", "small" => "96k", _ => "192k" }]);
                break;
            case "mp2":
                a.AddRange(["-b:a", "224k"]);
                break;
            case "ac3":
                a.AddRange(["-b:a", "384k"]);
                break;
            case "libopencore_amrnb":
                a.AddRange(["-b:a", "12.2k"]);
                break;
        }
    }
}
