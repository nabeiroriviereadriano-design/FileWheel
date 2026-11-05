using System.IO;
using System.Windows;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;

namespace FileWheel.App.Ui;

/// <summary>
/// Installation d'un moteur avec l'accord de l'utilisateur (boite de consentement : source, version, taille,
/// empreinte, licence), utilisee par les parametres et par les outils qui en ont besoin (detourage).
/// </summary>
public static class EngineInstaller
{
    /// <summary>Installe le moteur et ceux dont il depend. Renvoie vrai si tout est disponible a la fin.</summary>
    public static async Task<bool> InstallAsync(EngineDefinition def, DependencyManager deps, Window? owner)
    {
        foreach (var req in def.Requires)
        {
            if (deps.IsAvailable(req)) continue;
            var r = EngineCatalog.Get(req);
            if (!ConfirmDialog.Ask(r.Name, $"{def.Name} a besoin de « {r.Name} », absent de ce PC. L'installer d'abord ?\n\n{r.Purpose}", "Installer"))
                return false;
            if (!await InstallOneAsync(r, deps, owner)) return false;
        }
        return await InstallOneAsync(def, deps, owner);
    }

    private static async Task<bool> InstallOneAsync(EngineDefinition def, DependencyManager deps, Window? owner) => def.Method switch
    {
        InstallMethod.GitHubRelease or InstallMethod.Pinned => await DownloadAsync(def, deps, owner),
        InstallMethod.Winget => await WingetAsync(def, deps, owner),
        _ => false,
    };

    private static async Task<bool> DownloadAsync(EngineDefinition def, DependencyManager deps, Window? owner)
    {
        InstallOffer offer;
        try { offer = await deps.PrepareGitHubInstallAsync(def.Id, CancellationToken.None); }
        catch (Exception ex) { Ui.Message(ConversionManager.Translate(ex).Message, image: MessageBoxImage.Warning, owner: owner); return false; }
        if (!new EngineConsentDialog(offer).Run()) return false;
        var progress = new ProgressDialog($"Installation de {def.Name}");
        progress.Show();
        try
        {
            if (def.Id == EngineIds.ImageMagick && offer.AssetName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && !deps.IsAvailable(EngineIds.SevenZip))
                throw new ConversionException(ConversionErrorKind.EngineMissing, "Ce paquet ImageMagick est au format 7Z : installez d'abord le moteur 7-Zip (même page).");
            await deps.InstallAsync(offer, new Progress<ConversionProgress>(pr => progress.Update(pr.Fraction, pr.Stage)), progress.Token);
            progress.Close();
            Ui.Message($"{def.Name} est installé.", owner: owner);
            return true;
        }
        catch (OperationCanceledException) { progress.Close(); return false; }
        catch (Exception ex)
        {
            progress.Close();
            Ui.Message("Installation impossible : " + ConversionManager.Translate(ex).Message, image: MessageBoxImage.Error, owner: owner);
            return false;
        }
    }

    private static async Task<bool> WingetAsync(EngineDefinition def, DependencyManager deps, Window? owner)
    {
        if (!ConfirmDialog.Ask($"Installer {def.Name}", $"{def.Name} sera installé par winget (gestionnaire de paquets de Windows), depuis la source officielle « winget ». Une fenêtre d'installation ou une demande d'autorisation Windows peut apparaître.\n\nLicence : {def.License}", "Installer"))
            return false;
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(winget))
        {
            Ui.Message("winget est introuvable sur ce PC. Installez « App Installer » depuis le Microsoft Store, ou installez le logiciel depuis son site officiel puis indiquez son emplacement.", image: MessageBoxImage.Warning, owner: owner);
            return false;
        }
        var progress = new ProgressDialog($"Installation de {def.Name} (winget)");
        progress.Show();
        try
        {
            var r = await ProcessRunner.RunAsync(winget, DependencyManager.WingetArguments(def), progress.Token, timeout: TimeSpan.FromMinutes(30));
            progress.Close();
            deps.Refresh();
            bool ok = deps.IsAvailable(def.Id);
            Ui.Message(ok ? $"{def.Name} est installé." : $"winget a terminé (code {r.ExitCode}) mais {def.Name} n'a pas été trouvé. Indiquez son emplacement manuellement.", owner: owner);
            return ok;
        }
        catch (Exception ex)
        {
            progress.Close();
            Ui.Message(ConversionManager.Translate(ex).Message, image: MessageBoxImage.Error, owner: owner);
            return false;
        }
    }
}
