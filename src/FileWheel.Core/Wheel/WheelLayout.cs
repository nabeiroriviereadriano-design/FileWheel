namespace FileWheel.Core.Wheel;

/// <summary>
/// Geometrie de la roue : anneau de N secteurs autour d'un disque central.
/// Le premier secteur est centre en haut, les suivants dans le sens horaire.
/// Coordonnees exprimees par rapport au centre, axe Y vers le bas (convention ecran).
/// </summary>
public sealed class WheelLayout
{
    public const int Center = -1;
    public const int Outside = -2;

    public double OuterRadius { get; }
    public double InnerRadius { get; }
    public int Count { get; }

    public WheelLayout(double outerRadius, double innerRadius, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (innerRadius <= 0 || innerRadius >= outerRadius) throw new ArgumentOutOfRangeException(nameof(innerRadius));
        OuterRadius = outerRadius;
        InnerRadius = innerRadius;
        Count = count;
    }

    /// <summary>Largeur angulaire d'un secteur, en degres.</summary>
    public double SweepDegrees => 360.0 / Count;

    /// <summary>Angle de debut du secteur i (degres, 0 = droite, sens horaire).</summary>
    public double StartAngle(int i) => -90 - SweepDegrees / 2 + i * SweepDegrees;

    public double MidAngle(int i) => -90 + i * SweepDegrees;

    /// <summary>Indice du secteur sous le point, Center (disque central) ou Outside.</summary>
    public int HitTest(double dx, double dy)
    {
        double r = Math.Sqrt(dx * dx + dy * dy);
        if (r > OuterRadius) return Outside;
        if (r < InnerRadius) return Center;
        double deg = Math.Atan2(dy, dx) * 180 / Math.PI;           // -180..180, 0 = droite
        double fromStart = deg - (-90 - SweepDegrees / 2);          // 0 au debut du secteur 0
        fromStart = ((fromStart % 360) + 360) % 360;
        int idx = (int)(fromStart / SweepDegrees);
        return Math.Clamp(idx, 0, Count - 1);
    }

    /// <summary>Point au milieu du secteur i (position de l'icone).</summary>
    public (double X, double Y) IconPosition(int i, double radialFraction = 0.5)
    {
        double r = InnerRadius + (OuterRadius - InnerRadius) * radialFraction;
        double a = MidAngle(i) * Math.PI / 180;
        return (r * Math.Cos(a), r * Math.Sin(a));
    }
}
