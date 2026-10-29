namespace FileWheel.Core.Util;

/// <summary>
/// Emplacements utilises par FileWheel. Tout est dans le profil de l'utilisateur :
/// aucune ecriture dans Program Files ni dans les dossiers systeme.
///
/// - Config  : %APPDATA%\FileWheel            (parametres, historique, actions perso)
/// - Data    : %LOCALAPPDATA%\FileWheel        (journaux, moteurs telecharges, temporaires)
///
/// La variable d'environnement FILEWHEEL_HOME remplace les deux racines (tests, mode portable).
/// </summary>
public static class AppPaths
{
    private static string? _overrideRoot = Environment.GetEnvironmentVariable("FILEWHEEL_HOME");

    /// <summary>Redirige toutes les racines (utilise par les tests).</summary>
    public static void OverrideRoot(string? root) => _overrideRoot = root;

    public static string ConfigDir => Ensure(_overrideRoot is { Length: > 0 }
        ? Path.Combine(_overrideRoot, "config")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileWheel"));

    public static string DataDir => Ensure(_overrideRoot is { Length: > 0 }
        ? Path.Combine(_overrideRoot, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileWheel"));

    public static string LogsDir => Ensure(Path.Combine(DataDir, "logs"));
    public static string EnginesDir => Ensure(Path.Combine(DataDir, "engines"));
    public static string DownloadsDir => Ensure(Path.Combine(DataDir, "downloads"));

    /// <summary>Temporaires propres a FileWheel, nettoyes au demarrage selon les parametres.</summary>
    public static string TempDir => Ensure(Path.Combine(DataDir, "temp"));

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string HistoryFile => Path.Combine(ConfigDir, "history.json");
    public static string CustomActionsFile => Path.Combine(ConfigDir, "custom-actions.json");
    public static string PresetsFile => Path.Combine(ConfigDir, "presets.json");
    public static string EnginesFile => Path.Combine(ConfigDir, "engines.json");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
