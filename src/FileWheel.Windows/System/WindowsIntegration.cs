using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using FileWheel.Core.Logging;
using static FileWheel.Windows.Native.NativeMethods;

namespace FileWheel.Windows.SystemIntegration;

/// <summary>Demarrage avec Windows (cle Run de l'utilisateur, sans droits administrateur).</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FileWheel";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string s && s.Length > 0;
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
            k.SetValue(ValueName, $"\"{exe}\" --arriere-plan");
            Log.Info("Démarrage automatique activé.");
        }
        else if (k.GetValue(ValueName) is not null)
        {
            k.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info("Démarrage automatique désactivé.");
        }
    }
}

/// <summary>Theme Windows : clair/sombre, couleur d'accentuation, version (Windows 11 ou non).</summary>
public static class WindowsTheme
{
    public static bool AppsUseDarkTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Theme de la barre des taches et de la zone de notification (« mode Windows »), distinct de celui des applications.</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    public static bool TransparencyEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch { return true; }
    }

    /// <summary>Couleur d'accentuation (ARGB) ou bleu Windows par defaut.</summary>
    public static uint AccentColor()
    {
        try
        {
            if (DwmGetColorizationColor(out var c, out _) == 0) return c | 0xFF000000;
        }
        catch { }
        return 0xFF0067C0;
    }

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Barre de titre sombre, coins arrondis et fond Mica (Windows 11) pour une fenetre classique.</summary>
    public static void StyleWindow(IntPtr hwnd, bool dark, bool mica)
    {
        try
        {
            int d = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));
            if (IsWindows11)
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                if (mica)
                {
                    int backdrop = 2; // DWMSBT_MAINWINDOW (Mica)
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                }
            }
        }
        catch (Exception ex) { Log.Debug("DWM : " + ex.Message); }
    }

    /// <summary>
    /// Flou d'arriere-plan derriere une fenetre sans bordure (SetWindowCompositionAttribute, Windows 10/11).
    /// API non documentee mais stable depuis Windows 10 1803 ; en cas d'echec la fenetre garde son fond opaque.
    /// </summary>
    public static bool EnableBlur(IntPtr hwnd, uint tintArgb)
    {
        try
        {
            var accent = new AccentPolicy { AccentState = AccentState.BlurBehind, GradientColor = tintArgb, AccentFlags = 2 };
            int size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { return false; }
    }
}

/// <summary>Instance unique : un second lancement demande a la premiere d'ouvrir ses parametres puis se termine.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly Thread? _listener;
    private volatile bool _disposed;

    public bool IsFirst { get; }

    public SingleInstance(string name, Action onSignal)
    {
        _mutex = new Mutex(true, @"Local\" + name + ".Mutex", out bool created);
        IsFirst = created;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Show");
        if (!IsFirst) { _signal.Set(); return; }
        _listener = new Thread(() =>
        {
            while (!_disposed)
            {
                if (_signal.WaitOne(1000) && !_disposed) { try { onSignal(); } catch { } }
            }
        }) { IsBackground = true, Name = "FileWheel.Instance" };
        _listener.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        try { if (IsFirst) _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
        _signal.Dispose();
    }
}

/// <summary>Detection des fenetres de l'application elle-meme (pour ne jamais ouvrir la roue sur ses propres fenetres).</summary>
public static class WindowStyles
{
    /// <summary>Fenetre outil, sans activation, au premier plan : la roue ne vole jamais le focus a l'Explorateur.</summary>
    public static void MakeOverlay(IntPtr hwnd)
    {
        long ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    public static void PlaceTopmost(IntPtr hwnd, int x, int y, int w, int h) =>
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);

    /// <summary>
    /// Amene une fenetre au premier plan alors que l'utilisateur interagissait avec l'Explorateur.
    /// Windows refuse normalement ce changement a une application d'arriere-plan ; on rattache
    /// brievement la file d'entree au thread actif (methode documentee d'AttachThreadInput).
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            ShowWindow(hwnd, 5); // SW_SHOW
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }

    public static void ClipToCircle(IntPtr hwnd, int diameter)
    {
        var rgn = CreateEllipticRgn(0, 0, diameter + 1, diameter + 1);
        SetWindowRgn(hwnd, rgn, true); // le systeme devient proprietaire de la region
    }
}
