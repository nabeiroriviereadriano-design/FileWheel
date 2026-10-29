using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileWheel.App.Ui;

namespace FileWheel.App.Tools;

/// <summary>
/// Metadonnees d'une image : dimensions, appareil, date de prise de vue, logiciel, auteur et surtout
/// position GPS (signalee comme sensible). « Créer une copie sans métadonnées » réécrit les pixels seuls.
/// </summary>
public sealed class MetadataWindow : ToolWindow
{
    private readonly ImageIO.Loaded _src;

    public MetadataWindow(ImageIO.Loaded src) : base("Métadonnées", Path.GetFileName(src.Path), 980, 640)
    {
        _src = src;
        var picture = new Image { Source = ImageIO.Fit(src.Bitmap, 1400), Stretch = Stretch.Uniform, Margin = new Thickness(16) };
        PreviewHost.Child = picture;
        Side.Width = 340;

        var rows = Collect(src);
        Section("Fichier");
        foreach (var (k, v) in rows.Where(r => r.group == 0).Select(r => (r.key, r.value))) Add(Row(k, v));
        var photo = rows.Where(r => r.group == 1).ToList();
        Section("Prise de vue");
        if (photo.Count == 0) Add(Ui.Ui.Hint("Aucune information d'appareil."));
        foreach (var r in photo) Add(Row(r.key, r.value));
        var gps = rows.Where(r => r.group == 2).ToList();
        Section("Position GPS");
        if (gps.Count == 0) Add(Ui.Ui.Hint("Aucune position enregistrée dans ce fichier."));
        else
        {
            var warn = Ui.Ui.Text("Cette photo indique où elle a été prise. Pensez à créer une copie sans métadonnées avant de la partager.", Ui.Ui.Warning, 12);
            Add(warn);
            foreach (var r in gps) Add(Row(r.key, r.value));
        }
        var other = rows.Where(r => r.group == 3).ToList();
        if (other.Count > 0)
        {
            Section("Autres");
            foreach (var r in other) Add(Row(r.key, r.value));
        }
        Status.Text = gps.Count > 0 ? "⚠ Contient une position GPS" : $"{rows.Count(r => r.group > 0)} information(s) trouvée(s)";
        SetPrimary("Créer une copie sans métadonnées", Strip);
    }

    private static UIElement Row(string key, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var k = Ui.Ui.Text(key, Ui.Ui.Subtle, 12);
        var v = Ui.Ui.Text(value, null, 12);
        v.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(v, 1);
        g.Children.Add(k);
        g.Children.Add(v);
        return g;
    }

    /// <summary>(groupe, cle, valeur) : 0 fichier, 1 prise de vue, 2 GPS, 3 autres.</summary>
    private static List<(int group, string key, string value)> Collect(ImageIO.Loaded src)
    {
        var list = new List<(int, string, string)>
        {
            (0, "Dimensions", $"{src.Bitmap.PixelWidth} × {src.Bitmap.PixelHeight} px"),
            (0, "Poids", ImageIO.Size(src.Bytes)),
            (0, "Format", Path.GetExtension(src.Path).TrimStart('.').ToUpperInvariant()),
        };
        var m = src.Metadata;
        if (m is null) return list;
        void Try(int g, string k, Func<string?> get)
        {
            try { var v = get(); if (!string.IsNullOrWhiteSpace(v)) list.Add((g, k, v.Trim())); } catch { }
        }
        Try(1, "Appareil", () => string.Join(" ", new[] { m.CameraManufacturer, m.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s))));
        Try(1, "Date", () => m.DateTaken);
        Try(1, "Exposition", () => Rational(m, "/app1/ifd/exif/{ushort=33434}") is { } t ? (t < 1 ? $"1/{Math.Round(1 / t)} s" : $"{t:0.#} s") : null);
        Try(1, "Ouverture", () => Rational(m, "/app1/ifd/exif/{ushort=33437}") is { } f ? $"f/{f:0.0}" : null);
        Try(1, "ISO", () => Query(m, "/app1/ifd/exif/{ushort=34855}")?.ToString());
        Try(1, "Focale", () => Rational(m, "/app1/ifd/exif/{ushort=37386}") is { } fl ? $"{fl:0} mm" : null);
        Try(2, "Latitude", () => Gps(m, "/app1/ifd/gps/{ushort=2}", "/app1/ifd/gps/{ushort=1}"));
        Try(2, "Longitude", () => Gps(m, "/app1/ifd/gps/{ushort=4}", "/app1/ifd/gps/{ushort=3}"));
        Try(3, "Logiciel", () => m.ApplicationName);
        Try(3, "Auteur", () => m.Author is { Count: > 0 } a ? string.Join(", ", a) : null);
        Try(3, "Copyright", () => m.Copyright);
        Try(3, "Titre", () => m.Title);
        Try(3, "Commentaire", () => m.Comment);
        return list;
    }

    private static object? Query(BitmapMetadata m, string q)
    {
        try { return m.ContainsQuery(q) ? m.GetQuery(q) : null; } catch { return null; }
    }

    /// <summary>Rationnel EXIF (numerateur sur 32 bits bas, denominateur sur 32 bits hauts).</summary>
    private static double? Rational(BitmapMetadata m, string q) => Query(m, q) switch
    {
        ulong u => (uint)(u >> 32) == 0 ? null : (uint)u / (double)(uint)(u >> 32),
        long l => (int)(l >> 32) == 0 ? null : (int)l / (double)(int)(l >> 32),
        _ => null,
    };

    private static string? Gps(BitmapMetadata m, string valueQuery, string refQuery)
    {
        if (Query(m, valueQuery) is not ulong[] { Length: 3 } parts) return null;
        double D(ulong u) => (uint)(u >> 32) == 0 ? 0 : (uint)u / (double)(uint)(u >> 32);
        double deg = D(parts[0]) + D(parts[1]) / 60 + D(parts[2]) / 3600;
        var r = Query(m, refQuery) as string ?? "";
        return $"{deg:0.00000}° {r}".Trim();
    }

    private void Strip()
    {
        var ext = ImageIO.OutputExtension(_src.Path);
        // Seuls les pixels sont reecrits : aucune metadonnee n'est copiee dans le nouveau fichier.
        var data = ImageIO.Encode(_src.Bitmap, ext, 95);
        Done(ImageIO.SaveNew(data, ImageIO.OutputPath(_src.Path, "sans métadonnées", ext)));
    }
}
