using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FileWheel.Core.FileOperations;
using FileWheel.Core.Logging;
using static FileWheel.Windows.Native.NativeMethods;

namespace FileWheel.Windows.Shell;

/// <summary>Actions qui passent par le Shell Windows : corbeille, proprietes, « Ouvrir avec », partage, raccourcis...</summary>
public static class ShellOps
{
    // ------------------------------------------------------------------ Corbeille

    /// <summary>
    /// Place dans la Corbeille. Si un element ne peut pas y aller (lecteur reseau, Corbeille desactivee),
    /// Windows affiche son propre avertissement (FOF_WANTNUKEWARNING) au lieu de supprimer en silence.
    /// </summary>
    public static FileOpResult Recycle(IReadOnlyList<string> paths, IntPtr owner, bool confirm)
    {
        var r = new FileOpResult();
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToList();
        foreach (var missing in paths.Except(existing)) r.Errors.Add((missing, "Élément introuvable."));
        if (existing.Count == 0) return r;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', existing) + "\0\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING | (confirm ? 0 : FOF_NOCONFIRMATION)),
        };
        int rc = SHFileOperationW(ref op);
        foreach (var p in existing)
        {
            if (File.Exists(p) || Directory.Exists(p))
                r.Errors.Add((p, op.fAnyOperationsAborted ? "Opération annulée." : $"Non placé dans la Corbeille (code {rc})."));
            else r.Done++;
        }
        r.Cancelled = op.fAnyOperationsAborted;
        Log.Info($"Corbeille : {r.Summary("placé(s) dans la Corbeille")}");
        return r;
    }

    // ------------------------------------------------------------------ Ouvrir, Ouvrir avec, Proprietes

    public static void Open(string path, IntPtr owner, string? verb = null)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), hwnd = owner, lpVerb = verb, lpFile = path, nShow = 1,
            lpDirectory = Path.GetDirectoryName(path), fMask = SEE_MASK_NOASYNC,
        };
        if (!ShellExecuteExW(ref info)) throw new IOException($"Impossible d'ouvrir « {Path.GetFileName(path)} » (erreur {Marshal.GetLastWin32Error()}).");
    }

    public static void OpenWith(string path, IntPtr owner)
    {
        var info = new OPENASINFO { pcszFile = path, oaifInFlags = OAIF_EXEC | OAIF_ALLOW_REGISTRATION };
        int hr = SHOpenWithDialog(owner, ref info);
        // Annulation par l'utilisateur : HRESULT_FROM_WIN32(ERROR_CANCELLED)
        if (hr < 0 && hr != unchecked((int)0x800704C7)) throw new IOException($"« Ouvrir avec » indisponible (0x{hr:X8}).");
    }

    public static void Properties(IReadOnlyList<string> paths, IntPtr owner)
    {
        if (paths.Count == 1) { SHObjectProperties(owner, SHOP_FILEPATH, paths[0], null); return; }
        var data = DataObjectFor(paths, owner);
        if (data is not null) SHMultiFileProperties(data, 0);
        else SHObjectProperties(owner, SHOP_FILEPATH, paths[0], null);
    }

    /// <summary>Ouvre le dossier parent et selectionne les elements (Explorateur deja ouvert reutilise).</summary>
    public static void RevealInExplorer(IReadOnlyList<string> paths)
    {
        var groups = paths.GroupBy(p => Path.GetDirectoryName(Path.GetFullPath(p).TrimEnd('\\')) ?? "");
        foreach (var g in groups)
        {
            IntPtr folder = IntPtr.Zero;
            var children = new List<IntPtr>();
            try
            {
                if (SHParseDisplayName(g.Key, IntPtr.Zero, out folder, 0, out _) != 0) { Process.Start("explorer.exe", $"\"{g.Key}\""); continue; }
                foreach (var p in g)
                    if (SHParseDisplayName(Path.GetFullPath(p), IntPtr.Zero, out var pidl, 0, out _) == 0) children.Add(pidl);
                var lastIds = children.Select(ILFindLastID).ToArray();
                SHOpenFolderAndSelectItems(folder, (uint)lastIds.Length, lastIds, 0);
            }
            finally
            {
                if (folder != IntPtr.Zero) CoTaskMemFree(folder);
                foreach (var c in children) CoTaskMemFree(c);
            }
        }
    }

    /// <summary>Terminal dans le dossier : Windows Terminal s'il est installe, sinon PowerShell.
    /// Le dossier est passe comme repertoire de travail, jamais dans une ligne de commande.</summary>
    public static void OpenTerminal(string directory)
    {
        var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        ProcessStartInfo psi;
        if (File.Exists(wt))
        {
            psi = new ProcessStartInfo(wt) { UseShellExecute = false, WorkingDirectory = directory };
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add(".");
        }
        else
        {
            var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            psi = new ProcessStartInfo(File.Exists(ps) ? ps : "cmd.exe") { UseShellExecute = true, WorkingDirectory = directory };
            if (File.Exists(ps)) psi.ArgumentList.Add("-NoExit");
        }
        Process.Start(psi);
    }

    // ------------------------------------------------------------------ Raccourci

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    /// <summary>Cree « Nom - Raccourci.lnk » a cote de la cible (nom unique). Retourne le chemin cree.</summary>
    public static string CreateShortcut(string target, string? directory = null, string? name = null)
    {
        var dir = directory ?? Path.GetDirectoryName(Path.GetFullPath(target).TrimEnd('\\'))!;
        var baseName = name ?? (Path.GetFileName(target.TrimEnd('\\')) + " - Raccourci");
        var lnk = NameConflict.UniquePath(Path.Combine(dir, PathSafety.SanitizeFileName(baseName) + ".lnk"));
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(Path.GetFullPath(target));
            link.SetWorkingDirectory(Path.GetDirectoryName(Path.GetFullPath(target).TrimEnd('\\')) ?? "");
            ((IPersistFile)link).Save(lnk, true);
        }
        finally { Marshal.ReleaseComObject(link); }
        return lnk;
    }

    // ------------------------------------------------------------------ Partage (verbe du Shell)

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
        public POINT ptInvoke;
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, IntPtr commandstring, int cch);
    }

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDataObject = new("0000010e-0000-0000-C000-000000000046");
    private const int CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_FLAG_NO_UI = 0x400;

    /// <summary>
    /// Invoque un verbe du menu contextuel du Shell (ex. « Windows.ModernShare » = Partager).
    /// Retourne faux si le verbe n'existe pas pour ces elements.
    /// </summary>
    public static bool InvokeVerb(IReadOnlyList<string> paths, string verb, IntPtr owner)
    {
        var (folder, children, release) = Bind(paths);
        if (folder is null) return false;
        try
        {
            var iid = IID_IContextMenu;
            if (folder.GetUIObjectOf(owner, (uint)children.Length, children, ref iid, IntPtr.Zero, out var cmPtr) != 0) return false;
            var cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
            var hmenu = CreatePopupMenu();
            try
            {
                cm.QueryContextMenu(hmenu, 0, 1, 0x7FFF, 0x100 /* CMF_EXTENDEDVERBS */);
                var ansi = Marshal.StringToHGlobalAnsi(verb);
                var uni = Marshal.StringToHGlobalUni(verb);
                try
                {
                    var ci = new CMINVOKECOMMANDINFOEX
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(), fMask = CMIC_MASK_UNICODE, hwnd = owner,
                        lpVerb = ansi, lpVerbW = uni, nShow = 1,
                    };
                    return cm.InvokeCommand(ref ci) == 0;
                }
                finally { Marshal.FreeHGlobal(ansi); Marshal.FreeHGlobal(uni); }
            }
            finally
            {
                DestroyMenu(hmenu);
                Marshal.ReleaseComObject(cm);
                Marshal.Release(cmPtr);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Verbe « {verb} » : {ex.Message}");
            return false;
        }
        finally { release(); }
    }

    public static bool Share(IReadOnlyList<string> paths, IntPtr owner) => InvokeVerb(paths, "Windows.ModernShare", owner);

    private static object? DataObjectFor(IReadOnlyList<string> paths, IntPtr owner)
    {
        var (folder, children, release) = Bind(paths);
        try
        {
            if (folder is null) return null;
            var iid = IID_IDataObject;
            return folder.GetUIObjectOf(owner, (uint)children.Length, children, ref iid, IntPtr.Zero, out var p) == 0
                ? Marshal.GetObjectForIUnknown(p)
                : null;
        }
        catch { return null; }
        finally { release(); }
    }

    /// <summary>Dossier Shell parent commun + identifiants relatifs des elements (meme dossier requis).</summary>
    private static (IShellFolder? folder, IntPtr[] children, Action release) Bind(IReadOnlyList<string> paths)
    {
        var pidls = new List<IntPtr>();
        IShellFolder? folder = null;
        var children = new List<IntPtr>();
        void Release()
        {
            foreach (var p in pidls) CoTaskMemFree(p);
            if (folder is not null) Marshal.ReleaseComObject(folder);
        }
        var parent = Path.GetDirectoryName(Path.GetFullPath(paths[0]).TrimEnd('\\'));
        foreach (var p in paths)
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(p).TrimEnd('\\')), parent, StringComparison.OrdinalIgnoreCase)) continue;
            if (SHParseDisplayName(Path.GetFullPath(p), IntPtr.Zero, out var pidl, 0, out _) != 0) continue;
            pidls.Add(pidl);
            if (folder is null)
            {
                var iid = IID_IShellFolder;
                if (SHBindToParent(pidl, ref iid, out var folderPtr, out var last) != 0) continue;
                folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
                Marshal.Release(folderPtr);
                children.Add(last);
            }
            else children.Add(ILFindLastID(pidl));
        }
        return (folder, children.ToArray(), Release);
    }
}
