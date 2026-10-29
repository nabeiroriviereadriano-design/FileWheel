using System.Diagnostics;
using System.Windows.Automation;
using FileWheel.Core.Logging;

namespace FileWheel.Windows.Input;

/// <summary>
/// Verifie, via UI Automation, que l'element sous le curseur au moment de l'appui est un fichier
/// ou un dossier (element de liste ou d'arborescence de l'Explorateur), et non une zone vide :
/// une selection rectangulaire avec Alt ne doit pas ouvrir la roue.
/// L'appel se fait hors du thread des crochets et sa duree est bornee.
/// </summary>
public sealed class ItemProbe
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(300);

    public void CheckAsync(int x, int y, Action<bool> result)
    {
        _ = Task.Run(async () =>
        {
            if (!await _gate.WaitAsync(0)) return; // une verification a la fois
            try
            {
                var task = Task.Run(() => IsFileItemAt(x, y));
                // Pas de reponse claire (null) ou delai depasse : on ne tranche pas, le detecteur applique sa politique.
                if (await Task.WhenAny(task, Task.Delay(Timeout)) == task && task.Result is bool r) result(r);
                // Delai depasse : aucune reponse => le detecteur applique sa politique (tolerante par defaut)
            }
            catch (Exception ex) { Log.Debug("Vérification UI Automation : " + ex.Message); }
            finally { _gate.Release(); }
        });
    }

    /// <summary>
    /// true : element de fichier/dossier ; false : fond d'une liste (zone vide, selection rectangulaire) ;
    /// null : structure inconnue (nouvelle version de l'Explorateur...) : on ne bloque pas.
    /// </summary>
    private static bool? IsFileItemAt(int x, int y)
    {
        var sw = Stopwatch.StartNew();
        var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
        var walker = TreeWalker.ControlViewWalker;
        for (int depth = 0; el is not null && depth < 6; depth++)
        {
            var type = el.Current.ControlType;
            if (type == ControlType.ListItem || type == ControlType.TreeItem || type == ControlType.DataItem)
            {
                Log.Debug($"UIA : élément de type {type.ProgrammaticName} en {sw.ElapsedMilliseconds} ms");
                return true;
            }
            if (type == ControlType.List || type == ControlType.Tree || type == ControlType.DataGrid)
            {
                Log.Debug($"UIA : fond de liste ({type.ProgrammaticName}) en {sw.ElapsedMilliseconds} ms");
                return false;
            }
            if (type == ControlType.Window) break;
            el = walker.GetParent(el);
        }
        Log.Debug($"UIA : structure non reconnue en {sw.ElapsedMilliseconds} ms");
        return null;
    }
}
