using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileWheel.App.Tools;
using FileWheel.App.Ui;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Updates;
using FileWheel.Core.Localization;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;
using FileWheel.Core.Util;
using FileWheel.Core.Wheel;
using FileWheel.Windows.Imaging;

namespace FileWheel.App;

/// <summary>
/// Verification automatique sur un vrai Windows (GitHub Actions) : ouvre chaque fenetre et la roue
/// pour quelques fichiers d'exemple, enregistre le rendu en PNG, et dresse la liste des textes restes
/// en francais quand l'interface est en anglais. Aucune boite de message n'est bloquante.
/// Code de retour : nombre d'ecrans en erreur (0 = tout s'est ouvert).
/// </summary>
public static class CaptureMode
{
    private static string _out = "";
    private static readonly StringBuilder Report = new();
    private static int _failures;

    public static int Run(string outDir, string language)
    {
        _out = Path.GetFullPath(outDir);
        Directory.CreateDirectory(_out);
        var home = Path.Combine(Path.GetTempPath(), "filewheel-capture-" + language);
        if (Directory.Exists(home)) Directory.Delete(home, true);
        AppPaths.OverrideRoot(home);
        Loc.SetEnglish(language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        Loc.RecordMisses = true;
        Ui.Ui.Headless = true;
        Log.Info($"Mode capture ({Loc.Code}) → {_out}");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.ThemeMode = System.Windows.ThemeMode.Light;
        app.Startup += async (_, _) =>
        {
            Theme.Install(app);
            try { await RunScenesAsync(); }
            catch (Exception ex) { Fail("scènes", ex); }
            Finish();
            app.Shutdown(_failures);
        };
        return app.Run();
    }

    private static async Task RunScenesAsync()
    {
        var settings = new AppSettings();
        var deps = new DependencyManager();
        if (Environment.GetEnvironmentVariable("FILEWHEEL_CAPTURE_ENGINES") is { Length: > 0 } dir && Directory.Exists(dir))
            deps.ExtraSearchDirs.Add(dir);
        var engine = new ConversionEngine(() => settings.Conversion, [new WicImageProvider()], deps);
        Services.Engine = engine;
        Line($"Moteurs : " + string.Join(", ", EngineCatalog.All.Select(e => $"{e.Id}={(deps.IsAvailable(e.Id) ? "oui" : "non")}")));

        // ---------------------------------------------------------------- fichiers d'exemple
        var samples = Directory.CreateDirectory(Path.Combine(AppPaths.DataDir, "exemples")).FullName;
        var photo = Path.Combine(samples, "photo.png");
        var textImage = Path.Combine(samples, "texte.png");
        File.WriteAllBytes(photo, SamplePhoto());
        File.WriteAllBytes(textImage, SampleText());
        var txt = Path.Combine(samples, "rapport.txt");
        File.WriteAllText(txt, "Rapport annuel 2026\n\nFileWheel converts files while you drag them.\n" + string.Join("\n", Enumerable.Range(1, 80).Select(i => $"Ligne {i} : texte d'exemple.")));
        string? pdf = null;
        try
        {
            var cap = engine.Registry.Best("txt", "pdf");
            var prep = engine.Manager.Prepare(new ConversionOrder { Inputs = [txt], CapabilityKey = cap!.Key, OutputDir = samples });
            var r = await engine.Manager.RunAsync(prep, new Progress<ConversionProgress>(_ => { }), CancellationToken.None);
            pdf = r.Outputs[0];
        }
        catch (Exception ex) { Fail("PDF d'exemple", ex); }

        // ---------------------------------------------------------------- roue
        var wheel = new WheelWindow
        {
            Settings = () => settings,
            MenuFactory = files => WheelMenuBuilder.Build(files.Select(f => SelectedEntry.FromPath(f, true)).ToList(), settings, engine.Registry, []),
        };
        Scene("roue-image", () => wheel.PreviewFor([photo], hoverId: "tool.removebg"));
        if (pdf is not null)
        {
            Scene("roue-pdf", () => wheel.PreviewFor([pdf], hoverId: "convert.dialog"));
            Scene("roue-pdf-outils", () => wheel.PreviewFor([pdf], branchId: "pdf", hoverId: "pdf.compress"));
            Scene("roue-pdf-fichier", () => wheel.PreviewFor([pdf], branchId: "file"));
        }
        settings.Wheel.Layout = WheelMenuLayout.Classic;
        Scene("roue-classique", () => wheel.PreviewFor([photo, textImage], hoverId: "convert"));
        settings.Wheel.Layout = WheelMenuLayout.Express;

        // ---------------------------------------------------------------- fenetres
        var sw = new SettingsWindow(settings, engine, _ => { }, () => [], _ => { });
        foreach (var page in new[] { "Général", "Déclenchement", "Roue", "Conversion", "Moteurs", "Actions personnalisées", "Historique", "À propos" })
            await WindowScene("parametres-" + Slug(page), sw, () => sw.ShowTab(page), close: false);
        sw.Close();

        if (ToolWindow.TryLoad(photo, null, out var loaded))
        {
            await WindowScene("outil-recadrer", new CropWindow(loaded));
            await WindowScene("outil-fond", new BackgroundWindow(loaded));
            await WindowScene("outil-compresser", new CompressWindow([photo], loaded), delayMs: 1200);
            await WindowScene("outil-annoter", new AnnotateWindow(loaded));
            await WindowScene("outil-infos", new MetadataWindow(loaded));
            if (deps.IsAvailable(EngineIds.BackgroundRemoval))
                await WindowScene("outil-detourer", new RemoveBackgroundWindow(loaded), delayMs: 20000);
            else Line("outil-detourer : moteur absent (non capturé)");
        }
        await WindowScene("outil-texte-image", new OcrWindow(textImage), delayMs: 4000);
        if (pdf is not null && deps.IsAvailable(EngineIds.Pdfium))
        {
            FileWheel.Conversion.Providers.Pdf.PdfTools.EnsureEngine(deps);
            int count;
            using (var d = FileWheel.Conversion.Providers.Pdf.Pdfium.Open(pdf, null)) count = d.PageCount;
            await WindowScene("pdf-pages", new PdfPagesWindow(pdf, count), delayMs: 2500);
            await WindowScene("outil-texte-pdf", new OcrWindow(pdf), delayMs: 6000);
        }
        else Line("pdf-pages : PDFium absent (non capturé)");

        await WindowScene("conversion-rapide", new QuickConvertWindow(engine, () => settings, [photo], null, _ => { }, _ => { }));
        await WindowScene("convertisseur", new ConverterWindow(engine, () => settings, [photo, txt], _ => { }));
        await WindowScene("taches", new TasksWindow(engine, new OperationsCenter()));

        // ---------------------------------------------------------------- dialogues
        await WindowScene("bienvenue", new FirstRunDialog(settings.Trigger.Chord.ToString()));
        await WindowScene("pdf-compresser", new PdfCompressDialog(1));
        if (pdf is not null) await WindowScene("pdf-fusionner", new PdfMergeDialog([pdf, pdf]));
        var fake = new UpdateInfo(new Version(9, 9, 9), "v9.9.9", Updater.ReleasesPage,
            "## Nouveautés\n- Outils PDF dans la roue\n- Détourage IA local\n- Interface en anglais", null, 0, null, null);
        await WindowScene("mise-a-jour", new UpdateDialog(fake, portable: false));
        await WindowScene("signaler", new ReportProblemDialog());
        try
        {
            var offer = await deps.PrepareGitHubInstallAsync(EngineIds.BackgroundRemoval, CancellationToken.None);
            await WindowScene("accord-moteur", new EngineConsentDialog(offer));
        }
        catch (Exception ex) { Fail("accord-moteur", ex); }

        // ---------------------------------------------------------------- verifications fonctionnelles
        await CheckOcrAsync(textImage);
        CheckUpdateParsing();
    }

    // ------------------------------------------------------------------ verifications

    private static async Task CheckOcrAsync(string textImage)
    {
        try
        {
            var langs = FileWheel.Windows.Ocr.OcrService.Languages();
            Line("OCR : langues = " + (langs.Count == 0 ? "(aucune)" : string.Join(", ", langs.Select(l => l.Tag))));
            if (langs.Count == 0) return;
            var img = ImageIO.Load(textImage);
            var src = new FormatConvertedBitmap(img.Bitmap, PixelFormats.Bgra32, null, 0);
            var px = new byte[src.PixelWidth * src.PixelHeight * 4];
            src.CopyPixels(px, src.PixelWidth * 4, 0);
            var text = await FileWheel.Windows.Ocr.OcrService.RecognizeAsync(px, src.PixelWidth, src.PixelHeight, null);
            Line("OCR : texte reconnu = " + text.Replace("\n", " | "));
            if (!text.Contains("drop it on the wheel", StringComparison.OrdinalIgnoreCase)) Fail("OCR", new Exception("texte de l'exemple non reconnu"));
        }
        catch (Exception ex) { Fail("OCR", ex); }
    }

    private static void CheckUpdateParsing()
    {
        var info = Updater.Parse("""{"tag_name":"v1.2.3","html_url":"https://github.com/x","body":"","draft":false,"prerelease":false,"assets":[{"name":"FileWheel-Setup-1.2.3.exe","size":10,"browser_download_url":"https://github.com/a/b.exe","digest":"sha256:ABCDEF"}]}""");
        if (info?.Version != new Version(1, 2, 3) || info.SetupSha256 != "abcdef") Fail("mise à jour (analyse)", new Exception("analyse incorrecte"));
        else Line("Mise à jour : analyse de la Release OK");
    }

    // ------------------------------------------------------------------ scenes

    private static void Scene(string name, Func<FrameworkElement> build)
    {
        try
        {
            var e = build();
            Save(name, Render(e, Brushes.Transparent));
        }
        catch (Exception ex) { Fail(name, ex); }
    }

    private static async Task WindowScene(string name, Window w, Action? prepare = null, int delayMs = 500, bool close = true)
    {
        try
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = 0;
            w.Top = 0;
            w.ShowActivated = false;
            if (!w.IsVisible) w.Show();
            prepare?.Invoke();
            await Settle(delayMs);
            Ui.Ui.Translate(w);
            await Settle(50);
            if (w.Content is FrameworkElement content) Save(name, Render(content, w.Background));
        }
        catch (Exception ex) { Fail(name, ex); }
        finally { if (close) try { w.Close(); } catch { } }
    }

    private static async Task Settle(int ms)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static BitmapSource Render(FrameworkElement e, Brush? background)
    {
        double w = Math.Max(1, e.ActualWidth), h = Math.Max(1, e.ActualHeight);
        if (e.ActualWidth < 1) { e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); w = e.DesiredSize.Width; h = e.DesiredSize.Height; e.Arrange(new Rect(0, 0, w, h)); }
        const double scale = 1.5; // captures nettes (equivalent 144 ppp)
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (background is not null) dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            // Viewbox absolu : les marges vides en haut et a gauche sont gardees (sinon le contenu remonte)
            dc.DrawRectangle(new VisualBrush(e) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) }, null, new Rect(0, 0, w, h));
        }
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static void Save(string name, BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        var path = Path.Combine(_out, $"{Loc.Code}-{name}.png");
        using (var fs = File.Create(path)) enc.Save(fs);
        Line($"{name} : OK ({bmp.PixelWidth}×{bmp.PixelHeight})");
    }

    private static void Fail(string name, Exception ex)
    {
        _failures++;
        Line($"{name} : ÉCHEC — {ex.GetType().Name}: {ex.Message}");
        Log.Error("Capture " + name, ex);
    }

    private static void Line(string s)
    {
        Report.AppendLine(s);
        Console.WriteLine(s);
    }

    private static void Finish()
    {
        File.WriteAllText(Path.Combine(_out, $"{Loc.Code}-rapport.txt"), Report.ToString(), Encoding.UTF8);
        if (Loc.English)
            File.WriteAllLines(Path.Combine(_out, "en-textes-non-traduits.txt"), Loc.Misses.Keys.OrderBy(k => k, StringComparer.Ordinal), Encoding.UTF8);
        try
        {
            var log = Log.CurrentLogFile;
            Log.Flush();
            if (File.Exists(log)) File.Copy(log, Path.Combine(_out, $"{Loc.Code}-journal.log"), true);
        }
        catch { }
    }

    private static string Slug(string s) =>
        new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');

    // ------------------------------------------------------------------ images d'exemple

    private static byte[] SamplePhoto()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(120, 180, 230), Color.FromRgb(240, 245, 250), 90), null, new Rect(0, 0, 900, 600));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(90, 160, 90)), null, new Rect(0, 430, 900, 170));
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(255, 140, 60), Color.FromRgb(200, 60, 30)), null, new Point(450, 330), 150, 150);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 230, 120)), null, new Point(760, 110), 55, 55);
        }
        return Png(dv, 900, 600);
    }

    private static byte[] SampleText()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 500));
            var lines = new[] { "FileWheel", "Drag a file, hold Alt, drop it on the wheel.", "Convert, compress, crop and recognize text." };
            double y = 60;
            foreach (var (l, i) in lines.Select((l, i) => (l, i)))
            {
                var ft = new FormattedText(l, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), i == 0 ? 72 : 40, Brushes.Black, 1);
                dc.DrawText(ft, new Point(60, y));
                y += ft.Height + 30;
            }
        }
        return Png(dv, 1200, 500);
    }

    private static byte[] Png(Visual v, int w, int h)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
