namespace FileWheel.Core.Input;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public override string ToString() => $"[{Left},{Top} {Width}x{Height}]";
}

/// <summary>Un ecran tel que Windows le decrit, en pixels physiques (bureau virtuel).</summary>
public sealed record MonitorInfo(PixelRect Bounds, PixelRect WorkArea, int Dpi)
{
    public double Scale => Dpi / 96.0;
}

/// <summary>
/// Calculs d'emplacement de la roue, independants de Windows :
/// la roue est dimensionnee selon le DPI de l'ecran qui contient le curseur et
/// recadree pour rester entierement visible dans la zone de travail de cet ecran
/// (barre des taches exclue), y compris sur des ecrans a coordonnees negatives.
/// </summary>
public static class ScreenGeometry
{
    public static MonitorInfo MonitorAt(IReadOnlyList<MonitorInfo> monitors, int x, int y)
    {
        if (monitors.Count == 0) throw new ArgumentException("Aucun ecran.");
        foreach (var m in monitors)
            if (m.Bounds.Contains(x, y)) return m;
        // Curseur hors de tout ecran (transition) : ecran le plus proche.
        MonitorInfo best = monitors[0];
        long bestD = long.MaxValue;
        foreach (var m in monitors)
        {
            long cx = Math.Clamp(x, m.Bounds.Left, m.Bounds.Right - 1);
            long cy = Math.Clamp(y, m.Bounds.Top, m.Bounds.Bottom - 1);
            long d = (cx - x) * (cx - x) + (cy - y) * (cy - y);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    /// <summary>
    /// Rectangle (pixels physiques) de la fenetre de la roue.
    /// </summary>
    /// <param name="diameterDip">Diametre en pixels independants (96 ppp).</param>
    /// <param name="offsetXDip">Decalage du centre par rapport au curseur, en DIP.</param>
    public static PixelRect PlaceWheel(IReadOnlyList<MonitorInfo> monitors, int cursorX, int cursorY,
        double diameterDip, double offsetXDip, double offsetYDip, out MonitorInfo monitor)
    {
        monitor = MonitorAt(monitors, cursorX, cursorY);
        var work = monitor.WorkArea;
        int d = (int)Math.Round(diameterDip * monitor.Scale);
        d = Math.Min(d, Math.Min(work.Width, work.Height));
        int cx = cursorX + (int)Math.Round(offsetXDip * monitor.Scale);
        int cy = cursorY + (int)Math.Round(offsetYDip * monitor.Scale);
        int left = Math.Clamp(cx - d / 2, work.Left, work.Right - d);
        int top = Math.Clamp(cy - d / 2, work.Top, work.Bottom - d);
        return new PixelRect(left, top, left + d, top + d);
    }
}
