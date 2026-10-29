using FileWheel.Core.Logging;
using FileWheel.Tests;

// Harnais de tests FileWheel : moteurs reels, fichiers generes, sorties verifiees.
// Usage : dotnet run --project tests/FileWheel.Tests [-- filtre]
Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length >= 2 && args[0] == "--catalogue") { Catalogue.Write(args[1]); return 0; }
if (args.Length > 0) T.Filter = args[0];
Log.MinimumLevel = LogLevel.Debug;

Env.Init();
Console.WriteLine($"Dossier de test : {Env.Root}");
Samples.Generate();
if (Samples.Missing.Count > 0) Console.WriteLine("Outils absents : " + string.Join(", ", Samples.Missing));

await WheelTests.RunAll();
await FileOpsTests.RunAll();
await ConversionTests.RunAll();
await ArchiveTests.RunAll();
await WindowsOnlyTests.RunAll();

Log.Flush();
var env = $"Environnement : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, .NET {Environment.Version}. " +
          "Moteurs : " + string.Join(", ", FileWheel.Conversion.Engines.EngineCatalog.All
              .Select(e => $"{e.Name} {(Env.Engine.Dependencies.IsAvailable(e.Id) ? "présent" : "absent")}")) + ".";
var report = T.Report(env);
var reportPath = Environment.GetEnvironmentVariable("FILEWHEEL_TEST_OUTPUT") is { Length: > 0 } custom ? custom : Path.Combine(AppContext.BaseDirectory, "TEST-RESULTS.md");
File.WriteAllText(reportPath, report);
int fail = T.Results.Count(r => r.Status == "ÉCHEC");
Console.WriteLine();
Console.WriteLine($"{T.Results.Count(r => r.Status == "OK")} réussis, {fail} échecs, {T.Results.Count(r => r.Status == "NON EXÉCUTÉ")} non exécutés. Rapport : {reportPath}");
return fail == 0 ? 0 : 1;
