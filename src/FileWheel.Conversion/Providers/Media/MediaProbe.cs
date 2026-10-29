using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Media;

public sealed record MediaStream(int Index, string Type, string Codec, int Width, int Height, double? Fps, int Channels,
    int SampleRate, string? Language, bool AttachedPicture, int Rotation);

public sealed record MediaInfo(double? Duration, string? Container, IReadOnlyList<MediaStream> Streams, long? BitRate)
{
    public IEnumerable<MediaStream> Video => Streams.Where(s => s.Type == "video" && !s.AttachedPicture);
    public IEnumerable<MediaStream> Audio => Streams.Where(s => s.Type == "audio");
    public IEnumerable<MediaStream> Subtitles => Streams.Where(s => s.Type == "subtitle");
    public MediaStream? Cover => Streams.FirstOrDefault(s => s.AttachedPicture);
    public bool HasVideo => Video.Any();
    public bool HasAudio => Audio.Any();
}

/// <summary>Analyse d'un fichier media par ffprobe (ou, a defaut, par la sortie de « ffmpeg -i »).</summary>
public static class MediaProbe
{
    public static async Task<MediaInfo> ProbeAsync(string? ffprobe, string ffmpeg, string input, CancellationToken ct)
    {
        if (ffprobe is not null)
        {
            var r = await ProcessRunner.RunAsync(ffprobe, ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", input], ct, tailChars: 400_000).ConfigureAwait(false);
            if (r.ExitCode == 0) return ParseJson(r.StdOutTail);
            if (r.StdErrTail.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(ConversionErrorKind.Corrupt, "Fichier audio/vidéo endommagé ou format non pris en charge par FFmpeg.", r.StdErrTail);
        }
        // Repli : « ffmpeg -i » ecrit les informations sur stderr puis echoue (pas de sortie demandee).
        var r2 = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", input], ct).ConfigureAwait(false);
        if (r2.StdErrTail.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
            throw new ConversionException(ConversionErrorKind.Corrupt, "Fichier audio/vidéo endommagé ou illisible.", r2.StdErrTail);
        return ParseFfmpegBanner(r2.StdErrTail);
    }

    public static MediaInfo ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        double? duration = null;
        string? container = null;
        long? bitrate = null;
        if (root.TryGetProperty("format", out var fmt))
        {
            if (fmt.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)) duration = dv;
            if (fmt.TryGetProperty("format_name", out var fn)) container = fn.GetString();
            if (fmt.TryGetProperty("bit_rate", out var br) && long.TryParse(br.GetString(), out var bv)) bitrate = bv;
        }
        var streams = new List<MediaStream>();
        if (root.TryGetProperty("streams", out var arr))
        {
            foreach (var s in arr.EnumerateArray())
            {
                string type = Str(s, "codec_type") ?? "";
                string codec = Str(s, "codec_name") ?? "";
                int w = Int(s, "width"), h = Int(s, "height");
                double? fps = null;
                var rate = Str(s, "avg_frame_rate") ?? Str(s, "r_frame_rate");
                if (rate is not null && rate.Contains('/'))
                {
                    var p = rate.Split('/');
                    if (double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                        double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b > 0 && a > 0) fps = a / b;
                }
                bool attached = s.TryGetProperty("disposition", out var disp) && disp.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                string? lang = s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var l) ? l.GetString() : null;
                int rotation = 0;
                if (s.TryGetProperty("tags", out var t2) && t2.TryGetProperty("rotate", out var rot) && int.TryParse(rot.GetString(), out var rv)) rotation = rv;
                if (s.TryGetProperty("side_data_list", out var sdl))
                    foreach (var sd in sdl.EnumerateArray())
                        if (sd.TryGetProperty("rotation", out var rr) && rr.ValueKind == JsonValueKind.Number) rotation = (int)rr.GetDouble();
                int sr = int.TryParse(Str(s, "sample_rate"), out var srv) ? srv : 0;
                streams.Add(new MediaStream(Int(s, "index"), type, codec, w, h, fps, Int(s, "channels"), sr, lang, attached, rotation));
            }
        }
        return new MediaInfo(duration, container, streams, bitrate);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    /// <summary>Lecture minimale de la banniere de « ffmpeg -i » (repli sans ffprobe).</summary>
    public static MediaInfo ParseFfmpegBanner(string text)
    {
        double? duration = null;
        var dm = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (dm.Success)
            duration = int.Parse(dm.Groups[1].Value) * 3600 + int.Parse(dm.Groups[2].Value) * 60 + double.Parse(dm.Groups[3].Value, CultureInfo.InvariantCulture);
        var streams = new List<MediaStream>();
        int idx = 0;
        foreach (Match m in Regex.Matches(text, @"Stream #\d+:(\d+)(?:\[[^\]]*\])?(?:\((\w+)\))?: (Video|Audio|Subtitle): (\w+)([^\n]*)"))
        {
            var rest = m.Groups[5].Value;
            int w = 0, h = 0, ch = 0, sr = 0;
            var res = Regex.Match(rest, @"(\d{2,5})x(\d{2,5})");
            if (res.Success) { w = int.Parse(res.Groups[1].Value); h = int.Parse(res.Groups[2].Value); }
            var srm = Regex.Match(rest, @"(\d+) Hz");
            if (srm.Success) sr = int.Parse(srm.Groups[1].Value);
            if (rest.Contains("stereo")) ch = 2; else if (rest.Contains("mono")) ch = 1; else if (rest.Contains("5.1")) ch = 6;
            double? fps = null;
            var fm = Regex.Match(rest, @"([\d.]+) fps");
            if (fm.Success) fps = double.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture);
            streams.Add(new MediaStream(int.TryParse(m.Groups[1].Value, out var i) ? i : idx, m.Groups[3].Value.ToLowerInvariant(),
                m.Groups[4].Value, w, h, fps, ch, sr, m.Groups[2].Success ? m.Groups[2].Value : null, rest.Contains("attached pic"), 0));
            idx++;
        }
        return new MediaInfo(duration, null, streams, null);
    }
}
