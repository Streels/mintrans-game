using Mintrans.Core.Spatial;

namespace Mintrans.Core.Geometry;

/// <summary>Точные расстояния между точками, отрезками и прямоугольниками, м.</summary>
public static class Distances
{
    public static double PointSegment(WorldPoint p, WorldPoint a, WorldPoint b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared;
        if (lengthSquared == 0)
            return p.DistanceTo(a);
        var t = Math.Clamp((p - a).Dot(ab) / lengthSquared, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    public static double SegmentSegment(WorldPoint a0, WorldPoint a1, WorldPoint b0, WorldPoint b1)
    {
        if (SegmentsIntersect(a0, a1, b0, b1))
            return 0;
        return Math.Min(
            Math.Min(PointSegment(a0, b0, b1), PointSegment(a1, b0, b1)),
            Math.Min(PointSegment(b0, a0, a1), PointSegment(b1, a0, a1)));
    }

    /// <summary>Пересекаются ли или касаются отрезки.</summary>
    public static bool SegmentsIntersect(WorldPoint a0, WorldPoint a1, WorldPoint b0, WorldPoint b1)
    {
        var d1 = (a1 - a0).Cross(b0 - a0);
        var d2 = (a1 - a0).Cross(b1 - a0);
        var d3 = (b1 - b0).Cross(a0 - b0);
        var d4 = (b1 - b0).Cross(a1 - b0);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;
        return (d1 == 0 && OnSegment(a0, a1, b0)) || (d2 == 0 && OnSegment(a0, a1, b1)) ||
               (d3 == 0 && OnSegment(b0, b1, a0)) || (d4 == 0 && OnSegment(b0, b1, a1));
    }

    private static bool OnSegment(WorldPoint a, WorldPoint b, WorldPoint p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);

    public static double PointRect(WorldPoint p, WorldBounds r)
    {
        var dx = Math.Max(Math.Max(r.MinX - p.X, 0), p.X - r.MaxX);
        var dy = Math.Max(Math.Max(r.MinY - p.Y, 0), p.Y - r.MaxY);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Расстояние от отрезка до прямоугольника; 0, если отрезок заходит внутрь или касается.</summary>
    public static double SegmentRect(WorldPoint a, WorldPoint b, WorldBounds r)
    {
        if (ClipsRect(a, b, r))
            return 0;
        var corners = new[]
        {
            new WorldPoint(r.MinX, r.MinY), new WorldPoint(r.MaxX, r.MinY),
            new WorldPoint(r.MaxX, r.MaxY), new WorldPoint(r.MinX, r.MaxY),
        };
        var best = Math.Min(PointRect(a, r), PointRect(b, r));
        foreach (var corner in corners)
            best = Math.Min(best, PointSegment(corner, a, b));
        return best;
    }

    /// <summary>Отсечение Лианга — Барски: есть ли у отрезка общая точка с прямоугольником.</summary>
    private static bool ClipsRect(WorldPoint a, WorldPoint b, WorldBounds r)
    {
        double t0 = 0, t1 = 1;
        var d = b - a;
        bool Clip(double p, double q)
        {
            if (p == 0)
                return q >= 0;
            var t = q / p;
            if (p < 0)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }
        return Clip(-d.X, a.X - r.MinX) && Clip(d.X, r.MaxX - a.X) &&
               Clip(-d.Y, a.Y - r.MinY) && Clip(d.Y, r.MaxY - a.Y);
    }
}
