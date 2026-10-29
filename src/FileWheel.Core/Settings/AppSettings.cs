using FileWheel.Core.Input;
using FileWheel.Core.Util;

namespace FileWheel.Core.Settings;

public enum ConflictPolicy
{
    /// <summary>Ajoute « (2) », « (3) »... au nom : comportement par defaut, rien n'est ecrase.</summary>
    Rename,
    /// <summary>Ignore le fichier si la destination existe.</summary>
    Skip,
    /// <summary>Demande a chaque conflit (operations sur fichiers).</summary>
    Ask,
    /// <summary>Remplace la destination. Jamais par defaut, jamais le fichier source lui-meme.</summary>
    Overwrite,
}

public enum TaskPriority { Normal, BelowNormal, Idle }

public enum ThemeMode { System, Dark, Light }

public sealed class TriggerSettings
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public int Key { get; set; }
    public int DelayMs { get; set; } = 90;
    public int MinDistancePx { get; set; } = 12;
    public bool OnDesktop { get; set; } = true;
    public bool InExplorer { get; set; } = true;
    public bool AllowSingle { get; set; } = true;
    public bool AllowMultiple { get; set; } = true;
    public bool CloseOnChordRelease { get; set; }
    public ItemCheckPolicy ItemCheck { get; set; } = ItemCheckPolicy.Lenient;

    public ActivationChord Chord => new(Ctrl, Alt, Shift, Win, Key);

    public void SetChord(ActivationChord c)
    {
        Ctrl = c.Ctrl; Alt = c.Alt; Shift = c.Shift; Win = c.Win; Key = c.Key;
    }

    public GestureOptions ToGestureOptions() => new()
    {
        Chord = Chord.IsValid ? Chord : ActivationChord.Default,
        DelayMs = DelayMs,
        MinDistancePx = MinDistancePx,
        OnDesktop = OnDesktop,
        InExplorer = InExplorer,
        CloseOnChordRelease = CloseOnChordRelease,
        ItemCheck = ItemCheck,
    };
}

/// <summary>Organisation de la roue.</summary>
public enum WheelMenuLayout
{
    /// <summary>Formats cibles et outils directement dans la roue ; actions de fichier dans « Fichier ».</summary>
    Express,
    /// <summary>Disposition d'origine : Convertir, Copier, Deplacer, Ouvrir avec, Archive, Renommer, Corbeille, Plus.</summary>
    Classic,
}

public sealed class WheelAppearance
{
    public WheelMenuLayout Layout { get; set; } = WheelMenuLayout.Express;
    /// <summary>Libelles courts sous les icones des secteurs.</summary>
    public bool ShowCaptions { get; set; } = true;
    /// <summary>Diametre de la roue en pixels independants (96 ppp).</summary>
    public int DiameterDip { get; set; } = 320;
    public int OffsetXDip { get; set; }
    public int OffsetYDip { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    /// <summary>Flou de l'arriere-plan (verre depoli). Experimental : desactive par defaut.</summary>
    public bool Blur { get; set; }
    public bool Animations { get; set; } = true;
    /// <summary>Opacite du fond de la roue (0.55 a 1).</summary>
    public double Opacity { get; set; } = 0.88;
    /// <summary>Delai de survol avant d'ouvrir un sous-menu pendant le glisser.</summary>
    public int SubmenuDwellMs { get; set; } = 380;
    public bool ConfirmRecycle { get; set; }
}

public sealed class ConversionSettings
{
    /// <summary>Dossier de sortie par defaut ; vide = a cote du fichier source.</summary>
    public string DefaultOutputDir { get; set; } = "";
    public string Suffix { get; set; } = "";
    public ConflictPolicy Conflicts { get; set; } = ConflictPolicy.Rename;
    public int MaxConcurrent { get; set; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
    public TaskPriority Priority { get; set; } = TaskPriority.BelowNormal;
    /// <summary>Taille maximale d'un fichier source (Mio, 0 = illimite).</summary>
    public long MaxInputMiB { get; set; }
    /// <summary>Espace libre minimal a conserver sur le disque de destination (Mio).</summary>
    public long MinFreeSpaceMiB { get; set; } = 512;
    public int DefaultImageQuality { get; set; } = 90;
    public bool HistoryEnabled { get; set; } = true;
    public int HistoryMaxEntries { get; set; } = 2000;
    /// <summary>Supprime les temporaires plus vieux que N heures au demarrage.</summary>
    public int TempMaxAgeHours { get; set; } = 24;
    /// <summary>Taille totale maximale d'une extraction d'archive (Gio).</summary>
    public long ExtractMaxGiB { get; set; } = 20;
    public int ExtractMaxEntries { get; set; } = 200_000;
    /// <summary>Rapport de compression au-dela duquel une archive est consideree comme une bombe.</summary>
    public int ExtractMaxRatio { get; set; } = 250;
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool WheelEnabled { get; set; } = true;
    /// <summary>null = l'utilisateur n'a pas encore ete interroge (premier lancement).</summary>
    public bool? StartWithWindows { get; set; }
    public TriggerSettings Trigger { get; set; } = new();
    public WheelAppearance Wheel { get; set; } = new();
    public ConversionSettings Conversion { get; set; } = new();
    /// <summary>Dossiers proposes dans « Envoyer vers » / « Deplacer vers ».</summary>
    public List<string> SendToFolders { get; set; } = new();
    public List<string> Favorites { get; set; } = new();
    public string? LastMoveFolder { get; set; }

    /// <summary>Corrige les valeurs hors bornes (fichier edite a la main, ancienne version).</summary>
    public AppSettings Normalize()
    {
        Trigger ??= new();
        Wheel ??= new();
        Conversion ??= new();
        SendToFolders ??= new();
        Favorites ??= new();
        if (!Trigger.Chord.IsValid) Trigger.SetChord(ActivationChord.Default);
        Trigger.DelayMs = Math.Clamp(Trigger.DelayMs, 0, 2000);
        Trigger.MinDistancePx = Math.Clamp(Trigger.MinDistancePx, 2, 200);
        Wheel.DiameterDip = Math.Clamp(Wheel.DiameterDip, 200, 600);
        Wheel.OffsetXDip = Math.Clamp(Wheel.OffsetXDip, -300, 300);
        Wheel.OffsetYDip = Math.Clamp(Wheel.OffsetYDip, -300, 300);
        Wheel.Opacity = Math.Clamp(Wheel.Opacity, 0.55, 1.0);
        Wheel.SubmenuDwellMs = Math.Clamp(Wheel.SubmenuDwellMs, 150, 1500);
        Conversion.MaxConcurrent = Math.Clamp(Conversion.MaxConcurrent, 1, 8);
        Conversion.DefaultImageQuality = Math.Clamp(Conversion.DefaultImageQuality, 1, 100);
        Conversion.HistoryMaxEntries = Math.Clamp(Conversion.HistoryMaxEntries, 10, 100_000);
        Conversion.MinFreeSpaceMiB = Math.Clamp(Conversion.MinFreeSpaceMiB, 0, 1_000_000);
        Conversion.ExtractMaxGiB = Math.Clamp(Conversion.ExtractMaxGiB, 1, 10_000);
        Conversion.ExtractMaxEntries = Math.Clamp(Conversion.ExtractMaxEntries, 100, 10_000_000);
        Conversion.ExtractMaxRatio = Math.Clamp(Conversion.ExtractMaxRatio, 10, 100_000);
        Conversion.TempMaxAgeHours = Math.Clamp(Conversion.TempMaxAgeHours, 1, 24 * 30);
        Conversion.Suffix ??= "";
        Conversion.DefaultOutputDir ??= "";
        Conversion.Suffix = FileOperations.PathSafety.SanitizeFileName(Conversion.Suffix, allowEmpty: true);
        // Jamais de politique « Ecraser » silencieuse pour les conversions par defaut d'une ancienne version.
        SendToFolders = SendToFolders.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        Favorites = Favorites.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        return this;
    }
}

/// <summary>Chargement / enregistrement des parametres (%APPDATA%\FileWheel\settings.json).</summary>
public static class SettingsStore
{
    private static readonly object Gate = new();

    public static AppSettings Load() => JsonStore.Load(AppPaths.SettingsFile, () => new AppSettings()).Normalize();

    public static void Save(AppSettings s)
    {
        lock (Gate) JsonStore.Save(AppPaths.SettingsFile, s.Normalize());
    }
}
