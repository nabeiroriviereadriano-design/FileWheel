using System.Text.Json;
using System.Text.Json.Serialization;
using FileWheel.Core.Logging;

namespace FileWheel.Core.Util;

/// <summary>
/// Lecture/ecriture JSON atomique : on ecrit dans un fichier voisin puis on remplace.
/// Une coupure de courant pendant l'enregistrement ne laisse jamais un fichier tronque.
/// Un fichier illisible est mis de cote (.corrompu-AAAAMMJJ) au lieu d'etre ecrase en silence.
/// </summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (Exception ex)
        {
            Log.Warn($"Fichier de configuration illisible ({Path.GetFileName(path)}) : {ex.Message}. Valeurs par défaut utilisées.");
            try
            {
                var backup = path + ".corrompu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, backup, overwrite: false);
            }
            catch { /* tant pis */ }
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(tmp, json);
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
