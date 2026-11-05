using System.Runtime.InteropServices;
using System.Text;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Ai;

/// <summary>
/// Liaison minimale avec ONNX Runtime (API C officielle, bibliotheque native chargee a la demande).
/// Aucune dependance NuGet : la bibliotheque fait partie du moteur optionnel « Détourage IA »,
/// telechargee avec l'accord de l'utilisateur et verifiee par SHA-256.
///
/// Les indices de la table OrtApi viennent de onnxruntime_c_api.h ; ils sont stables d'une version a
/// l'autre (les nouvelles fonctions sont ajoutees a la fin). On demande la version 17 de l'API (1.17),
/// disponible dans toutes les versions recentes.
/// </summary>
public static unsafe class OnnxRuntime
{
    private const uint ApiVersion = 17;
    private static readonly object Gate = new();
    private static IntPtr _lib;
    private static void** _api;
    private static IntPtr _env;

    // Indices dans la table OrtApi
    private const int iGetErrorMessage = 2, iCreateEnv = 3, iCreateSession = 7, iRun = 9, iCreateSessionOptions = 10,
        iSetGraphOptimizationLevel = 23, iSetIntraOpNumThreads = 24, iSessionGetInputName = 36, iSessionGetOutputName = 37,
        iCreateTensorWithData = 49, iGetTensorMutableData = 51, iGetDimensionsCount = 61, iGetDimensions = 62,
        iGetTensorTypeAndShape = 65, iCreateCpuMemoryInfo = 69, iAllocatorFree = 76, iGetAllocatorWithDefaultOptions = 78,
        iReleaseEnv = 92, iReleaseStatus = 93, iReleaseMemoryInfo = 94, iReleaseSession = 95, iReleaseValue = 96,
        iReleaseTensorTypeAndShapeInfo = 99, iReleaseSessionOptions = 100;

    public static string? Version { get; private set; }

    public static void EnsureLoaded(string libraryPath)
    {
        lock (Gate)
        {
            if (_lib != IntPtr.Zero) return;
            try { _lib = NativeLibrary.Load(libraryPath); }
            catch (Exception ex)
            {
                throw new ConversionException(ConversionErrorKind.EngineMissing,
                    OperatingSystem.IsWindows()
                        ? "Impossible de charger ONNX Runtime. Il a besoin des bibliothèques Microsoft Visual C++ 2015-2022 (x64) : installez-les depuis Paramètres › Moteurs, puis réessayez."
                        : "Impossible de charger ONNX Runtime.", ex.Message, ex);
            }
            if (!NativeLibrary.TryGetExport(_lib, "OrtGetApiBase", out var baseFn))
                throw new ConversionException(ConversionErrorKind.EngineMissing, "Bibliothèque ONNX Runtime incompatible (OrtGetApiBase absent).");
            var apiBase = ((delegate* unmanaged[Cdecl]<void**>)baseFn)();
            var getApi = (delegate* unmanaged[Cdecl]<uint, void**>)apiBase[0];
            var getVersion = (delegate* unmanaged[Cdecl]<byte*>)apiBase[1];
            Version = Marshal.PtrToStringUTF8((IntPtr)getVersion());
            _api = getApi(ApiVersion);
            if (_api == null)
                throw new ConversionException(ConversionErrorKind.EngineMissing, $"Version d'ONNX Runtime trop ancienne ({Version}) : 1.17 ou plus récente nécessaire.");
            IntPtr env;
            var logId = "FileWheel\0"u8;
            fixed (byte* id = logId)
                Check(((delegate* unmanaged[Cdecl]<int, byte*, IntPtr*, IntPtr>)_api[iCreateEnv])(3 /* ERROR */, id, &env));
            _env = env;
        }
    }

    private static void Check(IntPtr status)
    {
        if (status == IntPtr.Zero) return;
        var msg = Marshal.PtrToStringUTF8((IntPtr)((delegate* unmanaged[Cdecl]<IntPtr, byte*>)_api[iGetErrorMessage])(status)) ?? "?";
        ((delegate* unmanaged[Cdecl]<IntPtr, void>)_api[iReleaseStatus])(status);
        throw new ConversionException(ConversionErrorKind.EngineCrashed, "Erreur du moteur d'IA (ONNX Runtime).", msg);
    }

    private static void Release(int index, IntPtr p)
    {
        if (p != IntPtr.Zero) ((delegate* unmanaged[Cdecl]<IntPtr, void>)_api[index])(p);
    }

    /// <summary>Modele charge, pret a etre execute (une entree et une sortie de type float).</summary>
    public sealed class Session : IDisposable
    {
        private IntPtr _session;
        private readonly byte[] _inputName, _outputName;

        public Session(string modelPath, int threads = 0)
        {
            if (_api == null) throw new InvalidOperationException("ONNX Runtime non chargé.");
            IntPtr opts;
            Check(((delegate* unmanaged[Cdecl]<IntPtr*, IntPtr>)_api[iCreateSessionOptions])(&opts));
            try
            {
                if (threads <= 0) threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
                Check(((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr>)_api[iSetIntraOpNumThreads])(opts, threads));
                Check(((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr>)_api[iSetGraphOptimizationLevel])(opts, 99 /* ORT_ENABLE_ALL */));
                IntPtr session;
                // ORTCHAR_T : UTF-16 sous Windows, UTF-8 ailleurs
                if (OperatingSystem.IsWindows())
                {
                    fixed (char* p = modelPath + "\0")
                        Check(((delegate* unmanaged[Cdecl]<IntPtr, void*, IntPtr, IntPtr*, IntPtr>)_api[iCreateSession])(_env, p, opts, &session));
                }
                else
                {
                    fixed (byte* p = Encoding.UTF8.GetBytes(modelPath + "\0"))
                        Check(((delegate* unmanaged[Cdecl]<IntPtr, void*, IntPtr, IntPtr*, IntPtr>)_api[iCreateSession])(_env, p, opts, &session));
                }
                _session = session;
            }
            finally { Release(iReleaseSessionOptions, opts); }
            _inputName = Name(iSessionGetInputName, 0);
            _outputName = Name(iSessionGetOutputName, 0);
        }

        private byte[] Name(int fn, int index)
        {
            IntPtr alloc;
            Check(((delegate* unmanaged[Cdecl]<IntPtr*, IntPtr>)_api[iGetAllocatorWithDefaultOptions])(&alloc));
            byte* name;
            Check(((delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr, byte**, IntPtr>)_api[fn])(_session, (nuint)index, alloc, &name));
            try
            {
                var s = Marshal.PtrToStringUTF8((IntPtr)name) ?? "";
                return Encoding.UTF8.GetBytes(s + "\0");
            }
            finally
            {
                Check(((delegate* unmanaged[Cdecl]<IntPtr, void*, IntPtr>)_api[iAllocatorFree])(alloc, name));
            }
        }

        /// <summary>Execute le modele sur un tenseur float ; renvoie la premiere sortie et sa forme.</summary>
        public (float[] Data, long[] Shape) Run(float[] input, long[] shape)
        {
            IntPtr mem;
            Check(((delegate* unmanaged[Cdecl]<int, int, IntPtr*, IntPtr>)_api[iCreateCpuMemoryInfo])(1 /* OrtArenaAllocator */, 0, &mem));
            IntPtr inValue = IntPtr.Zero, outValue = IntPtr.Zero;
            try
            {
                fixed (float* data = input)
                fixed (long* sh = shape)
                fixed (byte* inName = _inputName)
                fixed (byte* outName = _outputName)
                {
                    Check(((delegate* unmanaged[Cdecl]<IntPtr, void*, nuint, long*, nuint, int, IntPtr*, IntPtr>)_api[iCreateTensorWithData])(
                        mem, data, (nuint)(input.Length * sizeof(float)), sh, (nuint)shape.Length, 1 /* float */, &inValue));
                    byte* inNames = inName, outNames = outName;
                    IntPtr inV = inValue;
                    IntPtr outV = IntPtr.Zero;
                    Check(((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte**, IntPtr*, nuint, byte**, nuint, IntPtr*, IntPtr>)_api[iRun])(
                        _session, IntPtr.Zero, &inNames, &inV, 1, &outNames, 1, &outV));
                    outValue = outV;
                }

                IntPtr info;
                Check(((delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, IntPtr>)_api[iGetTensorTypeAndShape])(outValue, &info));
                long[] outShape;
                try
                {
                    nuint n;
                    Check(((delegate* unmanaged[Cdecl]<IntPtr, nuint*, IntPtr>)_api[iGetDimensionsCount])(info, &n));
                    outShape = new long[(int)n];
                    fixed (long* d = outShape)
                        Check(((delegate* unmanaged[Cdecl]<IntPtr, long*, nuint, IntPtr>)_api[iGetDimensions])(info, d, n));
                }
                finally { Release(iReleaseTensorTypeAndShapeInfo, info); }

                long count = 1;
                foreach (var d in outShape) count *= Math.Max(1, d);
                void* outData;
                Check(((delegate* unmanaged[Cdecl]<IntPtr, void**, IntPtr>)_api[iGetTensorMutableData])(outValue, &outData));
                var result = new float[count];
                new ReadOnlySpan<float>(outData, (int)count).CopyTo(result);
                return (result, outShape);
            }
            finally
            {
                Release(iReleaseValue, inValue);
                Release(iReleaseValue, outValue);
                Release(iReleaseMemoryInfo, mem);
            }
        }

        public void Dispose()
        {
            Release(iReleaseSession, _session);
            _session = IntPtr.Zero;
        }
    }
}
