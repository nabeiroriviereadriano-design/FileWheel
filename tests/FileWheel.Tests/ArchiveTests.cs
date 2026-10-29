using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using static FileWheel.Tests.Env;
using static FileWheel.Tests.T;

namespace FileWheel.Tests;

public static class ArchiveTests
{
    private static string Dir(string name)
    {
        var d = Path.Combine(Work, "archives", name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        return Directory.CreateDirectory(d).FullName;
    }

    public static async Task RunAll()
    {
        Suite("Archives — création, extraction, vérification");
        var svc = Engine.Archives;

        await Run("ZIP (intégré) : création d'un dossier avec accents, vérification CRC, extraction identique", async () =>
        {
            var d = Dir("zip");
            var src = Directory.CreateDirectory(Path.Combine(d, "Photos été")).FullName;
            File.WriteAllText(Path.Combine(src, "à.txt"), "bonjour");
            Directory.CreateDirectory(Path.Combine(src, "sous dossier"));
            File.WriteAllBytes(Path.Combine(src, "sous dossier", "data.bin"), Enumerable.Range(0, 100_000).Select(i => (byte)(i * 7)).ToArray());
            var zip = await svc.CreateAsync([src], "zip", 6, null, null, null, null, default);
            Equal("Photos été.zip", Path.GetFileName(zip), "nom");
            var test = await svc.TestAsync(zip, null, default);
            Check(test.Ok, test.Message);
            var outDir = Dir("zip-out");
            var x = await svc.ExtractAsync(zip, outDir, null, null, default);
            Equal(Hash(Path.Combine(src, "sous dossier", "data.bin")), Hash(Path.Combine(x, "sous dossier", "data.bin")), "contenu identique");
        });
        await Run("ZIP corrompu : vérification en échec", async () =>
        {
            var d = Dir("zip-bad");
            var good = await svc.CreateAsync([Sample("notes été.txt")], "zip", 9, null, d, null, null, default);
            var bytes = File.ReadAllBytes(good);
            bytes[60] ^= 0xFF; bytes[70] ^= 0xFF;
            var bad = Path.Combine(d, "abîmé.zip");
            File.WriteAllBytes(bad, bytes);
            var t = await svc.TestAsync(bad, null, default);
            Check(!t.Ok, "erreur détectée : " + t.Message);
        });
        await Run("TAR.GZ (intégré) : plusieurs éléments", async () =>
        {
            var d = Dir("tgz");
            var tgz = await svc.CreateAsync([Sample("notes été.txt"), Sample("cube.stl")], "tgz", 6, null, d, "lot", null, default);
            Check(tgz.EndsWith("lot.tar.gz"), tgz);
            Check((await svc.TestAsync(tgz, null, default)).Ok, "vérifié");
            var x = await svc.ExtractAsync(tgz, d, null, null, default);
            Check(File.Exists(Path.Combine(x, "notes été.txt")) && File.Exists(Path.Combine(x, "cube.stl")), "extrait");
        });
        await Run("GZ d'un seul fichier, puis GZ de plusieurs => TAR.GZ", async () =>
        {
            var d = Dir("gz");
            var gz = await svc.CreateAsync([Sample("notes été.txt")], "gz", 9, null, d, null, null, default);
            Equal("notes été.txt.gz", Path.GetFileName(gz), "nom");
            var x = await svc.ExtractAsync(gz, d, null, null, default);
            Equal(Hash(Sample("notes été.txt")), Hash(Directory.GetFiles(x)[0]), "contenu");
            var many = await svc.CreateAsync([Sample("notes été.txt"), Sample("cube.stl")], "gz", 6, null, d, "deux", null, default);
            Check(many.EndsWith(".tar.gz"), "basculé en TAR.GZ");
        });
        await Run("7Z avec mot de passe et en-têtes chiffrés (7-Zip)", async () =>
        {
            RequireEngine(EngineIds.SevenZip);
            var d = Dir("7z");
            var a = await svc.CreateAsync([Sample("notes été.txt")], "7z", 5, "s3cr3t é", d, null, null, default);
            Check((await svc.TestAsync(a, "s3cr3t é", default)).Ok, "vérifié avec le mot de passe");
            var wrong = await svc.TestAsync(a, "faux", default);
            Check(!wrong.Ok, "mauvais mot de passe refusé : " + wrong.Message);
            var ex = await Throws<ConversionException>(() => svc.ExtractAsync(a, d, "faux", null, default), "extraction");
            Equal(ConversionErrorKind.Protected, ex.Kind, "type");
            var x = await svc.ExtractAsync(a, d, "s3cr3t é", null, default);
            Equal(Hash(Sample("notes été.txt")), Hash(Directory.GetFiles(x)[0]), "contenu");
        });
        await Run("ZIP chiffré AES-256 (7-Zip) puis extraction (7-Zip)", async () =>
        {
            RequireEngine(EngineIds.SevenZip);
            var d = Dir("zip-aes");
            var z = await svc.CreateAsync([Sample("cube.stl")], "zip", 5, "mdp", d, null, null, default);
            var x = await svc.ExtractAsync(z, d, "mdp", null, default);
            Check(File.Exists(Path.Combine(x, "cube.stl")), "extrait");
        });
        await Run("XZ et BZ2 (7-Zip)", async () =>
        {
            RequireEngine(EngineIds.SevenZip);
            var d = Dir("xz");
            foreach (var f in new[] { "xz", "bz2" })
            {
                var a = await svc.CreateAsync([Sample("notes été.txt")], f, 5, null, d, null, null, default);
                var x = await svc.ExtractAsync(a, d, null, null, default);
                Equal(Hash(Sample("notes été.txt")), Hash(Directory.GetFiles(x)[0]), f);
            }
        });
        await Run("Mot de passe refusé pour TAR.GZ (format sans chiffrement)", async () =>
        {
            var ex = await Throws<ConversionException>(() => svc.CreateAsync([Sample("cube.stl")], "tgz", 5, "x", Dir("pwd"), null, null, default), "refus");
            Equal(ConversionErrorKind.NotSupported, ex.Kind, "type");
        });

        Suite("Archives — sécurité");
        await Run("Zip slip : entrées « ../ », absolues, lecteur et flux NTFS ignorées ; rien hors du dossier", async () =>
        {
            var d = Dir("slip");
            var outer = Path.GetDirectoryName(d)!;
            var x = await svc.ExtractAsync(Sample("piège zip slip.zip"), d, null, null, default);
            Check(File.Exists(Path.Combine(x, "normal.txt")), "entrée normale extraite");
            Check(!File.Exists(Path.Combine(outer, "evasion.txt")) && !File.Exists(Path.Combine(Path.GetDirectoryName(outer)!, "evasion.txt")), "aucune évasion");
            Check(!File.Exists("/tmp/absolu.txt"), "aucune écriture absolue");
            Check(svc.LastSkipped.Count >= 3, "entrées dangereuses listées : " + string.Join(", ", svc.LastSkipped));
        });
        await Run("Zip slip via 7-Zip : extraction refusée", async () =>
        {
            RequireEngine(EngineIds.SevenZip);
            var d = Dir("slip7");
            var cap = Engine.Registry.AllCapabilities.First(c => c.Kind == ConversionKind.Recompress && c.To == "7z");
            // Une archive 7z piegee : on la construit avec 7-Zip a partir d'un chemin relatif remontant
            var root = Directory.CreateDirectory(Path.Combine(d, "a", "b")).FullName;
            File.WriteAllText(Path.Combine(d, "a", "secret.txt"), "x");
            var arc = Path.Combine(d, "piege.7z");
            var r = Sh("bash", "-c", $"cd '{root}' && 7z a -spf2 '{arc}' ../secret.txt >/dev/null && 7z l -slt '{arc}' | grep '^Path'");
            if (!r.stdout.Contains("..")) Skip("7-Zip de cet environnement normalise les chemins : impossible de fabriquer l'archive piégée");
            var ex = await Throws<ConversionException>(() => svc.ExtractAsync(arc, Dir("slip7-out"), null, null, default), "extraction");
            Equal(ConversionErrorKind.Unsafe, ex.Kind, "type");
            _ = cap;
        });
        await Run("Bombe de décompression (80 Mio de zéros, taux ×1000) : arrêtée", async () =>
        {
            var d = Dir("bomb");
            var ex = await Throws<ConversionException>(() => svc.ExtractAsync(Sample("bombe.zip"), d, null, null, default), "bombe");
            Equal(ConversionErrorKind.Unsafe, ex.Kind, "type");
            Check(!Directory.EnumerateFileSystemEntries(d).Any(), "rien laissé sur le disque : " + string.Join(", ", Directory.EnumerateFileSystemEntries(d)));
        });
        await Run("TAR avec liens symboliques et matériels : liens ignorés, évasion refusée", async () =>
        {
            var d = Dir("tarlinks");
            var x = await svc.ExtractAsync(Sample("liens.tar.gz"), d, null, null, default);
            var all = Directory.EnumerateFileSystemEntries(x, "*", SearchOption.AllDirectories).ToList();
            Check(all.Any(p => p.EndsWith("normal.txt")), "fichier normal extrait");
            Check(!all.Any(p => p.Contains("lien")), "liens non recréés : " + string.Join(", ", all));
            Check(!File.Exists(Path.Combine(d, "evasion-tar.txt")) && !File.Exists(Path.Combine(Path.GetDirectoryName(d)!, "evasion-tar.txt")), "pas d'évasion");
        });
        await Run("Extraction : jamais d'écrasement (dossier numéroté)", async () =>
        {
            var d = Dir("noclobber");
            var a = await svc.ExtractAsync(Sample("photos été.zip"), d, null, null, default);
            var b = await svc.ExtractAsync(Sample("photos été.zip"), d, null, null, default);
            Check(a != b && Directory.Exists(a) && Directory.Exists(b), $"{Path.GetFileName(a)} / {Path.GetFileName(b)}");
        });
        await Run("Conversion d'archive ZIP -> 7Z via le convertisseur (contenu identique)", async () =>
        {
            RequireEngine(EngineIds.SevenZip);
            var key = Key("zip", "7z", ConversionKind.Recompress);
            var d = Dir("recompress");
            var r = await Convert(Sample("photos été.zip"), key, outDir: d);
            Equal("photos été.7z", Path.GetFileName(r.Outputs[0]), "nom");
            var x = await svc.ExtractAsync(r.Outputs[0], d, null, null, default);
            Check(Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories).Any(f => f.EndsWith("é à ç.txt")), "contenu");
        });
        await Run("Extraction via le convertisseur (dossier de sortie validé)", async () =>
        {
            var key = Key("zip", "extract", ConversionKind.Extract);
            var r = await Convert(Sample("photos été.zip"), key, outDir: Dir("extract-conv"));
            Check(Directory.Exists(r.Outputs[0]) && Directory.EnumerateFiles(r.Outputs[0], "*", SearchOption.AllDirectories).Count() == 3, r.Outputs[0]);
        });
        await Run("RAR : extraction seulement avec 7-Zip complet (jamais de création)", () =>
        {
            Check(!FileWheel.Conversion.Providers.Archives.ArchiveService.CreateFormats.Any(f => f.Id == "rar"), "pas de création RAR proposée");
            Skip("aucune archive RAR d'essai (outil rar non libre absent de l'environnement)");
        });
    }
}
