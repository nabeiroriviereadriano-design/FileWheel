using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FileWheel.Conversion.Updates;
using FileWheel.Core.Localization;
using FileWheel.Core.Logging;
using FileWheel.Core.Util;

namespace FileWheel.App.Ui;

/// <summary>Une nouvelle version est disponible : notes de version, puis mise a jour en un clic.</summary>
public sealed class UpdateDialog : DialogWindow
{
    public enum Choice { Later, Skip, Install }
    public Choice Result { get; private set; } = Choice.Later;

    public UpdateDialog(UpdateInfo info, bool portable) : base("Mise à jour de FileWheel", 600)
    {
        Body.Children.Add(Ui.Title(Loc.F("FileWheel {0} est disponible", info.Version.ToString(3))));
        Body.Children.Add(Ui.Text(Loc.F("Version installée : {0}.", Updater.Current.ToString(3))));
        if (info.Notes.Trim().Length > 0)
        {
            Body.Children.Add(Ui.Heading("Nouveautés"));
            Body.Children.Add(new TextBox
            {
                Text = PlainNotes(info.Notes), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 8),
            });
        }
        Body.Children.Add(Ui.Hint(portable
            ? "Version portable : téléchargez la nouvelle archive et remplacez le dossier (vos réglages sont dans le sous-dossier « FileWheel-data »)."
            : "L'installateur est téléchargé depuis GitHub, son empreinte SHA-256 est vérifiée, puis FileWheel est fermé, mis à jour et relancé. Vos réglages sont conservés."));
        var link = Ui.Button("Voir la page de la version", () => Open(info.PageUrl));
        Body.Children.Add(link);

        AddButton("Plus tard", false, isCancel: true);
        Buttons.Children.Add(Ui.Button("Ignorer cette version", () => { Result = Choice.Skip; DialogResult = false; }));
        if (portable)
            Buttons.Children.Add(Ui.Button("Télécharger", () => { Open(info.PortableUrl ?? info.PageUrl); DialogResult = false; }, accent: true));
        else
            Buttons.Children.Add(Ui.Button("Mettre à jour", () => { Result = Choice.Install; DialogResult = true; }, accent: true));
    }

    /// <summary>Notes Markdown → texte lisible (titres et puces simplifies).</summary>
    private static string PlainNotes(string md)
    {
        var sb = new StringBuilder();
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith('#')) line = line.TrimStart('#', ' ').ToUpperInvariant();
            line = line.Replace("**", "").Replace("`", "");
            if (line.StartsWith("- ") || line.StartsWith("* ")) line = "• " + line[2..];
            sb.AppendLine(line);
        }
        return sb.ToString().Trim();
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Ouverture du lien : " + ex.Message); }
    }
}

/// <summary>
/// Signaler un probleme : description, version, Windows, extrait du journal nettoye (chemins et noms de
/// fichiers masques). Tout est visible et modifiable avant envoi ; l'envoi ouvre GitHub dans le navigateur
/// (un compte GitHub est necessaire), ou le rapport peut etre copie.
/// </summary>
public sealed class ReportProblemDialog : DialogWindow
{
    private readonly TextBox _what = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _log = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 170, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly CheckBox _withLog;

    public ReportProblemDialog() : base("Signaler un problème", 680)
    {
        Body.Children.Add(Ui.Text("Décrivez ce que vous faisiez et ce qui s'est passé. Le rapport s'ouvre sur GitHub (dépôt public de FileWheel) : vous pourrez encore le relire avant de l'envoyer."));
        Body.Children.Add(Ui.Heading("Que s'est-il passé ?"));
        Body.Children.Add(_what);
        Body.Children.Add(Ui.Heading("Informations jointes"));
        Body.Children.Add(Ui.Text(SystemInfo(), Ui.Subtle, 12));
        _withLog = Ui.Check("Joindre la fin du journal (noms de fichiers, chemins et e-mails masqués)", true, v => _log.IsEnabled = v);
        Body.Children.Add(_withLog);
        _log.Text = RecentLog(40);
        Body.Children.Add(_log);
        Body.Children.Add(Ui.Hint("Relisez l'extrait : vous pouvez supprimer ce que vous ne voulez pas publier."));

        AddButton("Annuler", false, isCancel: true);
        Buttons.Children.Add(Ui.Button("Copier le rapport", () =>
        {
            Clipboard.SetText(Body_());
            Ui.Message("Rapport copié. Collez-le dans un nouveau signalement sur GitHub, ou dans un message.", owner: this);
        }));
        Buttons.Children.Add(Ui.Button("Ouvrir sur GitHub", () => { UpdateDialog.Open(IssueUrl()); DialogResult = true; }, accent: true));
    }

    public static string SystemInfo() =>
        $"FileWheel {Updater.Current.ToString(3)}{(AppPaths.IsPortable ? " (portable)" : "")} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · " +
        $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} · {Loc.Code} · .NET {Environment.Version}";

    /// <summary>Dernieres lignes du journal du jour, nettoyees.</summary>
    public static string RecentLog(int lines)
    {
        try
        {
            var file = Log.CurrentLogFile;
            if (file is null || !File.Exists(file)) return "";
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            var all = r.ReadToEnd().Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\n", all.TakeLast(lines).Select(LogSanitizer.Clean));
        }
        catch (Exception ex) { return "(" + ex.Message + ")"; }
    }

    private string Body_()
    {
        var sb = new StringBuilder();
        sb.AppendLine("### " + Loc.T("Description")).AppendLine(_what.Text.Trim()).AppendLine();
        sb.AppendLine("### " + Loc.T("Environnement")).AppendLine(SystemInfo()).AppendLine();
        if (_withLog.IsChecked == true && _log.Text.Trim().Length > 0)
            sb.AppendLine("### " + Loc.T("Journal")).AppendLine("```").AppendLine(_log.Text.Trim()).AppendLine("```");
        return sb.ToString();
    }

    /// <summary>Formulaire de signalement pre-rempli (champs du modele .github/ISSUE_TEMPLATE/bug_report.yml).</summary>
    private string IssueUrl()
    {
        var title = _what.Text.Trim().Split('\n')[0];
        if (title.Length > 80) title = title[..77] + "…";
        string log = _withLog.IsChecked == true ? _log.Text.Trim() : "";
        // Les navigateurs acceptent des adresses longues, GitHub environ 8 000 caracteres : le journal est raccourci si besoin
        string Build(string l) =>
            $"https://github.com/{Updater.Repository}/issues/new?template=bug_report.yml" +
            $"&title={Uri.EscapeDataString(title.Length > 0 ? title : "Problème")}" +
            $"&description={Uri.EscapeDataString(_what.Text.Trim())}" +
            $"&environment={Uri.EscapeDataString(SystemInfo())}" +
            $"&logs={Uri.EscapeDataString(l)}";
        var url = Build(log);
        while (url.Length > 7500 && log.Length > 0)
        {
            log = string.Join("\n", log.Split('\n').Skip(5));
            url = Build(log);
        }
        return url;
    }
}
