using System.Runtime.InteropServices;
using System.Windows.Interop;
using FileWheel.Core.Logging;
using static FileWheel.Windows.Native.NativeMethods;

namespace FileWheel.Windows.Tray;

/// <summary>Entree du menu de l'icone de notification.</summary>
public sealed class TrayMenuItem
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public bool Checked { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsDefault { get; init; }
    public bool Separator { get; init; }
    public List<TrayMenuItem> Children { get; } = new();

    public static TrayMenuItem Sep() => new() { Separator = true };
}

/// <summary>
/// Icone dans la zone de notification (Shell_NotifyIcon), avec menu natif et bulles d'information.
/// Recree automatiquement l'icone si l'Explorateur redemarre (message « TaskbarCreated »).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 10;
    private readonly HwndSource _window;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tooltip;
    private bool _added;

    public Func<List<TrayMenuItem>>? MenuProvider { get; set; }
    public event Action<string>? MenuCommand;
    public event Action? Activated;
    public event Action? BalloonClicked;

    /// <summary>Taille d'icone attendue par la zone de notification (16 px a 100 %, 20 a 125 %, 32 a 200 %...).</summary>
    public static int IconSize
    {
        get
        {
            try { return Math.Max(16, GetSystemMetricsForDpi(49, GetDpiForSystem())); } // SM_CXSMICON
            catch { return Math.Max(16, GetSystemMetrics(49)); }
        }
    }

    /// <summary>
    /// Remplace l'icone affichee. L'icone transmise appartient desormais a TrayIcon (detruite au prochain
    /// remplacement ou a la fermeture). Sans effet si <paramref name="hIcon"/> est nul.
    /// </summary>
    public void SetIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return;
        var old = _icon;
        _icon = hIcon;
        var d = Data(NIF_ICON);
        Shell_NotifyIconW(NIM_MODIFY, ref d);
        if (old != IntPtr.Zero && old != hIcon) DestroyIcon(old);
    }

    /// <summary>
    /// Icone Windows (HICON) a partir d'une image : PNG d'abord (Windows Vista et suivants), sinon pixels
    /// BGRA 32 bits avec transparence (<paramref name="bgra"/>, lignes de haut en bas).
    /// </summary>
    public static IntPtr CreateIcon(byte[] png, byte[] bgra, int size)
    {
        var h = png.Length > 0 ? CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, size, size, 0) : IntPtr.Zero;
        if (h != IntPtr.Zero) return h;
        var color = CreateBitmap(size, size, 1, 32, bgra);
        var mask = CreateBitmap(size, size, 1, 1, new byte[((size + 15) / 16) * 2 * size]);
        try
        {
            var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
            h = CreateIconIndirect(ref info);
            if (h == IntPtr.Zero) Log.Warn($"Création de l'icône de notification impossible (erreur {Marshal.GetLastWin32Error()}).");
            return h;
        }
        finally
        {
            if (color != IntPtr.Zero) DeleteObject(color);
            if (mask != IntPtr.Zero) DeleteObject(mask);
        }
    }

    public TrayIcon(string iconPath, string tooltip)
    {
        _tooltip = tooltip;
        var p = new HwndSourceParameters("FileWheelTray") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        int size = IconSize;
        _icon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        if (_icon == IntPtr.Zero) Log.Warn($"Icône de notification introuvable : {iconPath}");
        Add();
    }

    public IntPtr Handle => _window.Handle;

    private NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _window.Handle, uID = 1, uFlags = flags, uCallbackMessage = CallbackMessage,
        hIcon = _icon, szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip, szInfo = "", szInfoTitle = "",
    };

    private void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref d);
        d.uVersion = 4; // NOTIFYICON_VERSION_4
        Shell_NotifyIconW(NIM_SETVERSION, ref d);
        if (!_added) Log.Warn("Ajout de l'icône de notification impossible (Explorateur indisponible ?).");
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        var d = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    }

    /// <summary>Bulle d'information (fin de conversion, erreur...).</summary>
    public void ShowBalloon(string title, string text, bool error = false)
    {
        var d = Data(NIF_INFO);
        d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        d.szInfo = text.Length > 255 ? text[..252] + "…" : text;
        d.dwInfoFlags = (error ? NIIF_ERROR : NIIF_INFO) | NIIF_NOSOUND;
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_taskbarCreated) { Add(); return IntPtr.Zero; }
        if (msg != CallbackMessage) return IntPtr.Zero;
        int ev = (int)((long)lParam & 0xFFFF);
        switch (ev)
        {
            case WM_CONTEXTMENU:
            case WM_RBUTTONUP:
                ShowMenu();
                handled = true;
                break;
            case NIN_SELECT:
            case NIN_KEYSELECT:
            case WM_LBUTTONDBLCLK:
                Activated?.Invoke();
                handled = true;
                break;
            case WM_USER + 5: // NIN_BALLOONUSERCLICK
                BalloonClicked?.Invoke();
                break;
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;
        var ids = new Dictionary<int, string>();
        var menu = Build(items, ids);
        try
        {
            GetCursorPos(out var pt);
            SetForegroundWindow(_window.Handle); // indispensable pour que le menu se ferme en cliquant ailleurs
            int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, pt.X, pt.Y, _window.Handle, IntPtr.Zero);
            PostMessageW(_window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            if (cmd > 0 && ids.TryGetValue(cmd, out var id)) MenuCommand?.Invoke(id);
        }
        finally { DestroyMenu(menu); }
    }

    private static IntPtr Build(List<TrayMenuItem> items, Dictionary<int, string> ids)
    {
        var menu = CreatePopupMenu();
        foreach (var it in items)
        {
            if (it.Separator) { AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null); continue; }
            uint flags = MF_STRING | (it.Checked ? MF_CHECKED : 0) | (it.Enabled ? 0 : MF_GRAYED);
            if (it.Children.Count > 0)
            {
                var sub = Build(it.Children, ids);
                AppendMenuW(menu, flags | MF_POPUP, (UIntPtr)(ulong)sub, it.Text);
                continue;
            }
            int id = ids.Count + 1;
            ids[id] = it.Id;
            AppendMenuW(menu, flags | (it.IsDefault ? MF_DEFAULT : 0), (UIntPtr)(uint)id, it.Text);
        }
        return menu;
    }

    public void Dispose()
    {
        if (_added)
        {
            var d = Data(0);
            Shell_NotifyIconW(NIM_DELETE, ref d);
            _added = false;
        }
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
        _window.Dispose();
    }
}
