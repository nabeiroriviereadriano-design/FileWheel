using System.Diagnostics;
using System.Security.Cryptography;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Core.Settings;
using FileWheel.Core.Util;

namespace FileWheel.Tests;

/// <summary>Environnement de test : dossiers isoles, moteurs reels, utilitaires de verification.</summary>
public static class Env
{
    public static string Root { get; private set; } = "";
    public static string Samples { get; private set; } = "";
    public static string Work { get; private set; } = "";
    public static string ShimDir { get; private set; } = "";
    public static ConversionSettings Settings { get; } = new() { MinFreeSpaceMiB = 64, MaxConcurrent = 2 };
    public static ConversionEngine Engine { get; private set; } = null!;

    public static void Init()
    {
        Root = Path.Combine(Path.GetTempPath(), "filewheel-tests-" + DateTime.Now.ToString("HHmmss"));
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
        Directory.CreateDirectory(Root);
        AppPaths.OverrideRoot(Path.Combine(Root, "home"));
        Samples = Directory.CreateDirectory(Path.Combine(Root, "échantillons")).FullName;
        Work = Directory.CreateDirectory(Path.Combine(Root, "sorties")).FullName;
        ShimDir = Directory.CreateDirectory(Path.Combine(Root, "moteurs")).FullName;

        // ImageMagick 6 sous Linux : « magick » n'existe pas, on fournit une passerelle vers « convert ».
        if (Which("magick") is null && Which("convert") is not null)
        {
            var shim = Path.Combine(ShimDir, "magick");
            File.WriteAllText(shim, "#!/bin/sh\ncase \"$1\" in\n  identify) shift; exec identify \"$@\";;\n  *) exec convert \"$@\";;\nesac\n");
            Sh("chmod", "+x", shim);
        }
        // PDFium : bibliotheque fournie par le paquet Python pypdfium2 (memes binaires que bblanchon/pdfium-binaries)
        var pdfium = Sh("python3", "-c", "import pypdfium2_raw,os;print(os.path.dirname(pypdfium2_raw.__file__))").stdout.Trim();
        if (pdfium.Length > 0 && File.Exists(Path.Combine(pdfium, "libpdfium.so")))
            File.Copy(Path.Combine(pdfium, "libpdfium.so"), Path.Combine(ShimDir, "libpdfium.so"), true);

        // Calibre (QtWebEngine) refuse de tourner en root sans cette option : propre a l'environnement de test Linux.
        if (!OperatingSystem.IsWindows() && Environment.UserName == "root")
            Environment.SetEnvironmentVariable("QTWEBENGINE_CHROMIUM_FLAGS", "--no-sandbox");
        Engine = NewEngine();
    }

    public static ConversionEngine NewEngine(bool withEngines = true)
    {
        var deps = new DependencyManager();
        if (withEngines) deps.ExtraSearchDirs.Add(ShimDir);
        return new ConversionEngine(() => Settings, deps: deps);
    }

    public static string? Which(string exe)
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var p = Path.Combine(d, exe);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static (int code, string stdout, string stderr) Sh(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, o.Result, e.Result);
    }

    public static string Must(string exe, params string[] args)
    {
        var r = Sh(exe, args);
        if (r.code != 0) throw new AssertException($"{exe} a échoué : {r.stderr.Trim().Split('\n').LastOrDefault()}");
        return r.stdout;
    }

    public static string Sample(string name) => Path.Combine(Samples, name);

    public static string Hash(string path)
    {
        using var fs = File.OpenRead(path);
        return System.Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>Lance une conversion via le gestionnaire (meme chemin que l'application).</summary>
    public static async Task<ConversionResult> Convert(string input, string capabilityKey, Dictionary<string, string>? options = null,
        string? outDir = null, ConflictPolicy? conflicts = null, IReadOnlyList<string>? inputs = null, CancellationToken ct = default)
    {
        var order = new ConversionOrder
        {
            Inputs = inputs ?? [input], CapabilityKey = capabilityKey, Options = options ?? new(),
            OutputDir = outDir ?? Work, Conflicts = conflicts,
        };
        var prep = Engine.Manager.Prepare(order);
        return await Engine.Manager.RunAsync(prep, new Progress<ConversionProgress>(_ => { }), ct);
    }

    /// <summary>Cle de capacite pour une source et une cible (echec si inexistante).</summary>
    public static string Key(string source, string target, ConversionKind kind = ConversionKind.Convert)
    {
        var cap = Engine.Registry.Best(source, target, kind) ?? throw new AssertException($"Aucune capacité {source} -> {target} ({kind})");
        if (!Engine.Registry.EngineOk(cap)) throw new SkipException($"Moteur absent pour {source} -> {target} : {Engine.Registry.MissingEngine(cap)}");
        return cap.Key;
    }

    public static void RequireEngine(string id)
    {
        if (!Engine.Dependencies.IsAvailable(id)) throw new SkipException($"Moteur {id} non disponible dans cet environnement.");
    }

    // ------------------------------------------------------------- verifications de sortie

    public static (int w, int h, string alpha, int frames, string format) Identify(string path)
    {
        var o = Must("identify", "-format", "%w %h %A %m\n", path).Trim().Split('\n');
        var p = o[0].Split(' ');
        return (int.Parse(p[0]), int.Parse(p[1]), p[2], o.Length, p[3]);
    }

    public static (double duration, List<(string type, string codec)> streams, string format) Probe(string path)
    {
        var json = Must("ffprobe", "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path);
        var info = FileWheel.Conversion.Providers.Media.MediaProbe.ParseJson(json);
        return (info.Duration ?? 0, info.Streams.Select(s => (s.Type, s.Codec)).ToList(), info.Container ?? "");
    }

    public static string PdfText(string path) => Must("pdftotext", "-layout", path, "-");

    public static int PdfPages(string path)
    {
        var info = Must("pdfinfo", path);
        var line = info.Split('\n').First(l => l.StartsWith("Pages:"));
        return int.Parse(line["Pages:".Length..].Trim());
    }
}
