namespace FileWheel.Core.Wheel;

/// <summary>Glyphes vectoriels (police Segoe Fluent Icons / Segoe MDL2 Assets, presente sur Windows 10 et 11).</summary>
public static class Glyphs
{
    public const string Convert = "", Copy = "", MoveTo = "", Open = "", OpenWith = "",
        Rename = "", Archive = "", Extract = "", Share = "", Send = "", Folder = "",
        Terminal = "", Info = "", Link = "", Tag = "", Recycle = "", DeleteForever = "",
        Favorite = "", Add = "", More = "", Back = "", Cancel = "", Settings = "",
        Image = "", Video = "", Audio = "", Document = "", Data = "", Book = "",
        Model3D = "", Font = "", Mail = "", Calendar = "", Contact = "", Check = "",
        Resize = "", Lightning = "", List = "", Shortcut = "", Path = "", Star = "",
        Crop = "\uE7A8", Background = "\uE790", Compress = "\uE73F", Annotate = "\uE70F", Metadata = "\uE946";
}

/// <summary>
/// Un secteur de la roue. Un element a enfants est une branche (sous-menu).
/// <see cref="Command"/> identifie l'action executee par l'application (voir ActionIds).
/// </summary>
public sealed class WheelItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Glyph { get; init; } = Glyphs.More;
    /// <summary>Libelle court affiche dans le secteur (majuscules), sous l'icone ou a sa place.</summary>
    public string? Caption { get; init; }
    /// <summary>Le secteur affiche son libelle court en gros a la place de l'icone (formats : DOCX, JPG...).</summary>
    public bool TextFace { get; init; }
    public string Tooltip { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    /// <summary>Action irreversible : confirmation obligatoire, jamais declenchee par un simple survol.</summary>
    public bool Destructive { get; init; }
    public string Command { get; init; } = "";
    /// <summary>Donnees de la commande (format cible, dossier, identifiant d'action perso...).</summary>
    public string? Argument { get; init; }
    public List<WheelItem> Children { get; } = new();

    public bool IsBranch => Children.Count > 0;

    /// <summary>Ajoute des enfants (construction fluide).</summary>
    public WheelItem WithChildren(IEnumerable<WheelItem> children)
    {
        Children.AddRange(children);
        return this;
    }

    public static WheelItem Disabled(string id, string label, string glyph, string reason) =>
        new() { Id = id, Label = label, Glyph = glyph, Enabled = false, DisabledReason = reason };

    public override string ToString() => IsBranch ? $"{Label} ▸ ({Children.Count})" : Label;
}
