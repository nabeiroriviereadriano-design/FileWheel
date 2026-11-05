using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Logging;
using FileWheel.Core.Util;

namespace FileWheel.Conversion.Updates;

/// <summary>Version publiee dans les Releases GitHub du depot officiel.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string Notes,
    string? SetupUrl, long SetupSize, string? SetupSha256, string? PortableUrl);

/// <summary>
/// Mises a jour de FileWheel. La verification lit uniquement la derniere Release du depot officiel
/// (api.github.com, sans compte, aucune donnee personnelle envoyee). L'installation telecharge
/// FileWheel-Setup-&lt;version&gt;.exe depuis github.com, verifie son empreinte SHA-256 (publiee par GitHub,
/// ou fichier SHA256SUMS.txt de la Release) puis lance l'installateur en mode silencieux :
/// il ferme FileWheel, remplace les fichiers et le relance (reglages conserves).
/// </summary>
public static class Updater
{
    public const string Repository = "nabeiroriviereadriano-design/FileWheel";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    private static readonly HttpClient Http = CreateHttp();

    public static Version Current => typeof(Updater).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    /// <summary>Derniere version publiee, ou null si elle n'est pas plus recente que celle-ci.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aucune Release publiee
        if (!resp.IsSuccessStatusCode)
            throw new ConversionException(ConversionErrorKind.Unknown, $"GitHub a répondu {(int)resp.StatusCode}. Réessayez plus tard.");
        var info = Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (info is null || info.Version <= Current) return null;
        return info;
    }

    /// <summary>Lecture de la reponse de l'API GitHub (public pour les tests).</summary>
    public static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var m = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        var version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        string? setupUrl = null, setupSha = null, portable = null;
        long setupSize = 0;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString();
            if (Regex.IsMatch(name, @"^FileWheel-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase))
            {
                setupUrl = url;
                setupSize = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    setupSha = digest[7..].ToLowerInvariant();
            }
            else if (Regex.IsMatch(name, @"portable.*\.zip$", RegexOptions.IgnoreCase)) portable = url;
        }
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new UpdateInfo(version, tag, page, notes, setupUrl, setupSize, setupSha, portable);
    }

    /// <summary>Empreinte de l'installateur lue dans SHA256SUMS.txt (si GitHub ne la fournit pas).</summary>
    private static async Task<string?> ShaFromSumsAsync(UpdateInfo info, CancellationToken ct)
    {
        if (info.SetupUrl is null) return null;
        var sumsUrl = info.SetupUrl[..(info.SetupUrl.LastIndexOf('/') + 1)] + "SHA256SUMS.txt";
        try
        {
            var text = await Http.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
            var file = Path.GetFileName(new Uri(info.SetupUrl).AbsolutePath);
            return ParseSums(text, file);
        }
        catch (Exception ex) { Log.Warn("SHA256SUMS.txt illisible : " + ex.Message); return null; }
    }

    public static string? ParseSums(string text, string file)
    {
        foreach (var line in text.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && m.Groups[2].Value.Trim().Equals(file, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Telecharge et verifie l'installateur ; renvoie son chemin local.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        if (info.SetupUrl is null) throw new ConversionException(ConversionErrorKind.NotSupported, "Cette version ne contient pas d'installateur Windows.");
        if (!info.SetupUrl.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new ConversionException(ConversionErrorKind.Unsafe, "Adresse de téléchargement inattendue : mise à jour refusée.");
        var sha = info.SetupSha256 ?? await ShaFromSumsAsync(info, ct).ConfigureAwait(false)
            ?? throw new ConversionException(ConversionErrorKind.Unsafe, "Empreinte SHA-256 de l'installateur introuvable : intégrité non vérifiable, mise à jour refusée. Téléchargez-la depuis la page des versions.");
        var target = Path.Combine(AppPaths.DownloadsDir, Path.GetFileName(new Uri(info.SetupUrl).AbsolutePath));
        await DependencyManager.DownloadVerifiedAsync(info.SetupUrl, target, sha, info.SetupSize, progress, ct).ConfigureAwait(false);
        Log.Info($"Mise à jour {info.Version} téléchargée et vérifiée (sha256 {sha[..12]}…)");
        return target;
    }

    /// <summary>Lance l'installateur en silencieux : il ferme FileWheel, met a jour, puis le relance.</summary>
    public static void RunInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = false, ArgumentList = { "/S", "/RELANCER" } });
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileWheel", Current.ToString()));
        return c;
    }
}
