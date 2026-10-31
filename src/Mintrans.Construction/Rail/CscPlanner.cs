using Mintrans.Core.Geometry;
using Mintrans.Core.Spatial;

namespace Mintrans.Construction.Rail;

/// <summary>Положение и направление движения в точке, м и рад.</summary>
public readonly record struct Pose(WorldPoint At, double Heading);

/// <summary>
/// Семейство соединения «дуга — прямая — дуга» (docs/10в, раздел 2). L — поворот налево по ходу движения А→Б
/// (против часовой стрелки на карте с севером вверху), R — направо. Порядок перечисления — порядок выбора при равенстве.
/// </summary>
public enum CscFamily
{
    LSL,
    LSR,
    RSL,
    RSR,
}

/// <summary>Вариант соединения: семейство, радиус, геометрия и углы дуг (без знака, рад).</summary>
public sealed record CscCandidate(CscFamily Family, double RadiusM, PathGeometry Geometry, double FirstSweep, double SecondSweep)
{
    public double LengthM => Geometry.Length;
}

/// <summary>
/// Аналитическое построение соединений CSC между двумя ориентированными точками: дуга радиуса R, касательная прямая,
/// дуга того же радиуса. Нулевая дуга допустима — так получаются прямая и одна дуга. Геометрия считается в метрах,
/// без «лесенки» клеток; результат проверяется по совпадению конца.
/// </summary>
public static class CscPlanner
{
    public static IEnumerable<CscCandidate> Plan(Pose start, Pose end, double radiusM, double epsilonM = SpatialScale.EpsilonM)
    {
        foreach (var family in Enum.GetValues<CscFamily>())
            if (Build(family, start, end, radiusM, epsilonM) is { } candidate)
                yield return candidate;
    }

    /// <summary>Знак поворота в мировых углах: рост угла — к +Y, то есть направо на карте с севером вверху.</summary>
    private static (int First, int Second) Signs(CscFamily family) => family switch
    {
        CscFamily.LSL => (-1, -1),
        CscFamily.LSR => (-1, +1),
        CscFamily.RSL => (+1, -1),
        CscFamily.RSR => (+1, +1),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    public static CscCandidate? Build(CscFamily family, Pose start, Pose end, double r, double epsilonM = SpatialScale.EpsilonM)
    {
        var (s0, s1) = Signs(family);
        // Центр поворота — сбоку от направления движения, в сторону поворота.
        var c0 = start.At + WorldPoint.FromAngle(start.Heading).Perpendicular(s0) * r;
        var c1 = end.At + WorldPoint.FromAngle(end.Heading).Perpendicular(s1) * r;
        var v = c1 - c0;
        var d = v.Length;

        double heading, straight;
        if (s0 == s1)
        {
            // Внешняя касательная: прямая параллельна линии центров.
            if (d <= epsilonM)
            {
                heading = start.Heading;
                straight = 0;
            }
            else
            {
                heading = v.Angle;
                straight = d;
            }
        }
        else
        {
            // Внутренняя касательная: окружности не должны пересекаться.
            if (d < 2 * r - epsilonM)
                return null;
            straight = Math.Sqrt(Math.Max(0, d * d - 4 * r * r));
            heading = v.Angle + Math.Atan2(2 * r * s0, straight);
        }

        var sweep0 = Sweep(start.Heading, heading, s0);
        var sweep1 = Sweep(heading, end.Heading, s1);
        var t0 = c0 - WorldPoint.FromAngle(heading).Perpendicular(s0) * r;
        var t1 = t0 + WorldPoint.FromAngle(heading) * straight;

        var primitives = new List<PathPrimitive>();
        if (sweep0 * r > epsilonM)
            primitives.Add(Arc(c0, r, start.At, s0 * sweep0));
        if (straight > epsilonM)
            primitives.Add(new StraightPrimitive(primitives.Count > 0 ? primitives[^1].End : start.At, t1));
        if (sweep1 * r > epsilonM)
            primitives.Add(Arc(c1, r, primitives.Count > 0 ? primitives[^1].End : t1, s1 * sweep1));
        if (primitives.Count == 0)
            return null;

        // Последняя точка должна совпасть с концом Б, направление — с его направлением.
        var last = primitives[^1];
        if (!last.End.ApproxEquals(end.At, 10 * epsilonM) ||
            Angles.Difference(last.EndHeading, end.Heading) > 1e-6 ||
            Angles.Difference(primitives[0].StartHeading, start.Heading) > 1e-6)
            return null;
        try
        {
            return new CscCandidate(family, r, new PathGeometry(primitives), sweep0, sweep1);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Угол поворота без знака от <paramref name="from"/> до <paramref name="to"/> в сторону <paramref name="sign"/>: 0…2π.</summary>
    private static double Sweep(double from, double to, int sign)
    {
        var sweep = Angles.Normalize2Pi(sign * (to - from));
        return sweep > Angles.TwoPi - 1e-9 ? 0 : sweep;
    }

    private static ArcPrimitive Arc(WorldPoint center, double r, WorldPoint from, double sweep) =>
        new(center, r, (from - center).Angle, sweep);
}
