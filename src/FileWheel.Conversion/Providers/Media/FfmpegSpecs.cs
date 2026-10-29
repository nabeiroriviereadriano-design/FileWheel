namespace FileWheel.Conversion.Providers.Media;

public enum SubtitleMode { Copy, MovText, WebVtt, Drop }

/// <summary>Ce qu'un conteneur video accepte et comment l'alimenter.</summary>
public sealed record ContainerSpec(
    string Id,
    string Muxer,
    string[] VideoEncoders,
    string[] AudioEncoders,
    string[] CopyVideo,
    string[] CopyAudio,
    SubtitleMode Subtitles,
    string[] ExtraArgs);

/// <summary>Ce qu'un format audio demande.</summary>
public sealed record AudioSpec(string Id, string Muxer, string[] Encoders, string[] CopyCodecs, bool SupportsCover, bool Lossless, string? LosslessEncoder = null);

public static class FfmpegSpecs
{
    public static readonly string[] AnyCodec = ["*"];
    public static readonly string[] TextSubtitleCodecs = ["subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text"];

    public static readonly IReadOnlyDictionary<string, ContainerSpec> Containers = new Dictionary<string, ContainerSpec>
    {
        ["mp4"] = new("mp4", "mp4", ["libx264", "h264_mf", "mpeg4"], ["aac"], ["h264", "hevc", "av1", "mpeg4"], ["aac", "mp3", "ac3", "eac3", "alac"], SubtitleMode.MovText, ["-movflags", "+faststart"]),
        ["m4v"] = new("m4v", "mp4", ["libx264", "h264_mf", "mpeg4"], ["aac"], ["h264", "hevc", "mpeg4"], ["aac", "ac3", "alac"], SubtitleMode.MovText, ["-movflags", "+faststart"]),
        ["mkv"] = new("mkv", "matroska", ["libx264", "mpeg4"], ["libopus", "aac"], AnyCodec, AnyCodec, SubtitleMode.Copy, []),
        ["webm"] = new("webm", "webm", ["libvpx-vp9", "libvpx", "libaom-av1"], ["libopus", "libvorbis"], ["vp8", "vp9", "av1"], ["opus", "vorbis"], SubtitleMode.WebVtt, []),
        ["mov"] = new("mov", "mov", ["libx264", "mpeg4"], ["aac"], ["h264", "hevc", "prores", "mpeg4", "mjpeg"], ["aac", "alac", "mp3", "pcm_s16le", "pcm_s24le"], SubtitleMode.MovText, ["-movflags", "+faststart"]),
        ["avi"] = new("avi", "avi", ["mpeg4", "libxvid"], ["libmp3lame", "ac3"], ["mpeg4", "msmpeg4v3", "mjpeg", "h264"], ["mp3", "ac3", "pcm_s16le"], SubtitleMode.Drop, []),
        ["wmv"] = new("wmv", "asf", ["wmv2"], ["wmav2"], ["wmv1", "wmv2", "wmv3", "vc1"], ["wmav1", "wmav2", "wmapro"], SubtitleMode.Drop, []),
        ["mpeg"] = new("mpeg", "vob", ["mpeg2video"], ["mp2", "ac3"], ["mpeg1video", "mpeg2video"], ["mp2", "mp3", "ac3"], SubtitleMode.Drop, []),
        ["ts"] = new("ts", "mpegts", ["libx264", "mpeg2video"], ["aac", "mp2"], ["h264", "hevc", "mpeg2video"], ["aac", "mp3", "ac3", "mp2"], SubtitleMode.Drop, []),
        ["flv"] = new("flv", "flv", ["libx264", "flv"], ["aac", "libmp3lame"], ["h264", "flv1"], ["aac", "mp3"], SubtitleMode.Drop, []),
    };

    public static readonly IReadOnlyDictionary<string, AudioSpec> AudioFormats = new Dictionary<string, AudioSpec>
    {
        ["mp3"] = new("mp3", "mp3", ["libmp3lame"], ["mp3"], true, false),
        ["m4a"] = new("m4a", "ipod", ["aac"], ["aac", "alac"], true, false, "alac"),
        ["aac"] = new("aac", "adts", ["aac"], ["aac"], false, false),
        ["ogg"] = new("ogg", "ogg", ["libvorbis", "vorbis"], ["vorbis"], false, false),
        ["opus"] = new("opus", "opus", ["libopus", "opus"], ["opus"], false, false),
        ["flac"] = new("flac", "flac", ["flac"], ["flac"], true, true),
        ["wav"] = new("wav", "wav", ["pcm_s16le"], ["pcm_s16le", "pcm_s24le", "pcm_f32le"], false, true),
        ["aiff"] = new("aiff", "aiff", ["pcm_s16be"], ["pcm_s16be", "pcm_s24be"], false, true),
        ["wma"] = new("wma", "asf", ["wmav2"], ["wmav2", "wmav1"], false, false),
        ["amr"] = new("amr", "amr", ["libopencore_amrnb"], ["amr_nb"], false, false),
    };

    public static readonly string[] VideoSources = ["mp4", "m4v", "mkv", "webm", "mov", "avi", "wmv", "mpeg", "ts", "flv", "3gp", "gif"];
    public static readonly string[] AudioSources = ["mp3", "wav", "flac", "aac", "m4a", "ogg", "opus", "wma", "aiff", "amr"];

    /// <summary>Encodeurs presents dans une version « essentials » typique (utilise tant que FFmpeg n'est pas installe).</summary>
    public static readonly string[] AssumedEncoders =
    [
        "libx264", "mpeg4", "libvpx-vp9", "libvpx", "aac", "libmp3lame", "libopus", "libvorbis", "flac", "pcm_s16le", "pcm_s16be",
        "wmav2", "wmv2", "mpeg2video", "mp2", "ac3", "gif", "png", "mjpeg", "alac", "mov_text", "webvtt", "flv",
    ];
}
