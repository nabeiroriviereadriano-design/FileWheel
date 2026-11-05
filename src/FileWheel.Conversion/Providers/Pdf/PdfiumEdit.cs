using System.Runtime.InteropServices;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Pdf;

/// <summary>
/// Fonctions d'edition de PDFium : creation, import de pages, suppression, rotation, enregistrement,
/// et remplacement des images d'une page (compression). Memes regles que la lecture : appels serialises.
/// </summary>
public static unsafe partial class Pdfium
{
    private static delegate* unmanaged[Cdecl]<IntPtr> _newDoc;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int, int> _importPages;
    private static delegate* unmanaged[Cdecl]<IntPtr, int, void> _deletePage;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _getRotation;
    private static delegate* unmanaged[Cdecl]<IntPtr, int, void> _setRotation;
    private static delegate* unmanaged[Cdecl]<IntPtr, void*, uint, int> _saveAsCopy;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _countObjects;
    private static delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr> _getObject;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _objType;
    private static delegate* unmanaged[Cdecl]<IntPtr, float*, float*, float*, float*, int> _objBounds;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _imgBitmap;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr> _imgRendered;
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ImageMetadata*, int> _imgMeta;
    private static IntPtr _imgRawSize; // unsigned long : 32 bits sous Windows, 64 bits ailleurs
    private static delegate* unmanaged[Cdecl]<IntPtr*, int, IntPtr, void*, int> _imgLoadJpeg;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _bmpWidth;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _bmpHeight;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _bmpFormat;
    private static delegate* unmanaged[Cdecl]<IntPtr, int> _generateContent;

    private const int PageObjImage = 3;
    private const uint SaveNoIncremental = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct ImageMetadata
    {
        public uint Width, Height;
        public float HorizontalDpi, VerticalDpi;
        public uint BitsPerPixel;
        public int Colorspace;
        public int MarkedContentId;
    }

    private static void LoadEditFunctions(Func<string, IntPtr> f)
    {
        _newDoc = (delegate* unmanaged[Cdecl]<IntPtr>)f("FPDF_CreateNewDocument");
        _importPages = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int, int>)f("FPDF_ImportPages");
        _deletePage = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)f("FPDFPage_Delete");
        _getRotation = (delegate* unmanaged[Cdecl]<IntPtr, int>)f("FPDFPage_GetRotation");
        _setRotation = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)f("FPDFPage_SetRotation");
        _saveAsCopy = (delegate* unmanaged[Cdecl]<IntPtr, void*, uint, int>)f("FPDF_SaveAsCopy");
        _countObjects = (delegate* unmanaged[Cdecl]<IntPtr, int>)f("FPDFPage_CountObjects");
        _getObject = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr>)f("FPDFPage_GetObject");
        _objType = (delegate* unmanaged[Cdecl]<IntPtr, int>)f("FPDFPageObj_GetType");
        _objBounds = (delegate* unmanaged[Cdecl]<IntPtr, float*, float*, float*, float*, int>)f("FPDFPageObj_GetBounds");
        _imgBitmap = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)f("FPDFImageObj_GetBitmap");
        _imgRendered = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>)f("FPDFImageObj_GetRenderedBitmap");
        _imgMeta = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ImageMetadata*, int>)f("FPDFImageObj_GetImageMetadata");
        _imgRawSize = f("FPDFImageObj_GetImageDataRaw");
        _imgLoadJpeg = (delegate* unmanaged[Cdecl]<IntPtr*, int, IntPtr, void*, int>)f("FPDFImageObj_LoadJpegFileInline");
        _bmpWidth = (delegate* unmanaged[Cdecl]<IntPtr, int>)f("FPDFBitmap_GetWidth");
        _bmpHeight = (delegate* unmanaged[Cdecl]<IntPtr, int>)f("FPDFBitmap_GetHeight");
        _bmpFormat = (delegate* unmanaged[Cdecl]<IntPtr, int>)f("FPDFBitmap_GetFormat");
        _generateContent = (delegate* unmanaged[Cdecl]<IntPtr, int>)f("FPDFPage_GenerateContent");
    }

    private static void EnsureReady()
    {
        if (_lib == IntPtr.Zero) throw new InvalidOperationException("PDFium non chargé.");
    }

    // ------------------------------------------------------------------ nouveau document

    /// <summary>Document vide cree en memoire (fusion, extraction de pages).</summary>
    public sealed class NewDocument : IDisposable
    {
        internal IntPtr Handle { get; private set; }

        public NewDocument()
        {
            EnsureReady();
            lock (Gate) Handle = _newDoc();
            if (Handle == IntPtr.Zero) throw new ConversionException(ConversionErrorKind.EngineCrashed, "PDFium n'a pas pu créer le document.");
        }

        public int PageCount { get { lock (Gate) return _pageCount(Handle); } }

        /// <summary>Ajoute des pages d'un document (pageRange : « 1,3,5-7 » ; null = toutes).</summary>
        public void Import(Document source, string? pageRange)
        {
            lock (Gate)
            {
                int at = _pageCount(Handle);
                var bytes = pageRange is null ? null : System.Text.Encoding.ASCII.GetBytes(pageRange + "\0");
                int ok;
                fixed (byte* r = bytes) ok = _importPages(Handle, source.Handle, r, at);
                if (ok == 0) throw new ConversionException(ConversionErrorKind.EngineCrashed, "PDFium n'a pas pu copier les pages.");
            }
        }

        public void SaveTo(string path) => Save(Handle, path);

        public void Dispose()
        {
            lock (Gate)
            {
                if (Handle != IntPtr.Zero) { _closeDoc(Handle); Handle = IntPtr.Zero; }
            }
        }
    }

    public sealed partial class Document
    {
        public void DeletePage(int index)
        {
            lock (Gate) _deletePage(Handle, index);
        }

        /// <summary>Rotation de la page en quarts de tour (0 a 3, sens horaire).</summary>
        public int GetRotation(int index)
        {
            lock (Gate)
            {
                var page = _loadPage(Handle, index);
                if (page == IntPtr.Zero) throw new ConversionException(ConversionErrorKind.Corrupt, $"Page {index + 1} illisible.");
                try { return _getRotation(page); }
                finally { _closePage(page); }
            }
        }

        public void Rotate(int index, int quarterTurns)
        {
            lock (Gate)
            {
                var page = _loadPage(Handle, index);
                if (page == IntPtr.Zero) throw new ConversionException(ConversionErrorKind.Corrupt, $"Page {index + 1} illisible.");
                try { _setRotation(page, (((_getRotation(page) + quarterTurns) % 4) + 4) % 4); }
                finally { _closePage(page); }
            }
        }

        public void SaveTo(string path) => Save(Handle, path);

        /// <summary>
        /// Recompresse les images de la page en JPEG quand c'est avantageux.
        /// Les images monochromes (scans noir et blanc), transparentes ou deja petites sont laissees telles quelles.
        /// </summary>
        public PdfImageStats CompressImages(int index, PdfCompressOptions o)
        {
            var stats = new PdfImageStats();
            lock (Gate)
            {
                var page = _loadPage(Handle, index);
                if (page == IntPtr.Zero) throw new ConversionException(ConversionErrorKind.Corrupt, $"Page {index + 1} illisible.");
                try
                {
                    int n = _countObjects(page);
                    for (int i = 0; i < n; i++)
                    {
                        var obj = _getObject(page, i);
                        if (obj == IntPtr.Zero || _objType(obj) != PageObjImage) continue;
                        stats.Images++;
                        if (TryRecompress(page, obj, o, out long before, out long after))
                        {
                            stats.Recompressed++;
                            stats.BytesBefore += before;
                            stats.BytesAfter += after;
                        }
                    }
                    // L'image remplacee est un nouvel objet : le contenu de la page doit y faire reference.
                    if (stats.Recompressed > 0 && _generateContent(page) == 0)
                        throw new ConversionException(ConversionErrorKind.EngineCrashed, $"PDFium n'a pas pu réécrire la page {index + 1}.");
                }
                finally { _closePage(page); }
            }
            return stats;
        }

        private bool TryRecompress(IntPtr page, IntPtr obj, PdfCompressOptions o, out long before, out long after)
        {
            before = after = 0;
            ImageMetadata meta;
            if (_imgMeta(obj, page, &meta) == 0) return false;
            if (meta.BitsPerPixel <= 1 || meta.Width < 32 || meta.Height < 32) return false;
            before = RawSize(obj);
            if (before < o.MinImageBytes) return false;

            // Transparence (masque) : JPEG ne la conserve pas → image laissee intacte.
            var rendered = _imgRendered(Handle, page, obj);
            if (rendered != IntPtr.Zero)
            {
                try { if (HasTransparency(rendered)) return false; }
                finally { _bmpDestroy(rendered); }
            }

            var bmp = _imgBitmap(obj);
            if (bmp == IntPtr.Zero) return false;
            int w, h;
            byte[] rgb;
            try
            {
                w = _bmpWidth(bmp); h = _bmpHeight(bmp);
                if (w <= 0 || h <= 0) return false;
                rgb = ToRgb(bmp, w, h);
            }
            finally { _bmpDestroy(bmp); }

            // Resolution d'affichage : taille sur la page (points) → pixels utiles a la resolution voulue
            float l, b, r, t;
            if (_objBounds(obj, &l, &b, &r, &t) != 0 && o.MaxDpi > 0)
            {
                double targetW = Math.Abs(r - l) / 72.0 * o.MaxDpi, targetH = Math.Abs(t - b) / 72.0 * o.MaxDpi;
                double k = Math.Min(1.0, Math.Max(targetW / w, targetH / h));
                if (k < 0.9)
                {
                    int nw = Math.Max(1, (int)Math.Round(w * k)), nh = Math.Max(1, (int)Math.Round(h * k));
                    rgb = Resample.AreaRgb(rgb, w, h, nw, nh);
                    w = nw; h = nh;
                }
            }

            var jpeg = o.EncodeJpeg(w, h, rgb, o.Quality);
            after = jpeg.Length;
            if (after >= before * 0.9) { after = before; return false; }

            using var ms = new MemoryStream(jpeg, writable: false);
            var handle = GCHandle.Alloc(ms);
            var access = CreateAccess(ms.Length, handle);
            try
            {
                IntPtr p = page;
                if (_imgLoadJpeg(&p, 1, obj, (void*)access) == 0) { after = before; return false; }
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(access);
                handle.Free();
            }
        }
    }

    private static long RawSize(IntPtr obj) => OperatingSystem.IsWindows()
        ? ((delegate* unmanaged[Cdecl]<IntPtr, void*, uint, uint>)_imgRawSize)(obj, null, 0)
        : (long)((delegate* unmanaged[Cdecl]<IntPtr, void*, ulong, ulong>)_imgRawSize)(obj, null, 0);

    private static bool HasTransparency(IntPtr bmp)
    {
        if (_bmpFormat(bmp) != 4) return false; // FPDFBitmap_BGRA
        int w = _bmpWidth(bmp), h = _bmpHeight(bmp), stride = _bmpStride(bmp);
        byte* p = (byte*)_bmpBuffer(bmp);
        for (int y = 0; y < h; y++)
        {
            byte* row = p + (long)y * stride;
            for (int x = 0; x < w; x++) if (row[x * 4 + 3] < 250) return true;
        }
        return false;
    }

    private static byte[] ToRgb(IntPtr bmp, int w, int h)
    {
        int format = _bmpFormat(bmp), stride = _bmpStride(bmp);
        byte* p = (byte*)_bmpBuffer(bmp);
        var rgb = new byte[(long)w * h * 3];
        for (int y = 0; y < h; y++)
        {
            byte* row = p + (long)y * stride;
            long o = (long)y * w * 3;
            for (int x = 0; x < w; x++)
            {
                switch (format)
                {
                    case 1: // gris
                        rgb[o + x * 3] = rgb[o + x * 3 + 1] = rgb[o + x * 3 + 2] = row[x];
                        break;
                    case 2: // BGR
                        rgb[o + x * 3] = row[x * 3 + 2]; rgb[o + x * 3 + 1] = row[x * 3 + 1]; rgb[o + x * 3 + 2] = row[x * 3];
                        break;
                    default: // BGRx / BGRA
                        rgb[o + x * 3] = row[x * 4 + 2]; rgb[o + x * 3 + 1] = row[x * 4 + 1]; rgb[o + x * 3 + 2] = row[x * 4];
                        break;
                }
            }
        }
        return rgb;
    }

    /// <summary>FPDF_FILEACCESS sur un flux quelconque (lecture par blocs).</summary>
    private static IntPtr CreateAccess(long length, GCHandle handle)
    {
        IntPtr access;
        if (OperatingSystem.IsWindows())
        {
            access = Marshal.AllocHGlobal(sizeof(FileAccessWin));
            *(FileAccessWin*)access = new FileAccessWin { FileLen = (uint)length, GetBlock = &GetBlockWin, Param = GCHandle.ToIntPtr(handle) };
        }
        else
        {
            access = Marshal.AllocHGlobal(sizeof(FileAccessLp64));
            *(FileAccessLp64*)access = new FileAccessLp64 { FileLen = (ulong)length, GetBlock = &GetBlockLp64, Param = GCHandle.ToIntPtr(handle) };
        }
        return access;
    }

    // ------------------------------------------------------------------ enregistrement
    // FPDF_FILEWRITE { int version; int (*WriteBlock)(FPDF_FILEWRITE* pThis, const void* pData, unsigned long size); }
    // On ajoute a la suite un champ prive : le GCHandle du flux de sortie.

    [StructLayout(LayoutKind.Sequential)]
    private struct FileWriteWin
    {
        public int Version;
        public delegate* unmanaged[Cdecl]<FileWriteWin*, void*, uint, int> WriteBlock;
        public IntPtr Stream;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileWriteLp64
    {
        public int Version;
        public delegate* unmanaged[Cdecl]<FileWriteLp64*, void*, ulong, int> WriteBlock;
        public IntPtr Stream;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int WriteBlockWin(FileWriteWin* self, void* data, uint size) => WriteTo(self->Stream, data, size);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int WriteBlockLp64(FileWriteLp64* self, void* data, ulong size) => WriteTo(self->Stream, data, (long)size);

    private static int WriteTo(IntPtr stream, void* data, long size)
    {
        try
        {
            var s = (Stream)GCHandle.FromIntPtr(stream).Target!;
            s.Write(new ReadOnlySpan<byte>(data, checked((int)size)));
            return 1;
        }
        catch { return 0; }
    }

    private static void Save(IntPtr doc, string path)
    {
        EnsureReady();
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var handle = GCHandle.Alloc(fs);
        try
        {
            int ok;
            lock (Gate)
            {
                if (OperatingSystem.IsWindows())
                {
                    var w = new FileWriteWin { Version = 1, WriteBlock = &WriteBlockWin, Stream = GCHandle.ToIntPtr(handle) };
                    ok = _saveAsCopy(doc, &w, SaveNoIncremental);
                }
                else
                {
                    var w = new FileWriteLp64 { Version = 1, WriteBlock = &WriteBlockLp64, Stream = GCHandle.ToIntPtr(handle) };
                    ok = _saveAsCopy(doc, &w, SaveNoIncremental);
                }
            }
            if (ok == 0) throw new ConversionException(ConversionErrorKind.EngineCrashed, "PDFium n'a pas pu enregistrer le PDF.");
        }
        finally { handle.Free(); }
    }
}

/// <summary>Reglages de la compression des images d'un PDF.</summary>
public sealed record PdfCompressOptions(int Quality, int MaxDpi, Func<int, int, byte[], int, byte[]> EncodeJpeg)
{
    /// <summary>Images plus petites que ce seuil laissees telles quelles.</summary>
    public long MinImageBytes { get; init; } = 24 * 1024;
}

public sealed class PdfImageStats
{
    public int Images { get; set; }
    public int Recompressed { get; set; }
    public long BytesBefore { get; set; }
    public long BytesAfter { get; set; }
}

/// <summary>Reechantillonnage simple (moyenne de surface) pour les reductions d'image.</summary>
public static class Resample
{
    /// <summary>Reduction RGB 8 bits par moyenne des pixels couverts (qualite proche de « Lanczos » en reduction).</summary>
    public static byte[] AreaRgb(byte[] src, int w, int h, int nw, int nh) => Area(src, w, h, nw, nh, 3);

    public static byte[] Area(byte[] src, int w, int h, int nw, int nh, int channels)
    {
        var dst = new byte[(long)nw * nh * channels];
        double sx = (double)w / nw, sy = (double)h / nh;
        var acc = new double[channels];
        for (int y = 0; y < nh; y++)
        {
            double y0 = y * sy, y1 = Math.Min(h, (y + 1) * sy);
            for (int x = 0; x < nw; x++)
            {
                double x0 = x * sx, x1 = Math.Min(w, (x + 1) * sx);
                Array.Clear(acc);
                double area = 0;
                for (int yy = (int)y0; yy < Math.Ceiling(y1); yy++)
                {
                    double wy = Math.Min(y1, yy + 1) - Math.Max(y0, yy);
                    if (wy <= 0) continue;
                    for (int xx = (int)x0; xx < Math.Ceiling(x1); xx++)
                    {
                        double wx = Math.Min(x1, xx + 1) - Math.Max(x0, xx);
                        if (wx <= 0) continue;
                        double wgt = wx * wy;
                        long o = ((long)yy * w + xx) * channels;
                        for (int c = 0; c < channels; c++) acc[c] += src[o + c] * wgt;
                        area += wgt;
                    }
                }
                long d = ((long)y * nw + x) * channels;
                for (int c = 0; c < channels; c++) dst[d + c] = (byte)Math.Clamp(Math.Round(acc[c] / area), 0, 255);
            }
        }
        return dst;
    }

    /// <summary>Agrandissement / reduction bilineaire d'une image a un canal (masques).</summary>
    public static byte[] BilinearGray(float[] src, int w, int h, int nw, int nh)
    {
        var dst = new byte[(long)nw * nh];
        double sx = (double)w / nw, sy = (double)h / nh;
        for (int y = 0; y < nh; y++)
        {
            double fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, h - 1);
            int y0 = (int)fy, y1 = Math.Min(h - 1, y0 + 1);
            double ty = fy - y0;
            for (int x = 0; x < nw; x++)
            {
                double fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, w - 1);
                int x0 = (int)fx, x1 = Math.Min(w - 1, x0 + 1);
                double tx = fx - x0;
                double v = (src[y0 * w + x0] * (1 - tx) + src[y0 * w + x1] * tx) * (1 - ty)
                         + (src[y1 * w + x0] * (1 - tx) + src[y1 * w + x1] * tx) * ty;
                dst[(long)y * nw + x] = (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
            }
        }
        return dst;
    }
}
