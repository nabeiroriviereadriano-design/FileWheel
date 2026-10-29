using System.Windows;
using System.Windows.Threading;
using FileWheel.Core.Logging;
using FileWheel.Core.Util;
using FileWheel.Windows.SystemIntegration;

namespace FileWheel.App;

public static class Program
{
    /// <summary>
    /// Arguments :
    ///   --arriere-plan     demarrage silencieux (lancement avec Windows)
    ///   --parametres       ouvre les parametres (ou les affiche dans l'instance deja lancee)
    ///   --quitter          demande a l'instance en cours de s'arreter (utilise par l'installeur et le desinstalleur)
    ///   --apres-installation  lance par FileWheel-Setup a la fin de l'installation ou de la mise a jour
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("FILEWHEEL_HOME") is { Length: > 0 } home) AppPaths.OverrideRoot(home);
        bool background = args.Any(a => a.Equals("--arriere-plan", StringComparison.OrdinalIgnoreCase));
        bool afterSetup = args.Any(a => a.Equals("--apres-installation", StringComparison.OrdinalIgnoreCase));

        if (args.Any(a => a.Equals("--quitter", StringComparison.OrdinalIgnoreCase)))
            return QuitRunningInstance();

        FileWheelApp? app = null;
        using var instance = new SingleInstance("FileWheel." + Environment.UserName, () => app?.BringToFront());
        if (!instance.IsFirst)
        {
            // L'instance deja lancee a ete prevenue (elle ouvre ses parametres).
            return 0;
        }
        using var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Exception non gérée (fatale)", e.ExceptionObject as Exception);
            Log.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Tâche en échec non observée", e.Exception);
            e.SetObserved();
        };

        try
        {
            app = new FileWheelApp(background, afterSetup);
            app.DispatcherUnhandledException += (_, e) =>
            {
                Log.Error("Erreur d'interface", e.Exception);
                e.Handled = true; // l'application resident reste en vie ; l'erreur est journalisee
                try { Ui.Ui.Message("Une erreur inattendue s'est produite :\n" + e.Exception.Message + "\n\nDétails dans le journal (menu de l'icône › Ouvrir le dossier des journaux).", image: MessageBoxImage.Error); } catch { }
            };
            var watcher = new Thread(() =>
            {
                try
                {
                    quitSignal.WaitOne();
                    app.Dispatcher.BeginInvoke(() => app.Shutdown());
                }
                catch { }
            }) { IsBackground = true, Name = "FileWheel.Quit" };
            watcher.Start();
            if (args.Any(a => a.Equals("--parametres", StringComparison.OrdinalIgnoreCase)))
                app.Startup += (_, _) => app.OpenSettings(null);
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Error("Démarrage impossible", ex);
            Log.Flush();
            MessageBox.Show("FileWheel n'a pas pu démarrer :\n" + ex.Message, "FileWheel", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static string QuitEventName => @"Local\FileWheel." + Environment.UserName + ".Quit";

    private static int QuitRunningInstance()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(QuitEventName, out var h)) return 0; // rien a arreter
            using (h) h.Set();
            return 0;
        }
        catch { return 1; }
    }
}
