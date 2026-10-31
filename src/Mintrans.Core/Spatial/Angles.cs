namespace Mintrans.Core.Spatial;

/// <summary>
/// Углы в радианах. Направление 0 — восток (+X), рост угла — поворот к +Y (югу), то есть по часовой стрелке
/// на карте с севером вверху.
/// </summary>
public static class Angles
{
    public const double TwoPi = 2 * Math.PI;

    /// <summary>Угол в диапазоне [0, 2π).</summary>
    public static double Normalize2Pi(double angle)
    {
        var a = angle % TwoPi;
        if (a < 0)
            a += TwoPi;
        return a >= TwoPi ? 0 : a;
    }

    /// <summary>Угол в диапазоне (−π, π].</summary>
    public static double NormalizePi(double angle)
    {
        var a = Normalize2Pi(angle);
        return a > Math.PI ? a - TwoPi : a;
    }

    /// <summary>Наименьшая разница направлений, без знака: 0…π.</summary>
    public static double Difference(double a, double b) => Math.Abs(NormalizePi(a - b));

    public static double Reverse(double heading) => Normalize2Pi(heading + Math.PI);

    public static double ToRadians(double degrees) => degrees * Math.PI / 180;

    public static double ToDegrees(double radians) => radians * 180 / Math.PI;
}

/// <summary>
/// 16 заранее нарисованных ракурсов подвижного состава с шагом 22,5° (docs/10в, раздел 6).
/// dir00 — мировой восток (+X), номера растут против часовой стрелки на карте с севером вверху:
/// dir04 — север, dir08 — запад, dir12 — юг. Рельсы при этом остаются плавными: 16 направлений ограничивают только спрайты.
/// </summary>
public static class SpriteDirections
{
    public const int Count = 16;

    /// <summary>Ближайший ракурс к направлению движения.</summary>
    public static int Index(double headingRad)
    {
        // Рост мирового угла — к югу, а номера ракурсов растут к северу: знак меняется.
        var index = (int)Math.Round(-headingRad * Count / Angles.TwoPi) % Count;
        return (index + Count) % Count;
    }

    /// <summary>Мировое направление ракурса.</summary>
    public static double Heading(int index) => Angles.Normalize2Pi(-index * Angles.TwoPi / Count);
}
