using FileWheel.Conversion.Core;
using FileWheel.Conversion.Engines;
using FileWheel.Conversion.Providers.Archives;
using FileWheel.Conversion.Providers.Data;
using FileWheel.Conversion.Providers.Ebooks;
using FileWheel.Conversion.Providers.Fonts;
using FileWheel.Conversion.Providers.Images;
using FileWheel.Conversion.Providers.Mail;
using FileWheel.Conversion.Providers.Media;
using FileWheel.Conversion.Providers.Model3D;
using FileWheel.Conversion.Providers.Office;
using FileWheel.Conversion.Providers.Pdf;
using FileWheel.Conversion.Providers.Text;
using FileWheel.Core.Settings;

namespace FileWheel.Conversion;

/// <summary>Assemblage du moteur de conversion complet (point d'entree unique pour l'application et les tests).</summary>
public sealed class ConversionEngine
{
    public DependencyManager Dependencies { get; }
    public ConversionRegistry Registry { get; }
    public ConversionManager Manager { get; }
    public JobQueue Queue { get; }
    public ArchiveService Archives { get; }
    public ConversionHistory History { get; }
    public PresetManager Presets { get; }
    public PdfiumProvider Pdfium { get; }

    public ConversionEngine(Func<ConversionSettings> settings, IEnumerable<IConversionProvider>? extraProviders = null, DependencyManager? deps = null)
    {
        Dependencies = deps ?? new DependencyManager();
        Archives = new ArchiveService(Dependencies, settings);
        Pdfium = new PdfiumProvider(Dependencies);
        var providers = new List<IConversionProvider>
        {
            new BuiltinDocumentProvider(),
            new DataProvider(),
            new MeshProvider(),
            new FontProvider(),
            new MailProvider(),
            new ArchiveConversionProvider(Archives, Dependencies),
            Pdfium,
            new FfmpegProvider(Dependencies),
            new ImageMagickProvider(Dependencies),
            new LibreOfficeProvider(Dependencies, Pdfium),
            new CalibreProvider(Dependencies),
        };
        if (extraProviders is not null) providers.AddRange(extraProviders);
        Registry = new ConversionRegistry(Dependencies, providers);
        var cfg = settings();
        History = new ConversionHistory { Enabled = cfg.HistoryEnabled, MaxEntries = cfg.HistoryMaxEntries };
        Presets = new PresetManager();
        Manager = new ConversionManager(Registry, History, settings);
        Queue = new JobQueue(Manager, cfg.MaxConcurrent);
        ProcessRunner.DefaultPriority = cfg.Priority;
    }

    /// <summary>Applique des parametres modifies (concurrence, priorite, historique).</summary>
    public void Apply(ConversionSettings cfg)
    {
        Queue.MaxConcurrent = cfg.MaxConcurrent;
        ProcessRunner.DefaultPriority = cfg.Priority;
        History.Enabled = cfg.HistoryEnabled;
        History.MaxEntries = cfg.HistoryMaxEntries;
    }
}
