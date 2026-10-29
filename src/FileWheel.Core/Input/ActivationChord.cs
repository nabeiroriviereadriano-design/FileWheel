namespace FileWheel.Core.Input;

/// <summary>Codes de touches virtuelles Windows utilises par FileWheel.</summary>
public static class VK
{
    public const int Back = 0x08, Tab = 0x09, Return = 0x0D, Shift = 0x10, Control = 0x11, Menu = 0x12,
        Pause = 0x13, Capital = 0x14, Escape = 0x1B, Space = 0x20, LWin = 0x5B, RWin = 0x5C, Apps = 0x5D,
        F1 = 0x70, F24 = 0x87, LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3,
        LMenu = 0xA4, RMenu = 0xA5, A = 0x41, Z = 0x5A, D0 = 0x30, D9 = 0x39;

    /// <summary>Touche non attribuee, envoyee pour empecher l'activation du menu (Alt) ou de Demarrer (Win).</summary>
    public const int MaskKey = 0xE8;

    public static bool IsCtrl(int vk) => vk is Control or LControl or RControl;
    public static bool IsAlt(int vk) => vk is Menu or LMenu or RMenu;
    public static bool IsShift(int vk) => vk is Shift or LShift or RShift;
    public static bool IsWin(int vk) => vk is LWin or RWin;
    public static bool IsModifier(int vk) => IsCtrl(vk) || IsAlt(vk) || IsShift(vk) || IsWin(vk);

    /// <summary>
    /// Touche « reelle » susceptible de faire partie d'un raccourci (lettres, chiffres, F1-F24, Tab, Espace,
    /// Entree, fleches, Suppr, pave numerique...). Les codes synthetiques ou non attribues que Windows,
    /// les pilotes de clavier ou d'autres logiciels emettent (0x07, 0xE8, 0xFF, VK_PACKET...) n'en font pas
    /// partie : ils ne doivent jamais annuler le geste.
    /// </summary>
    public static bool IsShortcutKey(int vk) => vk is
        Back or Tab or Return or Escape or Space
        or >= 0x21 and <= 0x2E      // Page prec./suiv., Fin, Debut, fleches, Inser, Suppr...
        or >= D0 and <= D9 or >= A and <= Z
        or >= 0x60 and <= 0x6F      // pave numerique
        or >= F1 and <= F24
        or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2; // ponctuation (selon le clavier)

    public static string Name(int vk) => vk switch
    {
        Space => "Espace",
        Tab => "Tab",
        Capital => "Verr. Maj",
        Apps => "Menu",
        Pause => "Pause",
        >= F1 and <= F24 => "F" + (vk - F1 + 1),
        >= A and <= Z => ((char)vk).ToString(),
        >= D0 and <= D9 => ((char)vk).ToString(),
        _ => $"0x{vk:X2}",
    };
}

/// <summary>
/// Combinaison d'activation : modificateurs exacts + une touche facultative.
/// La correspondance est exacte (Ctrl+Alt ne declenche pas une combinaison « Alt »),
/// ce qui evite les confusions avec AltGr (= Ctrl gauche + Alt droite) et les raccourcis Windows.
/// </summary>
public sealed record ActivationChord(bool Ctrl, bool Alt, bool Shift, bool Win, int Key = 0)
{
    public static readonly ActivationChord Default = new(false, true, false, false);

    /// <summary>Combinaisons proposees dans l'interface. D'autres peuvent etre saisies.</summary>
    public static readonly IReadOnlyList<ActivationChord> Presets =
    [
        new(false, true, false, false),
        new(true, true, false, false),
        new(false, true, true, false),
        new(true, false, true, false),
        new(false, false, false, true),
        new(false, false, false, false, VK.Capital),
        new(false, false, false, false, VK.Space),
    ];

    public bool IsEmpty => !Ctrl && !Alt && !Shift && !Win && Key == 0;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Maj");
        if (Win) parts.Add("Win");
        if (Key != 0) parts.Add(VK.Name(Key));
        return parts.Count == 0 ? "(aucune)" : string.Join(" + ", parts);
    }

    /// <summary>
    /// Avertissements a afficher a l'utilisateur pour cette combinaison.
    /// Ctrl et Maj changent l'effet du glisser natif (copier / deplacer) : c'est permis,
    /// mais l'utilisateur doit le savoir.
    /// </summary>
    public IReadOnlyList<string> Warnings()
    {
        var w = new List<string>();
        if (IsEmpty) w.Add("Aucune touche : la roue apparaîtrait à chaque glisser. Combinaison refusée.");
        if (Ctrl && !Alt && !Shift && !Win && Key == 0)
            w.Add("Ctrl seul force la copie pendant un glisser natif ; préférez Alt.");
        if (Shift && !Ctrl && !Alt && !Win && Key == 0)
            w.Add("Maj seule force le déplacement pendant un glisser natif ; préférez Alt.");
        if (Ctrl && Alt)
            w.Add("Ctrl + Alt équivaut à AltGr sur les claviers français : conflit possible avec la saisie.");
        if (Key == VK.Space)
            w.Add("Espace peut être tapé dans un champ de texte si le glisser part d'une zone de saisie.");
        if (Key == VK.Tab || Key == VK.Escape || Key == VK.Return)
            w.Add("Cette touche a un rôle pendant le glisser-déposer natif ; combinaison déconseillée.");
        return w;
    }

    public bool IsValid => !IsEmpty && Key is not (VK.Escape or VK.Return or VK.Tab);
}
