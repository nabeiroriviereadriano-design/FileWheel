using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;
using FileWheel.Core.Logging;
using FileWheel.Core.Util;

namespace FileWheel.Conversion.Engines;

public enum EngineSource { None, UserPath, FileWheel, Installed, SystemPath }

public sealed record EngineStatus(string Id, string? Path, EngineSource Source)
{
    public bool Available => Path is not null;
}

/// <summary>Chemins choisis par l'utilisateur et installations faites par FileWheel.</summary>
public sealed class EngineConfig
{
    public Dictionary<string, string> UserPaths { get; set; } = new();
    public Dictionary<string, InstalledEngine> Installed { get; set; } = new();
}

public sealed class InstalledEngine
{
    public string Version { get; set; } = "";
    public string Folder { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTime InstalledAt { get; set; }
}

/// <summary>Informations montrees a l'utilisateur AVANT tout telechargement (consentement).</summary>
public sealed record InstallOffer(EngineDefinition Engine, string Version, string AssetName, string Url, long Size, string Sha256, string? HelperUrl, string? HelperSha256);

/// <summary>
/// Disponibilite des outils externes et installation a la demande.
///
/// Rien n'est telecharge sans demande explicite de l'utilisateur. Une installation GitHub :
/// 1. interroge l'API GitHub (TLS) pour la derniere version du depot officiel declare ;
/// 2. exige l'empreinte SHA-256 publiee par GitHub pour l'asset (sinon refus) ;
/// 3. telecharge, verifie l'empreinte, extrait dans %LOCALAPPDATA%\FileWheel\engines\&lt;id&gt; ;
/// 4. n'execute aucun installeur : ce sont des versions portables.
/// Les moteurs installes ainsi sont supprimes par le desinstalleur.
/// </summary>
public sealed class DependencyManager
{
    private readonly object _gate = new();
    private EngineConfig _config;
    private readonly Dictionary<string, EngineStatus> _cache = new();
    private static readonly HttpClient Http = CreateHttp();

    public DependencyManager()
    {
        _config = JsonStore.Load(AppPaths.EnginesFile, () => new EngineConfig());
    }

    public event Action? Changed;

    /// <summary>Repertoires supplementaires consultes en priorite (tests).</summary>
    public List<string> ExtraSearchDirs { get; } = new();

    public EngineStatus Status(string id)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(id, out var s)) return s;
            s = Detect(id);
            _cache[id] = s;
            return s;
        }
    }

    public string? PathOf(string id) => Status(id).Path;

    public bool IsAvailable(string id) => Status(id).Available;

    public void Refresh()
    {
        lock (_gate) _cache.Clear();
        Changed?.Invoke();
    }

    public string Require(string id)
    {
        var p = PathOf(id);
        if (p is null)
        {
            var def = EngineCatalog.Get(id);
            throw new ConversionException(ConversionErrorKind.EngineMissing,
                $"Le moteur {def.Name} n'est pas installé. Paramètres › Moteurs pour l'installer ou indiquer son emplacement.");
        }
        return p;
    }

    public void SetUserPath(string id, string? path)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(path)) _config.UserPaths.Remove(id);
            else _config.UserPaths[id] = Path.GetFullPath(path);
            JsonStore.Save(AppPaths.EnginesFile, _config);
        }
        Refresh();
    }

    public InstalledEngine? InstalledInfo(string id) => _config.Installed.GetValueOrDefault(id);

    // ------------------------------------------------------------------ detection

    private EngineStatus Detect(string id)
    {
        var def = EngineCatalog.Get(id);
        if (_config.UserPaths.TryGetValue(id, out var user) && File.Exists(user))
            return new(id, user, EngineSource.UserPath);

        foreach (var dir in ExtraSearchDirs)
            foreach (var name in def.ExeNames)
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return new(id, p, EngineSource.SystemPath);
            }

        // FFprobe accompagne FFmpeg
        var ownId = id == EngineIds.FFprobe ? EngineIds.FFmpeg : id;
        if (_config.Installed.TryGetValue(ownId, out var inst) && Directory.Exists(inst.Folder))
        {
            var found = FindIn(inst.Folder, def.ExeNames);
            if (found is not null) return new(id, found, EngineSource.FileWheel);
        }
        var ownDir = Path.Combine(AppPaths.EnginesDir, ownId);
        if (Directory.Exists(ownDir))
        {
            var found = FindIn(ownDir, def.ExeNames);
            if (found is not null) return new(id, found, EngineSource.FileWheel);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var known in def.KnownPaths)
            {
                var expanded = Environment.ExpandEnvironmentVariables(known);
                if (expanded.Contains('*'))
                {
                    var dir = Path.GetDirectoryName(Path.GetDirectoryName(expanded))!;
                    var pattern = Path.GetFileName(Path.GetDirectoryName(expanded))!;
                    try
                    {
                        if (Directory.Exists(dir))
                            foreach (var d in Directory.GetDirectories(dir, pattern).OrderByDescending(x => x))
                            {
                                var p = Path.Combine(d, Path.GetFileName(expanded));
                                if (File.Exists(p)) return new(id, p, EngineSource.Installed);
                            }
                    }
                    catch { }
                }
                else if (File.Exists(expanded)) return new(id, expanded, EngineSource.Installed);
            }
        }

        if (!(OperatingSystem.IsWindows() && def.NoPathLookupOnWindows) && !def.IsLibrary)
        {
            var onPath = FindOnPath(def.ExeNames);
            if (onPath is not null) return new(id, onPath, EngineSource.SystemPath);
        }
        return new(id, null, EngineSource.None);
    }

    private static string? FindIn(string root, string[] names)
    {
        try
        {
            foreach (var name in names)
            {
                var hit = Directory.EnumerateFiles(root, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true }).FirstOrDefault();
                if (hit is not null) return hit;
            }
        }
        catch { }
        return null;
    }

    private static string? FindOnPath(string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var name in names)
        {
            // Sous Windows on n'accepte que les noms en .exe/.com ; ailleurs les noms nus.
            bool winName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".com", StringComparison.OrdinalIgnoreCase);
            if (OperatingSystem.IsWindows() != winName) continue;
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var p = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ installation

    /// <summary>Prepare une installation : version, taille, adresse, empreinte. Ne telecharge rien.</summary>
    public async Task<InstallOffer> PrepareGitHubInstallAsync(string id, CancellationToken ct)
    {
        var def = EngineCatalog.Get(id);
        if (def.Method != InstallMethod.GitHubRelease || def.GitHubRepo is null || def.AssetPattern is null)
            throw new ConversionException(ConversionErrorKind.NotSupported, $"{def.Name} ne s'installe pas automatiquement depuis FileWheel.");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{def.GitHubRepo}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new ConversionException(ConversionErrorKind.EngineMissing, $"GitHub a répondu {(int)resp.StatusCode} pour {def.GitHubRepo}. Réessayez plus tard ou installez {def.Name} manuellement.");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToList();
        var main = assets.FirstOrDefault(a => Regex.IsMatch(a.GetProperty("name").GetString() ?? "", def.AssetPattern, RegexOptions.IgnoreCase));
        if (main.ValueKind == JsonValueKind.Undefined)
            throw new ConversionException(ConversionErrorKind.EngineMissing, $"Aucun paquet Windows portable trouvé dans la dernière version de {def.Name} ({tag}).");
        var (url, size, sha) = AssetInfo(main, def.Name);
        string? helperUrl = null, helperSha = null;
        if (def.HelperAssetPattern is not null)
        {
            var helper = assets.FirstOrDefault(a => Regex.IsMatch(a.GetProperty("name").GetString() ?? "", def.HelperAssetPattern, RegexOptions.IgnoreCase));
            if (helper.ValueKind == JsonValueKind.Undefined)
                throw new ConversionException(ConversionErrorKind.EngineMissing, $"Outil d'extraction absent de la version {tag} de {def.Name}.");
            (helperUrl, _, helperSha) = AssetInfo(helper, def.Name);
        }
        // ImageMagick publie parfois son paquet portable en .7z : il faut 7-Zip pour l'ouvrir.
        return new InstallOffer(def, tag, main.GetProperty("name").GetString()!, url, size, sha, helperUrl, helperSha);
    }

    private static (string url, long size, string sha) AssetInfo(JsonElement asset, string engineName)
    {
        var url = asset.GetProperty("browser_download_url").GetString()!;
        var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
        string? digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new ConversionException(ConversionErrorKind.Unsafe,
                $"GitHub ne fournit pas d'empreinte SHA-256 pour ce paquet de {engineName} : intégrité non vérifiable, installation refusée. Installez-le manuellement puis indiquez son emplacement.");
        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            throw new ConversionException(ConversionErrorKind.Unsafe, "Adresse de téléchargement inattendue : installation refusée.");
        return (url, size, digest[7..].ToLowerInvariant());
    }

    /// <summary>Telecharge, verifie et installe. A n'appeler qu'apres accord de l'utilisateur sur <paramref name="offer"/>.</summary>
    public async Task InstallAsync(InstallOffer offer, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var def = offer.Engine;
        var dest = Path.Combine(AppPaths.EnginesDir, def.Id);
        var staging = dest + ".installation-" + Guid.NewGuid().ToString("N")[..6];
        var dl = Path.Combine(AppPaths.DownloadsDir, offer.AssetName);
        try
        {
            await DownloadVerifiedAsync(offer.Url, dl, offer.Sha256, offer.Size, progress, ct).ConfigureAwait(false);
            Directory.CreateDirectory(staging);
            var limits = new ExtractionLimits(4L << 30, 50_000, 1000);
            var lower = offer.AssetName.ToLowerInvariant();
            if (lower.EndsWith(".zip"))
            {
                progress?.Report(new ConversionProgress(null, "Extraction…"));
                ArchiveSafety.ExtractZip(dl, staging, limits, ct);
            }
            else if (lower.EndsWith(".tgz") || lower.EndsWith(".tar.gz"))
            {
                progress?.Report(new ConversionProgress(null, "Extraction…"));
                await using var fs = File.OpenRead(dl);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                ArchiveSafety.ExtractTar(gz, new FileInfo(dl).Length, staging, limits, ct);
            }
            else if (lower.EndsWith(".7z"))
            {
                var sevenZ = PathOf(EngineIds.SevenZip);
                if (sevenZ is null && offer.HelperUrl is not null)
                {
                    sevenZ = Path.Combine(staging, "7zr.exe");
                    await DownloadVerifiedAsync(offer.HelperUrl, sevenZ, offer.HelperSha256!, 0, progress, ct).ConfigureAwait(false);
                }
                if (sevenZ is null)
                    throw new ConversionException(ConversionErrorKind.EngineMissing, "Ce paquet est au format 7Z : installez d'abord le moteur 7-Zip.");
                var outDir = Path.Combine(staging, "contenu");
                Directory.CreateDirectory(outDir);
                var r = await ProcessRunner.RunAsync(sevenZ, ["x", "-y", "-bb0", "-bd", "-o" + outDir, dl], ct).ConfigureAwait(false);
                if (r.ExitCode != 0) throw new ConversionException(ConversionErrorKind.Corrupt, "Extraction du paquet impossible.", r.StdErrTail);
            }
            else
            {
                File.Copy(dl, Path.Combine(staging, offer.AssetName));
            }

            if (FindIn(staging, def.ExeNames) is null)
                throw new ConversionException(ConversionErrorKind.EngineMissing, $"Le paquet téléchargé ne contient pas {string.Join(" / ", def.ExeNames.Take(1))}.");

            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            Directory.Move(staging, dest);
            lock (_gate)
            {
                _config.Installed[def.Id] = new InstalledEngine
                {
                    Version = offer.Version, Folder = dest, SourceUrl = offer.Url, Sha256 = offer.Sha256, InstalledAt = DateTime.UtcNow,
                };
                JsonStore.Save(AppPaths.EnginesFile, _config);
            }
            Log.Info($"Moteur installé : {def.Name} {offer.Version} ({offer.AssetName}, sha256 {offer.Sha256[..12]}…)");
            Refresh();
        }
        finally
        {
            try { if (File.Exists(dl)) File.Delete(dl); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    /// <summary>Supprime un moteur installe par FileWheel (jamais une installation de l'utilisateur).</summary>
    public void Uninstall(string id)
    {
        var dir = Path.Combine(AppPaths.EnginesDir, id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        lock (_gate)
        {
            _config.Installed.Remove(id);
            JsonStore.Save(AppPaths.EnginesFile, _config);
        }
        Refresh();
    }

    /// <summary>Arguments winget pour installer un moteur (lance par l'interface apres accord).</summary>
    public static IReadOnlyList<string> WingetArguments(EngineDefinition def) =>
        def.WingetId is null ? [] : ["install", "--id", def.WingetId, "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements"];

    private static async Task DownloadVerifiedAsync(string url, string target, string expectedSha, long expectedSize,
        IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".part";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using (var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? expectedSize;
            if (total > 2L << 30) throw new ConversionException(ConversionErrorKind.TooLarge, "Paquet anormalement volumineux : téléchargement refusé.");
            await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await input.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                sha.AppendData(buf, 0, n);
                done += n;
                if (total > 0) progress?.Report(new ConversionProgress((double)done / total, $"Téléchargement {done >> 20} / {total >> 20} Mio"));
            }
            var actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                output.Close();
                File.Delete(tmp);
                throw new ConversionException(ConversionErrorKind.Unsafe, "Empreinte SHA-256 incorrecte : fichier altéré ou corrompu. Installation annulée.", $"attendu {expectedSha}, obtenu {actual}");
            }
        }
        File.Move(tmp, target, overwrite: true);
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileWheel", "0.1"));
        return c;
    }
}
