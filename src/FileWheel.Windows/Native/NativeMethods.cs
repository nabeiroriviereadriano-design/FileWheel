using System.Runtime.InteropServices;
using System.Text;

namespace FileWheel.Windows.Native;

/// <summary>Declarations Win32 utilisees par FileWheel (user32, kernel32, shell32, dwmapi, shcore).</summary>
internal static unsafe class NativeMethods
{
    // ------------------------------------------------------------------ crochets bas niveau
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204;
    public const int WM_TIMER = 0x0113, WM_QUIT = 0x0012, WM_APP = 0x8000, WM_USER = 0x0400;
    public const int LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    // ------------------------------------------------------------------ injection (masquage du menu Alt)
    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ------------------------------------------------------------------ fenetres
    public const uint GA_PARENT = 1, GA_ROOT = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT pt);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_NOZORDER = 0x0004, SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);

    // ------------------------------------------------------------------ ecrans et DPI
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    // ------------------------------------------------------------------ zone de notification
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 1, NIIF_WARNING = 2, NIIF_ERROR = 3, NIIF_NOSOUND = 0x10;
    public const int NIN_SELECT = WM_USER, NIN_KEYSELECT = WM_USER + 1, WM_CONTEXTMENU = 0x007B, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string lpString);

    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10, LR_DEFAULTSIZE = 0x40;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[]? bits);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    /// <summary>Cree une icone a partir d'une ressource d'icone (DIB ou PNG depuis Windows Vista).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(byte[] presbits, uint dwResSize, bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);

    // ------------------------------------------------------------------ menus natifs
    public const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_POPUP = 0x10, MF_CHECKED = 0x8, MF_GRAYED = 0x1, MF_DEFAULT = 0x1000;
    public const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20, TPM_NONOTIFY = 0x80;

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // ------------------------------------------------------------------ DWM (verre, coins, theme sombre)
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

    [DllImport("dwmapi.dll")]
    public static extern int DwmIsCompositionEnabled(out bool enabled);

    public enum AccentState { Disabled = 0, Gradient = 1, TransparentGradient = 2, BlurBehind = 3, AcrylicBlurBehind = 4 }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy { public AccentState AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    // ------------------------------------------------------------------ Shell
    public const uint FO_DELETE = 3;
    public const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    public const uint SEE_MASK_INVOKEIDLIST = 0x0C, SEE_MASK_NOASYNC = 0x100, SEE_MASK_FLAG_NO_UI = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO lpExecInfo);

    public const int SHOP_FILEPATH = 2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SHObjectProperties(IntPtr hwnd, int shopObjectType, string pszObjectName, string? pszPropertyPage);

    [DllImport("shell32.dll")]
    public static extern int SHMultiFileProperties([MarshalAs(UnmanagedType.Interface)] object pdtobj, int flags);

    public const int OAIF_ALLOW_REGISTRATION = 1, OAIF_REGISTER_EXT = 2, OAIF_EXEC = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENASINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pcszFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
        public int oaifInFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    [DllImport("shell32.dll")]
    public static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("shell32.dll")]
    public static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    // ------------------------------------------------------------------ divers
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateMutexW(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    public static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
