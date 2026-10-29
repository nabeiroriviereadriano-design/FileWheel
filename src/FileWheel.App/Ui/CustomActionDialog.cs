using System.Windows;
using System.Windows.Controls;
using FileWheel.Conversion;
using FileWheel.Conversion.Core;
using FileWheel.Core.Actions;
using FileWheel.Core.Formats;
using FileWheel.Core.Wheel;

namespace FileWheel.App.Ui;

/// <summary>Edition d'une action personnalisee, avec validation et avertissements de securite.</summary>
public sealed class CustomActionDialog : DialogWindow
{
    private readonly CustomAction _a;
    private readonly ConversionEngine _engine;
    private readonly StackPanel _typePanel = new();
    private readonly TextBlock _errors;
    private readonly TextBox _name, _ext, _args, _program, _folder;
    private CustomActionType _type;
    private CustomActionPlacement _placement;
    private string _capKey = "";
    private readonly CheckBox _folders, _multiple, _single;

    private static readonly (string glyph, string label)[] GlyphChoices =
    [
        (Glyphs.Lightning, "Éclair"), (Glyphs.Star, "Étoile"), (Glyphs.Folder, "Dossier"), (Glyphs.Send, "Envoi"), (Glyphs.Image, "Image"),
        (Glyphs.Video, "Vidéo"), (Glyphs.Audio, "Audio"), (Glyphs.Document, "Document"), (Glyphs.Terminal, "Terminal"), (Glyphs.Settings, "Engrenage"),
    ];
    private string _glyph;

    public CustomActionDialog(CustomAction action, ConversionEngine engine) : base("Action personnalisée", 640)
    {
        _a = action;
        _engine = engine;
        _type = action.Type;
        _placement = action.Placement;
        _glyph = action.Glyph;
        _capKey = action.ConversionOptions.TryGetValue("__capability", out var k) ? k : "";

        _name = new TextBox { Text = action.Name, MinWidth = 300 };
        Body.Children.Add(Ui.Field("Nom", _name, labelWidth: 170));
        Body.Children.Add(Ui.Field("Icône", Ui.Combo(GlyphChoices.Select(g => (g.glyph, g.label)), _glyph, v => _glyph = v), labelWidth: 170));
        Body.Children.Add(Ui.Field("Type", Ui.Combo(
            [(CustomActionType.RunProgram, "Lancer un programme"), (CustomActionType.CopyToFolder, "Copier vers un dossier"), (CustomActionType.MoveToFolder, "Déplacer vers un dossier"), (CustomActionType.ConvertTo, "Convertir")],
            _type, v => { _type = v; RenderType(); }), labelWidth: 170));

        _program = new TextBox { Text = action.ProgramPath, MinWidth = 300 };
        _args = new TextBox { Text = action.Arguments, MinWidth = 300 };
        _folder = new TextBox { Text = action.Folder, MinWidth = 300 };
        Body.Children.Add(_typePanel);

        _ext = new TextBox { Text = string.Join(", ", action.Extensions), MinWidth = 300 };
        Body.Children.Add(Ui.Field("Extensions acceptées", _ext, "Séparées par des virgules, ex. jpg, png. Vide = toutes.", 170));
        _folders = new CheckBox { Content = "Accepter les dossiers", IsChecked = action.AllowFolders };
        _multiple = new CheckBox { Content = "Accepter plusieurs éléments", IsChecked = action.AllowMultiple };
        _single = new CheckBox { Content = "Un seul lancement avec tous les éléments (sinon un par élément)", IsChecked = action.SingleInvocation };
        Body.Children.Add(_folders);
        Body.Children.Add(_multiple);
        Body.Children.Add(_single);
        Body.Children.Add(Ui.Field("Emplacement dans la roue", Ui.Combo([(CustomActionPlacement.Submenu, "Plus › Mes actions"), (CustomActionPlacement.Root, "Roue principale (2 actions au plus)")], _placement, v => _placement = v), labelWidth: 170));
        _errors = Ui.Text("", Ui.Error, 12);
        Body.Children.Add(_errors);
        Buttons.Children.Add(Ui.Button("Enregistrer", Save, accent: true));
        AddButton("Annuler", false, isCancel: true);
        RenderType();
    }

    private void RenderType()
    {
        _typePanel.Children.Clear();
        switch (_type)
        {
            case CustomActionType.RunProgram:
                _typePanel.Children.Add(Ui.Field("Programme", Ui.Row(_program, Ui.Button("Parcourir…", () =>
                {
                    var f = Pickers.File("Programme à lancer", "Programmes|*.exe;*.com|Tous les fichiers|*.*");
                    if (f is not null) _program.Text = f;
                })), labelWidth: 170));
                _typePanel.Children.Add(Ui.Field("Arguments", _args, labelWidth: 170));
                _typePanel.Children.Add(Ui.Hint("Jetons : {path} chemin complet, {dir} dossier, {name} nom, {stem} nom sans extension, {ext} extension, {paths} tous les chemins. Chaque mot (ou groupe entre guillemets) est un argument distinct ; aucun shell n'interprète la ligne."));
                break;
            case CustomActionType.CopyToFolder:
            case CustomActionType.MoveToFolder:
                _typePanel.Children.Add(Ui.Field("Dossier cible", Ui.Row(_folder, Ui.Button("Parcourir…", () =>
                {
                    var f = Pickers.Folder("Dossier cible");
                    if (f is not null) _folder.Text = f;
                })), labelWidth: 170));
                _typePanel.Children.Add(Ui.Hint("En cas de nom déjà pris, l'élément est renommé « (2) » : rien n'est écrasé."));
                break;
            case CustomActionType.ConvertTo:
            {
                var caps = _engine.Registry.AllCapabilities.Where(c => !c.MultiInput && !c.OutputIsFolder)
                    .GroupBy(c => c.Key).Select(g => g.First())
                    .OrderBy(c => FormatCatalog.Get(c.To)?.Label).ThenBy(c => c.ProviderId)
                    .Select(c => (c.Key, $"{c.DisplayLabel}  (depuis {string.Join(", ", c.From.Take(4))}{(c.From.Count > 4 ? "…" : "")})"))
                    .ToList();
                if (_capKey.Length == 0 && caps.Count > 0) _capKey = caps[0].Key;
                _typePanel.Children.Add(Ui.Field("Conversion", Ui.Combo(caps, _capKey, v => _capKey = v, 360), labelWidth: 170));
                _typePanel.Children.Add(Ui.Hint("La conversion utilise les réglages par défaut du format (modifiables ensuite dans le convertisseur)."));
                break;
            }
        }
    }

    private void Save()
    {
        var draft = new CustomAction
        {
            Id = _a.Id, Name = _name.Text.Trim(), Glyph = _glyph, Type = _type, Placement = _placement, Order = _a.Order,
            Extensions = _ext.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim().TrimStart('.')).ToList(),
            AllowFolders = _folders.IsChecked == true, AllowMultiple = _multiple.IsChecked == true, SingleInvocation = _single.IsChecked == true,
            ProgramPath = _program.Text.Trim(), Arguments = _args.Text, Folder = _folder.Text.Trim(),
        };
        if (_type == CustomActionType.ConvertTo)
        {
            var cap = _engine.Registry.ByKey(_capKey);
            draft.TargetFormat = cap?.To ?? "";
            draft.ConversionOptions["__capability"] = _capKey;
        }
        var errors = draft.Validate();
        if (errors.Count > 0) { _errors.Text = string.Join("\n", errors); return; }
        var warnings = draft.Warnings();
        if (warnings.Count > 0 && !ConfirmDialog.Ask("Attention", string.Join("\n", warnings), "Enregistrer quand même")) return;
        if (_type == CustomActionType.RunProgram) draft.ProgramSha256 = CustomAction.ComputeSha256(draft.ProgramPath);

        _a.Name = draft.Name; _a.Glyph = draft.Glyph; _a.Type = draft.Type; _a.Placement = draft.Placement;
        _a.Extensions = draft.Extensions; _a.AllowFolders = draft.AllowFolders; _a.AllowMultiple = draft.AllowMultiple;
        _a.SingleInvocation = draft.SingleInvocation; _a.ProgramPath = draft.ProgramPath; _a.ProgramSha256 = draft.ProgramSha256;
        _a.Arguments = draft.Arguments; _a.Folder = draft.Folder; _a.TargetFormat = draft.TargetFormat; _a.ConversionOptions = draft.ConversionOptions;
        DialogResult = true;
    }
}
