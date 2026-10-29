using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using FileWheel.Conversion.Engines;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Wheel;
using FileWheel.Windows.SystemIntegration;

namespace FileWheel.App.Ui;

/// <summary>
/// Fenetre de dialogue de base, charte hybride : contenu aere sur le fond de fenetre, bandeau de pied teinte
/// (boite de dialogue Windows 11), boutons alignes a droite avec l'action principale tout a droite (macOS).
/// Echap = annuler, Entree = action principale.
/// </summary>
public class DialogWindow : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(28, 24, 28, 18) };
    protected readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

    public DialogWindow(string title, double width = 480)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Ui.Decorate(this);
        var dock = new DockPanel();
        var footer = new Border { Padding = new Thickness(24, 14, 16, 14), BorderThickness = new Thickness(0, 1, 0, 0), Child = Buttons };
        footer.SetResourceReference(Border.BackgroundProperty, Theme.FooterBg);
        footer.SetResourceReference(Border.BorderBrushProperty, Theme.Separator);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(Body);
        Content = dock;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
        Loaded += (_, _) =>
        {
            Topmost = true;
            WindowStyles.ForceForeground(new WindowInteropHelper(this).Handle);
            Activate();
            Topmost = false;
        };
    }

    protected void AddButton(string text, bool? result, bool accent = false, bool isCancel = false)
    {
        var b = Ui.Button(text, () => { if (result is null) Close(); else DialogResult = result; }, accent);
        if (isCancel)
        {
            // Annuler a gauche, action principale a droite (ordre macOS, constant dans toute l'application)
            b.IsCancel = true;
            Buttons.Children.Insert(0, b);
        }
        else Buttons.Children.Add(b);
    }

    public bool Run() => ShowDialog() == true;
}

/// <summary>Saisie d'un texte (renommage...), validee avant fermeture.</summary>
public sealed class TextPromptDialog : DialogWindow
{
    private readonly TextBox _box;
    private readonly TextBlock _error;
    private readonly Func<string, string?> _validate;

    public string Value => _box.Text;

    public TextPromptDialog(string title, string label, string initial, Func<string, string?> validate, int selectLength = -1) : base(title)
    {
        _validate = validate;
        Body.Children.Add(Ui.Text(label));
        _box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 4), MinWidth = 380 };
        _error = Ui.Text("", Ui.Error, 12);
        Body.Children.Add(_box);
        Body.Children.Add(_error);
        _box.TextChanged += (_, _) => _error.Text = _validate(_box.Text) ?? "";
        Loaded += (_, _) =>
        {
            _box.Focus();
            if (selectLength >= 0) _box.Select(0, Math.Min(selectLength, initial.Length)); else _box.SelectAll();
        };
        var ok = Ui.Button("Valider", Confirm, accent: true);
        Buttons.Children.Add(ok);
        AddButton("Annuler", false, isCancel: true);
    }

    private void Confirm()
    {
        var err = _validate(_box.Text);
        if (err is not null) { _error.Text = err; return; }
        DialogResult = true;
    }

    public static string? Ask(string title, string label, string initial, Func<string, string?> validate, int selectLength = -1)
    {
        var d = new TextPromptDialog(title, label, initial, validate, selectLength);
        return d.Run() ? d.Value : null;
    }
}

/// <summary>Confirmation ; pour une action destructive, le bouton par defaut est « Annuler ».</summary>
public sealed class ConfirmDialog : DialogWindow
{
    public ConfirmDialog(string title, string message, string confirmText, bool destructive, IEnumerable<string>? details = null) : base(title, 520)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Icon(destructive ? Glyphs.DeleteForever : Glyphs.Info, 28, destructive ? Ui.Error : Ui.Accent));
        var text = Ui.Text(message, null, 14);
        text.Margin = new Thickness(14, 0, 0, 0);
        text.MaxWidth = 420;
        row.Children.Add(text);
        Body.Children.Add(row);
        if (details is not null)
        {
            var list = details.Take(8).ToList();
            var tb = Ui.Hint(string.Join("\n", list) + (details.Count() > 8 ? $"\n… et {details.Count() - 8} autre(s)" : ""));
            tb.Margin = new Thickness(42, 10, 0, 0);
            Body.Children.Add(tb);
        }
        var confirm = Ui.Button(confirmText, () => DialogResult = true, accent: !destructive, danger: destructive);
        if (destructive) confirm.IsDefault = false;
        var cancel = Ui.Button("Annuler", () => DialogResult = false);
        cancel.IsCancel = true;
        if (destructive) cancel.IsDefault = true; // Entree = annuler : rien n'est supprime par reflexe
        Buttons.Children.Add(cancel);
        Buttons.Children.Add(confirm);
    }

    public static bool Ask(string title, string message, string confirmText, bool destructive = false, IEnumerable<string>? details = null) =>
        new ConfirmDialog(title, message, confirmText, destructive, details).Run();
}

/// <summary>Conflit de nom pendant une copie / un deplacement.</summary>
public sealed class ConflictDialog : DialogWindow
{
    public ConflictChoice Choice { get; private set; } = ConflictChoice.Cancel;
    public bool ApplyToAll { get; private set; }

    public ConflictDialog(string source, string existing) : base("Un élément porte déjà ce nom", 560)
    {
        Body.Children.Add(Ui.Text($"« {Path.GetFileName(existing)} » existe déjà dans {Path.GetDirectoryName(existing)}.", null, 14));
        string Info(string p)
        {
            try
            {
                if (Directory.Exists(p)) return "Dossier, modifié le " + Directory.GetLastWriteTime(p).ToString("g");
                var fi = new FileInfo(p);
                return $"{Ui.Size(fi.Length)}, modifié le {fi.LastWriteTime:g}";
            }
            catch { return ""; }
        }
        Body.Children.Add(Ui.Hint("Élément à copier : " + Info(source)));
        Body.Children.Add(Ui.Hint("Élément existant : " + Info(existing)));
        var all = new CheckBox { Content = "Appliquer ce choix aux conflits suivants", Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Add(all);
        void Pick(ConflictChoice c) { Choice = c; ApplyToAll = all.IsChecked == true; DialogResult = true; }
        var cancel = Ui.Button("Annuler tout", () => Pick(ConflictChoice.Cancel));
        cancel.IsCancel = true;
        Buttons.Children.Add(cancel);
        Buttons.Children.Add(Ui.Button("Ignorer", () => Pick(ConflictChoice.Skip)));
        Buttons.Children.Add(Ui.Button("Remplacer", () => Pick(ConflictChoice.Replace), danger: true));
        Buttons.Children.Add(Ui.Button("Garder les deux (renommer)", () => Pick(ConflictChoice.Rename), accent: true));
    }

    /// <summary>Resolveur utilisable depuis un thread de travail (marshale vers l'interface).</summary>
    public static ConflictResolver Resolver() => (string src, string existing, out bool all) =>
    {
        ConflictChoice choice = ConflictChoice.Cancel;
        bool applyAll = false;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var d = new ConflictDialog(src, existing);
            if (d.Run()) { choice = d.Choice; applyAll = d.ApplyToAll; }
        });
        all = applyAll;
        return choice;
    };
}

/// <summary>Renommage en lot avec apercu.</summary>
public sealed class BatchRenameDialog : DialogWindow
{
    private readonly TextBox _pattern = new() { Text = "{nom} ({n})", Margin = new Thickness(0, 6, 0, 4) };
    private readonly TextBox _start = new() { Text = "1", Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ListBox _preview = new() { Height = 200, Margin = new Thickness(0, 8, 0, 0) };
    private readonly IReadOnlyList<string> _paths;
    public List<(string Source, string NewName)> Plan { get; private set; } = new();

    public BatchRenameDialog(IReadOnlyList<string> paths) : base($"Renommer {paths.Count} éléments", 600)
    {
        _paths = paths;
        Body.Children.Add(Ui.Text("Modèle de nom (l'extension d'origine est conservée) :"));
        Body.Children.Add(_pattern);
        Body.Children.Add(Ui.Hint("{nom} = nom d'origine, {n} = numéro, {n:000} = numéro sur 3 chiffres."));
        Body.Children.Add(Ui.Field("Premier numéro", _start, labelWidth: 140));
        Body.Children.Add(_preview);
        _pattern.TextChanged += (_, _) => Refresh();
        _start.TextChanged += (_, _) => Refresh();
        Buttons.Children.Add(Ui.Button("Renommer", () => { Refresh(); if (Plan.Count > 0) DialogResult = true; }, accent: true));
        AddButton("Annuler", false, isCancel: true);
        Refresh();
    }

    private void Refresh()
    {
        _preview.Items.Clear();
        int.TryParse(_start.Text, out var s);
        try
        {
            Plan = SafeFileOps.PlanBatchRename(_paths, _pattern.Text, Math.Max(0, s));
            foreach (var (src, name) in Plan.Take(200)) _preview.Items.Add($"{Path.GetFileName(src)}  →  {name}");
        }
        catch (Exception ex)
        {
            Plan = new();
            _preview.Items.Add("Modèle invalide : " + ex.Message);
        }
    }
}

/// <summary>Options de compression : format, niveau, mot de passe.</summary>
public sealed class CompressDialog : DialogWindow
{
    public string Format { get; private set; } = "zip";
    public int Level { get; private set; } = 6;
    public string? Password { get; private set; }

    public CompressDialog(int count, bool sevenZipAvailable) : base(count == 1 ? "Compresser" : $"Compresser {count} éléments")
    {
        var formats = FileWheel.Conversion.Providers.Archives.ArchiveService.CreateFormats
            .Select(f => (f.Id, f.Label + (f.NeedsSevenZip && !sevenZipAvailable ? " — nécessite 7-Zip" : ""))).ToList();
        var pwd = new PasswordBox { Margin = new Thickness(0, 2, 0, 2), MinWidth = 240 };
        var pwd2 = new PasswordBox { Margin = new Thickness(0, 2, 0, 2), MinWidth = 240 };
        var err = Ui.Text("", Ui.Error, 12);
        var combo = Ui.Combo(formats, "zip", v => Format = v);
        Body.Children.Add(Ui.Field("Format", combo, labelWidth: 160));
        var level = Ui.Slider(0, 9, 6, v => Level = (int)v);
        Body.Children.Add(Ui.Field("Niveau de compression", level, "0 = aucune, 9 = maximale (plus lent)", 160));
        Body.Children.Add(Ui.Field("Mot de passe (facultatif)", pwd, labelWidth: 160));
        Body.Children.Add(Ui.Field("Confirmer le mot de passe", pwd2, labelWidth: 160));
        Body.Children.Add(Ui.Hint("Le chiffrement (AES-256) est disponible pour ZIP et 7Z, avec le moteur 7-Zip."));
        Body.Children.Add(err);
        Buttons.Children.Add(Ui.Button("Compresser", () =>
        {
            var f = FileWheel.Conversion.Providers.Archives.ArchiveService.CreateFormats.First(x => x.Id == Format);
            if (pwd.Password != pwd2.Password) { err.Text = "Les mots de passe ne correspondent pas."; return; }
            if (pwd.Password.Length > 0 && !f.SupportsPassword) { err.Text = $"{f.Label} ne permet pas de mot de passe."; return; }
            if ((f.NeedsSevenZip || pwd.Password.Length > 0) && !sevenZipAvailable) { err.Text = "Ce choix nécessite le moteur 7-Zip (Paramètres › Moteurs)."; return; }
            Password = pwd.Password.Length > 0 ? pwd.Password : null;
            DialogResult = true;
        }, accent: true));
        AddButton("Annuler", false, isCancel: true);
    }
}

/// <summary>Demande d'accord avant d'installer un moteur externe : source, version, taille, licence, empreinte.</summary>
public sealed class EngineConsentDialog : DialogWindow
{
    public EngineConsentDialog(InstallOffer offer) : base($"Installer {offer.Engine.Name} ?", 600)
    {
        Body.Children.Add(Ui.Text(offer.Engine.Purpose, null, 14));
        Body.Children.Add(Ui.Field("Version", Ui.Text(offer.Version), labelWidth: 150));
        Body.Children.Add(Ui.Field("Fichier", Ui.Text($"{offer.AssetName} ({Ui.Size(offer.Size)})"), labelWidth: 150));
        Body.Children.Add(Ui.Field("Provenance", Ui.Text(offer.Url), labelWidth: 150));
        Body.Children.Add(Ui.Field("Empreinte SHA-256", new TextBox { Text = offer.Sha256, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas") }, labelWidth: 150));
        Body.Children.Add(Ui.Field("Licence", Ui.Text(offer.Engine.License), labelWidth: 150));
        Body.Children.Add(Ui.Hint("Le paquet est téléchargé depuis le dépôt officiel indiqué, vérifié avec l'empreinte publiée par GitHub, puis décompressé dans votre profil (%LOCALAPPDATA%\\FileWheel\\engines). Aucun installeur n'est exécuté, rien n'est modifié dans Windows. Le désinstalleur de FileWheel le supprime."));
        Buttons.Children.Add(Ui.Button("Télécharger et installer", () => DialogResult = true, accent: true));
        AddButton("Annuler", false, isCancel: true);
    }
}

/// <summary>Premier lancement : explication du geste et consentement au demarrage automatique.</summary>
public sealed class FirstRunDialog : DialogWindow
{
    public bool StartWithWindows { get; private set; }

    public FirstRunDialog(string chord, bool askStartup = true) : base("Bienvenue dans FileWheel", 560)
    {
        Body.Children.Add(Ui.Title("FileWheel est prêt"));
        Body.Children.Add(Ui.Text($"Dans l'Explorateur ou sur le Bureau, commencez à faire glisser un fichier, puis maintenez {chord} : la roue apparaît sous le curseur. Déposez le fichier sur un format ou un outil."));
        Body.Children.Add(Ui.Hint("Lâcher ailleurs que sur la roue fait le dépôt Windows habituel. Échap ou le centre de la roue annulent."));
        Body.Children.Add(Ui.Text("FileWheel reste discret dans la zone de notification (près de l'horloge)."));
        var start = new CheckBox { Content = "Lancer FileWheel au démarrage de Windows", IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
        if (askStartup)
        {
            Body.Children.Add(start);
            Body.Children.Add(Ui.Hint("Modifiable à tout moment dans Paramètres › Général ou depuis l'icône de notification."));
        }
        Buttons.Children.Add(Ui.Button("Commencer", () => { StartWithWindows = start.IsChecked == true; DialogResult = true; }, accent: true));
    }
}

public static class Pickers
{
    public static string? Folder(string title, string? initial = null)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false };
        if (initial is not null && Directory.Exists(initial)) d.InitialDirectory = initial;
        return d.ShowDialog() == true ? d.FolderName : null;
    }

    public static string[] Files(string title, string filter = "Tous les fichiers|*.*")
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = title, Multiselect = true, Filter = filter };
        return d.ShowDialog() == true ? d.FileNames : [];
    }

    public static string? File(string title, string filter)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = title, Multiselect = false, Filter = filter };
        return d.ShowDialog() == true ? d.FileName : null;
    }
}
