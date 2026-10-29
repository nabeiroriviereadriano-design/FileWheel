using System.Runtime.InteropServices;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Pdf;

/// <summary>
/// Liaison minimale avec PDFium (bibliotheque native chargee a la demande).
/// Le document est lu par blocs via FPDF_LoadCustomDocument : pas de copie integrale en memoire
/// et aucun probleme d'encodage de chemin (accents, caracteres non latins).
/// PDFium n'etant pas re-entrant, tous les appels sont serialises.
/// </summary>
public static unsafe class Pdfium
{
    private static readonly object Gate = new();
    private static IntPtr _lib;
    private static string? _loadedPath;

    // Pointeurs de fonctions resolus dynamiquement
    private static delegate* unmanaged[Cdecl]<void> _init;
    private static delegate* unmanaged[Cdecl]<void*, byte*, IntPtr> _loadCustom;
    private static delegate* unmanaged[Cdecl]<IntPtr, void> _closeDoc;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _pageCount;
    private static delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr> _loadPage;
    private static delegate* unmanaged[Cdecl]<IntPtr, void> _closePage;
    private static delegate* unmanaged[Cdecl]<IntPtr, float> _pageWidth;
    private static delegate* unmanaged[Cdecl]<IntPtr, float> _pageHeight;
    private static delegate* unmanaged[Cdecl]<int, int, int, IntPtr> _bmpCreate;
    private static delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int, uint, int> _bmpFill;
    private static delegate* unmanaged[Cdecl]<IntPtr, void*> _bmpBuffer;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _bmpStride;
    private static delegate* unmanaged[Cdecl]<IntPtr, void> _bmpDestroy;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int, int, int, int, int, void> _render;
    private static delegate* unmanaged[Cdecl]<uint> _lastError;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _textLoad;
    private static delegate* unmanaged[Cdecl]<IntPtr, void> _textClose;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _textCount;
    private static delegate* unmanaged[Cdecl]<IntPtr, int, int, ushort*, int> _textGet;

    private const int FPDF_ANNOT = 0x01, FPDF_LCD_TEXT = 0x02;

    public static void EnsureLoaded(string libraryPath)
    {
        lock (Gate)
        {
            if (_lib != IntPtr.Zero && _loadedPath == libraryPath) return;
            if (_lib != IntPtr.Zero) throw new ConversionException(ConversionErrorKind.EngineMissing, "PDFium est déjà chargé depuis un autre emplacement : redémarrez FileWheel.");
            try { _lib = NativeLibrary.Load(libraryPath); }
            catch (Exception ex) { throw new ConversionException(ConversionErrorKind.EngineMissing, "Impossible de charger PDFium.", ex.Message, ex); }
            _loadedPath = libraryPath;
            IntPtr F(string name) => NativeLibrary.TryGetExport(_lib, name, out var p) ? p
                : throw new ConversionException(ConversionErrorKind.EngineMissing, $"Version de PDFium incompatible (fonction {name} absente).");
            _init = (delegate* unmanaged[Cdecl]<void>)F("FPDF_InitLibrary");
            _loadCustom = (delegate* unmanaged[Cdecl]<void*, byte*, IntPtr>)F("FPDF_LoadCustomDocument");
            _closeDoc = (delegate* unmanaged[Cdecl]<IntPtr, void>)F("FPDF_CloseDocument");
            _pageCount = (delegate* unmanaged[Cdecl]<IntPtr, int>)F("FPDF_GetPageCount");
            _loadPage = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr>)F("FPDF_LoadPage");
            _closePage = (delegate* unmanaged[Cdecl]<IntPtr, void>)F("FPDF_ClosePage");
            _pageWidth = (delegate* unmanaged[Cdecl]<IntPtr, float>)F("FPDF_GetPageWidthF");
            _pageHeight = (delegate* unmanaged[Cdecl]<IntPtr, float>)F("FPDF_GetPageHeightF");
            _bmpCreate = (delegate* unmanaged[Cdecl]<int, int, int, IntPtr>)F("FPDFBitmap_Create");
            _bmpFill = (delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int, uint, int>)F("FPDFBitmap_FillRect");
            _bmpBuffer = (delegate* unmanaged[Cdecl]<IntPtr, void*>)F("FPDFBitmap_GetBuffer");
            _bmpStride = (delegate* unmanaged[Cdecl]<IntPtr, int>)F("FPDFBitmap_GetStride");
            _bmpDestroy = (delegate* unmanaged[Cdecl]<IntPtr, void>)F("FPDFBitmap_Destroy");
            _render = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int, int, int, int, int, void>)F("FPDF_RenderPageBitmap");
            _lastError = (delegate* unmanaged[Cdecl]<uint>)F("FPDF_GetLastError");
            _textLoad = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)F("FPDFText_LoadPage");
            _textClose = (delegate* unmanaged[Cdecl]<IntPtr, void>)F("FPDFText_ClosePage");
            _textCount = (delegate* unmanaged[Cdecl]<IntPtr, int>)F("FPDFText_CountChars");
            _textGet = (delegate* unmanaged[Cdecl]<IntPtr, int, int, ushort*, int>)F("FPDFText_GetText");
            _init();
        }
    }

    // ------------------------------------------------------------- acces au fichier par blocs
    // FPDF_FILEACCESS { unsigned long m_FileLen; int (*m_GetBlock)(void*, unsigned long, unsigned char*, unsigned long); void* m_Param; }
    // « unsigned long » vaut 32 bits sous Windows et 64 bits sous Linux : deux variantes.

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAccessWin
    {
        public uint FileLen;
        public delegate* unmanaged[Cdecl]<IntPtr, uint, byte*, uint, int> GetBlock;
        public IntPtr Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAccessLp64
    {
        public ulong FileLen;
        public delegate* unmanaged[Cdecl]<IntPtr, ulong, byte*, ulong, int> GetBlock;
        public IntPtr Param;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int GetBlockWin(IntPtr param, uint position, byte* buf, uint size) => ReadBlock(param, position, buf, size);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int GetBlockLp64(IntPtr param, ulong position, byte* buf, ulong size) => ReadBlock(param, (long)position, buf, (long)size);

    private static int ReadBlock(IntPtr param, long position, byte* buf, long size)
    {
        try
        {
            var fs = (FileStream)GCHandle.FromIntPtr(param).Target!;
            fs.Position = position;
            var span = new Span<byte>(buf, (int)size);
            int read = 0;
            while (read < span.Length)
            {
                int n = fs.Read(span[read..]);
                if (n == 0) return 0;
                read += n;
            }
            return 1;
        }
        catch { return 0; }
    }

    /// <summary>Document PDF ouvert (a fermer avec Dispose). Toutes les operations sont serialisees.</summary>
    public sealed class Document : IDisposable
    {
        private IntPtr _doc;
        private readonly FileStream _fs;
        private GCHandle _handle;
        private readonly IntPtr _access;

        internal Document(IntPtr doc, FileStream fs, GCHandle handle, IntPtr access)
        {
            _doc = doc; _fs = fs; _handle = handle; _access = access;
        }

        public int PageCount { get { lock (Gate) return _pageCount(_doc); } }

        public (double Width, double Height) PageSize(int index)
        {
            lock (Gate)
            {
                var page = _loadPage(_doc, index);
                if (page == IntPtr.Zero) throw new ConversionException(ConversionErrorKind.Corrupt, $"Page {index + 1} illisible.");
                try { return (_pageWidth(page), _pageHeight(page)); }
                finally { _closePage(page); }
            }
        }

        /// <summary>Rendu d'une page en RGBA (fond blanc).</summary>
        public (int W, int H, byte[] Rgba) Render(int index, double dpi)
        {
            lock (Gate)
            {
                var page = _loadPage(_doc, index);
                if (page == IntPtr.Zero) throw new ConversionException(ConversionErrorKind.Corrupt, $"Page {index + 1} illisible.");
                try
                {
                    double pw = _pageWidth(page), ph = _pageHeight(page);
                    int w = Math.Max(1, (int)Math.Round(pw / 72.0 * dpi));
                    int h = Math.Max(1, (int)Math.Round(ph / 72.0 * dpi));
                    if ((long)w * h > 100_000_000)
                        throw new ConversionException(ConversionErrorKind.TooLarge, $"Page {index + 1} trop grande à {dpi} ppp : réduisez la résolution.");
                    var bmp = _bmpCreate(w, h, 0);
                    if (bmp == IntPtr.Zero) throw new ConversionException(ConversionErrorKind.TooLarge, "Mémoire insuffisante pour le rendu de la page.");
                    try
                    {
                        _bmpFill(bmp, 0, 0, w, h, 0xFFFFFFFF);
                        _render(bmp, page, 0, 0, w, h, 0, FPDF_ANNOT | FPDF_LCD_TEXT);
                        int stride = _bmpStride(bmp);
                        byte* src = (byte*)_bmpBuffer(bmp);
                        var rgba = new byte[(long)w * h * 4];
                        for (int y = 0; y < h; y++)
                        {
                            byte* row = src + (long)y * stride;
                            long o = (long)y * w * 4;
                            for (int x = 0; x < w; x++)
                            {
                                // BGRx -> RGBA opaque
                                rgba[o + x * 4] = row[x * 4 + 2];
                                rgba[o + x * 4 + 1] = row[x * 4 + 1];
                                rgba[o + x * 4 + 2] = row[x * 4];
                                rgba[o + x * 4 + 3] = 255;
                            }
                        }
                        return (w, h, rgba);
                    }
                    finally { _bmpDestroy(bmp); }
                }
                finally { _closePage(page); }
            }
        }

        public string ExtractText(int index)
        {
            lock (Gate)
            {
                var page = _loadPage(_doc, index);
                if (page == IntPtr.Zero) return "";
                try
                {
                    var tp = _textLoad(page);
                    if (tp == IntPtr.Zero) return "";
                    try
                    {
                        int n = _textCount(tp);
                        if (n <= 0) return "";
                        var buf = new ushort[n + 1];
                        fixed (ushort* p = buf)
                        {
                            int written = _textGet(tp, 0, n, p);
                            return new string((char*)p, 0, Math.Max(0, written - 1));
                        }
                    }
                    finally { _textClose(tp); }
                }
                finally { _closePage(page); }
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (_doc != IntPtr.Zero) { _closeDoc(_doc); _doc = IntPtr.Zero; }
            }
            if (_handle.IsAllocated) _handle.Free();
            Marshal.FreeHGlobal(_access);
            _fs.Dispose();
        }
    }

    public static Document Open(string path, string? password)
    {
        if (_lib == IntPtr.Zero) throw new InvalidOperationException("PDFium non chargé.");
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var handle = GCHandle.Alloc(fs);
        IntPtr access;
        bool win = OperatingSystem.IsWindows();
        if (win)
        {
            if (fs.Length > uint.MaxValue) { handle.Free(); fs.Dispose(); throw new ConversionException(ConversionErrorKind.TooLarge, "PDF de plus de 4 Gio non pris en charge."); }
            access = Marshal.AllocHGlobal(sizeof(FileAccessWin));
            *(FileAccessWin*)access = new FileAccessWin { FileLen = (uint)fs.Length, GetBlock = &GetBlockWin, Param = GCHandle.ToIntPtr(handle) };
        }
        else
        {
            access = Marshal.AllocHGlobal(sizeof(FileAccessLp64));
            *(FileAccessLp64*)access = new FileAccessLp64 { FileLen = (ulong)fs.Length, GetBlock = &GetBlockLp64, Param = GCHandle.ToIntPtr(handle) };
        }
        IntPtr doc;
        uint err;
        var pwd = password is null ? null : System.Text.Encoding.UTF8.GetBytes(password + "\0");
        lock (Gate)
        {
            fixed (byte* pp = pwd)
            {
                doc = _loadCustom((void*)access, pp);
            }
            err = doc == IntPtr.Zero ? _lastError() : 0;
        }
        if (doc == IntPtr.Zero)
        {
            handle.Free();
            Marshal.FreeHGlobal(access);
            fs.Dispose();
            throw err switch
            {
                4 => new ConversionException(ConversionErrorKind.Protected, password is null ? "PDF protégé par mot de passe." : "Mot de passe du PDF incorrect."),
                5 => new ConversionException(ConversionErrorKind.Protected, "PDF protégé (format de sécurité non pris en charge)."),
                3 => new ConversionException(ConversionErrorKind.Corrupt, "PDF endommagé ou invalide."),
                2 => new ConversionException(ConversionErrorKind.InvalidInput, "PDF introuvable ou illisible."),
                _ => new ConversionException(ConversionErrorKind.Corrupt, $"PDFium n'a pas pu ouvrir le PDF (erreur {err})."),
            };
        }
        return new Document(doc, fs, handle, access);
    }
}
