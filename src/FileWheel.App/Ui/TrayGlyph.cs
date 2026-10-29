using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileWheel.App.Ui;

public enum TrayBadge { None, Success, Error }

/// <summary>Etat affiche par l'icone de la zone de notification.</summary>
public readonly record struct TrayState(
    bool LightTaskbar,      // barre des taches claire => glyphe noir ; sombre => glyphe blanc
    bool Disabled,          // roue desactivee => icone grisee
    double? Progress,       // 0..1 : anneau de progression ; null avec Busy => animation
    bool Busy,              // une tache est en cours
    int Phase,              // etape de l'animation (tache sans progression connue)
    TrayBadge Badge)        // pastille de fin (verte / rouge) jusqu'au prochain clic sur l'icone
{
    public string Key => $"{LightTaskbar}|{Disabled}|{(Busy ? Progress is { } p ? Math.Round(p * 24).ToString() : "~" + Phase % 6 : "-")}|{Badge}";
}

/// <summary>
/// Dessine l'icone monochrome de la zone de notification, dans le style des icones systeme de Windows 11 :
/// la roue a 6 secteurs du logo, blanche sur barre sombre, noire sur barre claire.
/// - progression : les secteurs se remplissent dans le sens horaire a partir du haut ;
/// - tache sans progression connue : un secteur plein tourne ;
/// - roue desactivee : icone grisee ;
/// - pastille verte (termine) ou rouge (echec) en bas a droite.
/// </summary>
public static class TrayGlyph
{
    public static (byte[] Png, byte[] Bgra) Render(TrayState st, int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double s = size, c = s / 2;
            var fg = st.LightTaskbar ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White;
            double baseOpacity = st.Disabled ? 0.38 : 1.0;
            double ro = s * 0.47, ri = s * 0.255, gap = size <= 16 ? 18 : 14;

            bool partial = st.Busy;
            double filled = st.Progress is { } p ? Math.Clamp(p, 0, 1) * 360 : -1;
            for (int i = 0; i < 6; i++)
            {
                double a0 = -30 + i * 60 + gap / 2, a1 = a0 + 60 - gap;
                if (!partial)
                {
                    Fill(dc, Sector(c, c, ro, ri, a0, a1), fg, baseOpacity);
                    continue;
                }
                // Fond attenue, puis la part « faite » en plein
                Fill(dc, Sector(c, c, ro, ri, a0, a1), fg, 0.32 * baseOpacity);
                if (filled >= 0)
                {
                    // Le remplissage part du debut du secteur du haut et tourne dans le sens horaire.
                    double end = -30 + filled;
                    if (end > a0) Fill(dc, Sector(c, c, ro, ri, a0, Math.Min(a1, end)), fg, baseOpacity);
                }
                else if (i == ((st.Phase % 6) + 6) % 6)
                {
                    Fill(dc, Sector(c, c, ro, ri, a0, a1), fg, baseOpacity);
                }
            }
            dc.DrawEllipse(new SolidColorBrush(fg) { Opacity = baseOpacity }, null, new Point(c, c), s * 0.105, s * 0.105);

            if (st.Badge != TrayBadge.None)
            {
                double r = Math.Max(3, s * 0.22);
                var center = new Point(s - r, s - r);
                var badge = st.Badge == TrayBadge.Success ? Color.FromRgb(0x30, 0xC2, 0x5A) : Color.FromRgb(0xF0, 0x3E, 0x33);
                // Liseré de la couleur de la barre des taches : la pastille se detache du glyphe
                var ring = st.LightTaskbar ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x1C, 0x1C, 0x1C);
                dc.DrawEllipse(new SolidColorBrush(ring), null, center, r + Math.Max(1, s / 16), r + Math.Max(1, s / 16));
                dc.DrawEllipse(new SolidColorBrush(badge), null, center, r, r);
            }
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);

        var straight = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        var bgra = new byte[size * size * 4];
        straight.CopyPixels(bgra, size * 4, 0);
        return (ms.ToArray(), bgra);
    }

    private static void Fill(DrawingContext dc, Geometry g, Color c, double opacity) =>
        dc.DrawGeometry(new SolidColorBrush(c) { Opacity = opacity }, null, g);

    /// <summary>Secteur d'anneau ; angles en degres, 0 = haut, sens horaire.</summary>
    private static Geometry Sector(double cx, double cy, double ro, double ri, double a0, double a1)
    {
        if (a1 <= a0) return Geometry.Empty;
        Point P(double r, double a)
        {
            double t = (a - 90) * Math.PI / 180;
            return new Point(cx + r * Math.Cos(t), cy + r * Math.Sin(t));
        }
        bool large = a1 - a0 > 180;
        var fig = new PathFigure { StartPoint = P(ro, a0), IsClosed = true };
        fig.Segments.Add(new ArcSegment(P(ro, a1), new Size(ro, ro), 0, large, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(ri, a1), true));
        fig.Segments.Add(new ArcSegment(P(ri, a0), new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }
}
