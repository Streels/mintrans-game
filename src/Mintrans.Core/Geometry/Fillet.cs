using Mintrans.Core.Spatial;

namespace Mintrans.Core.Geometry;

/// <summary>Построение плавного пути по ломаной: углы скругляются дугами одного радиуса (касательные к сторонам).</summary>
public static class Fillet
{
    /// <summary>
    /// Путь через точки <paramref name="points"/> со скруглёнными углами радиуса <paramref name="radiusM"/>.
    /// Ошибка, если дуга не помещается на сторонах ломаной или точки повторяются.
    /// </summary>
    public static PathGeometry Polyline(IReadOnlyList<WorldPoint> points, double radiusM)
    {
        if (points.Count < 2)
            throw new ArgumentException("Нужно хотя бы две точки.");
        for (var i = 1; i < points.Count; i++)
            if (points[i].ApproxEquals(points[i - 1], SpatialScale.EpsilonM))
                throw new ArgumentException($"Точка {points[i]} повторяется.");

        // Для каждого внутреннего угла — касательные точки на соседних сторонах.
        var tangents = new double[points.Count];
        for (var i = 1; i + 1 < points.Count; i++)
        {
            var (d1, d2) = ((points[i] - points[i - 1]).Normalized(), (points[i + 1] - points[i]).Normalized());
            var turn = Math.Abs(Angles.NormalizePi(d2.Angle - d1.Angle));
            if (turn > Math.PI - 1e-6)
                throw new ArgumentException($"Ломаная разворачивается назад в {points[i]}.");
            tangents[i] = radiusM * Math.Tan(turn / 2);
        }
        for (var i = 1; i < points.Count; i++)
        {
            var side = points[i - 1].DistanceTo(points[i]);
            if (tangents[i - 1] + tangents[i] > side + SpatialScale.EpsilonM)
                throw new ArgumentException(
                    $"Дуга радиуса {radiusM:0} м не помещается на стороне {points[i - 1]} — {points[i]}: нужно не меньше {tangents[i - 1] + tangents[i]:0} м.");
        }

        var primitives = new List<PathPrimitive>();
        var at = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            var d1 = (points[i] - points[i - 1]).Normalized();
            var until = points[i] - d1 * tangents[i];
            if (at.DistanceTo(until) > SpatialScale.EpsilonM)
                primitives.Add(new StraightPrimitive(at, until));
            at = until;
            if (i + 1 >= points.Count || tangents[i] <= SpatialScale.EpsilonM)
                continue;
            var d2 = (points[i + 1] - points[i]).Normalized();
            var sign = d1.Cross(d2) >= 0 ? 1 : -1;
            var center = until + d1.Perpendicular(sign) * radiusM;
            var sweep = sign * Math.Abs(Angles.NormalizePi(d2.Angle - d1.Angle));
            var arc = new ArcPrimitive(center, radiusM, (until - center).Angle, sweep);
            primitives.Add(arc);
            at = arc.End;
        }
        return new PathGeometry(primitives);
    }
}
