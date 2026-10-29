using static FileWheel.Tests.T;

namespace FileWheel.Tests;

/// <summary>
/// Parties qui ne peuvent s'executer que sous Windows (crochets, glisser-deposer OLE, Shell, WPF, WIC).
/// Elles sont compilees ici contre les assemblees de reference, mais leur comportement doit etre verifie
/// sur un vrai Windows (liste de controle : docs/VERIFICATION-WINDOWS.md). Elles figurent dans le rapport
/// pour qu'aucune case ne soit cochee a tort.
/// </summary>
public static class WindowsOnlyTests
{
    public static async Task RunAll()
    {
        Suite("Partie Windows — non exécutable dans cet environnement");
        string[] items =
        [
            "Crochets clavier/souris réels (WH_MOUSE_LL / WH_KEYBOARD_LL) et masquage du menu Alt",
            "Roue en cible de dépôt OLE au-dessus de l'Explorateur et du Bureau (CF_HDROP, effet Copier)",
            "Glisser-déposer natif intact quand le fichier est lâché hors de la roue",
            "Corbeille, Propriétés, Ouvrir avec, Partager, raccourci .lnk (API Shell)",
            "Icône de la zone de notification, bulles, démarrage avec Windows (clé Run)",
            "Fenêtres WPF : thème hybride clair/sombre, DPI par écran, lecteur d'écran",
            "Conversions par les codecs Windows (WIC)",
            "Installateur FileWheel-Setup.exe sur un vrai Windows (installer, mettre à jour, désinstaller)",
        ];
        foreach (var item in items)
            await Run(item, () => Skip("nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici)"));
    }
}
