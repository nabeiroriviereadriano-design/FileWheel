using FileWheel.Conversion.Updates;
using FileWheel.Core.Formats;
using FileWheel.Core.Localization;
using FileWheel.Core.Logging;
using FileWheel.Core.Settings;
using FileWheel.Core.Wheel;
using static FileWheel.Tests.Env;
using static FileWheel.Tests.T;

namespace FileWheel.Tests;

/// <summary>Interface en anglais, journal nettoye pour les signalements, lecture des Releases (mises a jour).</summary>
public static class LocTests
{
    public static async Task RunAll()
    {
        Suite("Langue de l'interface (français / anglais)");

        await Run("Dictionnaire anglais : textes exacts, modèles, majuscules, lignes", () =>
        {
            try
            {
                Loc.SetEnglish(true);
                Check(Loc.Count > 1000, $"entrées : {Loc.Count}");
                Equal("Crop", Loc.T("Recadrer"), "exact");
                Equal("CROP", Loc.T("RECADRER"), "majuscules");
                Equal("OPEN WITH", Loc.T("OUVRIR AVEC"), "libellé de secteur");
                Equal("Merging 3 PDFs", Loc.T("Fusion de 3 PDF"), "modèle");
                Equal("Copy (3)", Loc.T("Copier (3)"), "suffixe (n)");
                Equal("Conversion failed\nphoto.png", Loc.T("Conversion impossible\nphoto.png"), "plusieurs lignes");
                Equal("FileWheel 1.2.3 is available", Loc.F("FileWheel {0} est disponible", "1.2.3"), "Loc.F");
                Equal("nom inconnu du dictionnaire", Loc.T("nom inconnu du dictionnaire"), "texte inconnu inchangé");
                Equal("Copying photo.jpg to Docs", Loc.T("Copie de photo.jpg vers Docs"), "morceau variable traduit");
            }
            finally { Loc.SetEnglish(false); }
            Equal("Recadrer", Loc.T("Recadrer"), "français par défaut");
        });

        await Run("Langue automatique : français seulement si Windows est en français", () =>
        {
            Check(Loc.IsEnglishFor(UiLanguage.English) && !Loc.IsEnglishFor(UiLanguage.French), "choix explicites");
            var saved = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                System.Globalization.CultureInfo.CurrentUICulture = new("fr-FR");
                Check(!Loc.IsEnglishFor(UiLanguage.Auto), "Windows en français");
                System.Globalization.CultureInfo.CurrentUICulture = new("de-DE");
                Check(Loc.IsEnglishFor(UiLanguage.Auto), "autre langue → anglais");
            }
            finally { System.Globalization.CultureInfo.CurrentUICulture = saved; }
        });

        await Run("Toute la roue est traduite (libellés, aides, raisons, secteurs)", () =>
        {
            var missing = new SortedSet<string>();
            try
            {
                Loc.SetEnglish(true);
                var settings = new AppSettings();
                IReadOnlyList<SelectedEntry>[] selections =
                [
                    [SelectedEntry.FromPath(Sample("rapport.pdf"), true)],
                    [SelectedEntry.FromPath(Sample("rapport.pdf"), true), SelectedEntry.FromPath(Sample("rapport.pdf"), true)],
                    [SelectedEntry.FromPath(Sample("photo exif.jpg"), true)],
                    [SelectedEntry.FromPath(Sample("photo exif.jpg"), true), SelectedEntry.FromPath(Sample("logo transparent.png"), true)],
                    [SelectedEntry.FromPath(Sample("clip vidéo.mp4"), true)],
                    [SelectedEntry.FromPath(Sample("photos été.zip"), true)],
                    [SelectedEntry.FromPath(Env.Samples, false)],
                ];
                foreach (var layout in new[] { WheelMenuLayout.Express, WheelMenuLayout.Classic })
                {
                    settings.Wheel.Layout = layout;
                    foreach (var sel in selections)
                        Walk(WheelMenuBuilder.Build(sel, settings, Engine.Registry, []), missing);
                }
            }
            finally { Loc.SetEnglish(false); }
            Check(missing.Count == 0, "non traduits : " + string.Join(" | ", missing.Take(15)));
        });

        Suite("Signalement et mises à jour");

        await Run("Journal nettoyé : chemins, noms de fichiers et e-mails masqués", () =>
        {
            var line = @"Conversion de « C:\Users\marie\Documents\Mes vacances.jpg » vers PNG : moi@example.com ; fichier rapport_2024.pdf ; System.IO.IOException";
            var clean = LogSanitizer.Clean(line);
            Check(!clean.Contains("marie") && !clean.Contains("vacances") && !clean.Contains("rapport_2024"), "données retirées : " + clean);
            Check(clean.Contains(".jpg") && clean.Contains(".pdf"), "extensions gardées : " + clean);
            Check(clean.Contains("<e-mail>"), "e-mail masqué");
            Check(clean.Contains("System.IO.IOException"), "noms techniques gardés : " + clean);
            Check(clean.Contains("Conversion de") && clean.Contains("vers PNG"), "texte du journal gardé");
        });

        await Run("Release GitHub : version, installateur, empreinte, versions ignorées", () =>
        {
            var json = """
            {"tag_name":"v0.4.1","html_url":"https://github.com/x/y/releases/tag/v0.4.1","body":"notes","draft":false,"prerelease":false,
             "assets":[
               {"name":"FileWheel-Setup-0.4.1.exe","size":123,"browser_download_url":"https://github.com/nabeiroriviereadriano-design/FileWheel/releases/download/v0.4.1/FileWheel-Setup-0.4.1.exe","digest":"sha256:AB12"},
               {"name":"FileWheel-0.4.1-portable-win-x64.zip","size":5,"browser_download_url":"https://github.com/z.zip"}]}
            """;
            var info = Updater.Parse(json)!;
            Equal(new Version(0, 4, 1), info.Version, "version");
            Equal("ab12", info.SetupSha256, "empreinte publiée par GitHub");
            Check(info.PortableUrl is not null, "archive portable repérée");
            Check(Updater.Parse(json.Replace("\"prerelease\":false", "\"prerelease\":true")) is null, "préversion ignorée");
            var sums = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  FileWheel-Setup-0.4.1.exe\nffff  autre.zip";
            Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", Updater.ParseSums(sums, "FileWheel-Setup-0.4.1.exe"), "SHA256SUMS.txt");
        });
    }

    private static void Walk(WheelItem item, SortedSet<string> missing)
    {
        foreach (var c in item.Children)
        {
            foreach (var s in new[] { c.Label, c.Tooltip, c.DisabledReason, WheelMenuBuilder.CaptionOf(c) })
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                var t = Loc.T(s);
                if (t == s && Loc.LooksFrench(s)) missing.Add(s);
            }
            Walk(c, missing);
        }
    }
}
