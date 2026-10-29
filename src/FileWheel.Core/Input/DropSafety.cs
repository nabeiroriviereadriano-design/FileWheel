namespace FileWheel.Core.Input;

/// <summary>Valeurs DROPEFFECT d'OLE.</summary>
[Flags]
public enum OleDropEffect { None = 0, Copy = 1, Move = 2, Link = 4 }

/// <summary>
/// Choix de l'effet renvoye a la source du glisser quand on depose SUR la roue.
///
/// Point de securite essentiel : si la cible repond « Deplacer », l'Explorateur source
/// peut supprimer lui-meme les fichiers d'origine apres le depot (deplacement optimise).
/// La roue ne repond donc JAMAIS « Deplacer » : elle repond « Copier » (la source ne touche
/// a rien), ou « Lien » si la source n'autorise que cela. Quand l'utilisateur choisit
/// « Deplacer vers... » dans la roue, c'est FileWheel qui deplace, apres verification.
/// </summary>
public static class DropSafety
{
    public static OleDropEffect ChooseEffect(OleDropEffect allowedBySource)
    {
        if (allowedBySource.HasFlag(OleDropEffect.Copy)) return OleDropEffect.Copy;
        if (allowedBySource.HasFlag(OleDropEffect.Link)) return OleDropEffect.Link;
        return OleDropEffect.None;
    }
}
