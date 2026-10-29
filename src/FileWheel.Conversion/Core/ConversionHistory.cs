using FileWheel.Core.Util;

namespace FileWheel.Conversion.Core;

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Date { get; set; } = DateTime.Now;
    public List<string> Inputs { get; set; } = new();
    public string SourceFormat { get; set; } = "";
    public string TargetFormat { get; set; } = "";
    public string CapabilityKey { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new();
    public double DurationSeconds { get; set; }
    public string Result { get; set; } = "";      // Réussie, Échec, Annulée, Ignorée
    public string? Error { get; set; }
    public List<string> Outputs { get; set; } = new();

    public string FileName => Inputs.Count == 0 ? "" : Path.GetFileName(Inputs[0]) + (Inputs.Count > 1 ? $" (+{Inputs.Count - 1})" : "");
    public string? OutputFolder => Outputs.Count == 0 ? null : Path.GetDirectoryName(Outputs[0]);
}

/// <summary>
/// Historique des conversions (si l'utilisateur l'autorise). Ne contient que des metadonnees :
/// chemins, formats, duree, resultat. Jamais le contenu des fichiers.
/// </summary>
public sealed class ConversionHistory
{
    private readonly object _gate = new();
    private List<HistoryEntry> _entries;
    private readonly string _file;

    public ConversionHistory(string? file = null)
    {
        _file = file ?? AppPaths.HistoryFile;
        _entries = JsonStore.Load(_file, () => new List<HistoryEntry>());
    }

    public bool Enabled { get; set; } = true;
    public int MaxEntries { get; set; } = 2000;
    public event Action? Changed;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_gate) return _entries.OrderByDescending(e => e.Date).ToList(); }
    }

    public void Add(HistoryEntry e)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            _entries.Add(e);
            if (_entries.Count > MaxEntries) _entries = _entries.OrderByDescending(x => x.Date).Take(MaxEntries).ToList();
            Save();
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<HistoryEntry> Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Entries;
        return Entries.Where(e =>
            e.FileName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            e.SourceFormat.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            e.TargetFormat.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            e.Result.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            (e.Error?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    public void Remove(string id)
    {
        lock (_gate) { _entries.RemoveAll(e => e.Id == id); Save(); }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) { _entries.Clear(); Save(); }
        Changed?.Invoke();
    }

    private void Save() => JsonStore.Save(_file, _entries);
}

/// <summary>Profils enregistres par l'utilisateur (format cible + reglages).</summary>
public sealed class ConversionPreset
{
    public string Name { get; set; } = "";
    public string CapabilityKey { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new();
}

public sealed class PresetManager
{
    private readonly string _file;
    private List<ConversionPreset> _presets;

    public PresetManager(string? file = null)
    {
        _file = file ?? AppPaths.PresetsFile;
        _presets = JsonStore.Load(_file, () => new List<ConversionPreset>());
    }

    public IReadOnlyList<ConversionPreset> All => _presets;

    public IReadOnlyList<ConversionPreset> For(string capabilityKey) => _presets.Where(p => p.CapabilityKey == capabilityKey).ToList();

    public void Save(ConversionPreset p)
    {
        _presets.RemoveAll(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && x.CapabilityKey == p.CapabilityKey);
        _presets.Add(p);
        JsonStore.Save(_file, _presets);
    }

    public void Delete(string name, string capabilityKey)
    {
        _presets.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.CapabilityKey == capabilityKey);
        JsonStore.Save(_file, _presets);
    }
}
