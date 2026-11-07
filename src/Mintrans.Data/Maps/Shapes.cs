using Mintrans.Core.Grid;
using Mintrans.World;

namespace Mintrans.Data.Maps;

/// <summary>
/// Геометрия для операций карты: какие ячейки рельефа входят в фигуру. Каждая ячейка представлена своей центральной
/// строительной клеткой; всё обрезается границами карты.
/// </summary>
internal static class Shapes
{
    /// <summary>Ячейки эллипса и нормированное расстояние до центра (0 — центр, 1 — край).</summary>
    public static IEnumerable<(GridPos Cell, double Distance)> Ellipse(WorldMap map, GridPos center, int radiusX, int radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0)
            throw new ArgumentException($"Радиусы эллипса должны быть положительными: {radiusX}, {radiusY}.");
        var box = new GridRect(center.X - radiusX, center.Y - radiusY, 2 * radiusX + 1, 2 * radiusY + 1);
        foreach (var cell in map.Terrain.Samples(box))
        {
            var dx = (cell.X - center.X) / (double)radiusX;
            var dy = (cell.Y - center.Y) / (double)radiusY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= 1.0)
                yield return (cell, distance);
        }
    }

    /// <summary>Ячейки широкой ломаной: всё, что ближе width/2 к любому её отрезку (ширина — не меньше ячейки рельефа).</summary>
    public static IEnumerable<GridPos> ThickPolyline(WorldMap map, IReadOnlyList<GridPos> points, double width)
    {
        if (points.Count < 2)
            throw new ArgumentException("Ломаной нужно хотя бы две точки.");
        if (width <= 0)
            throw new ArgumentException($"Ширина должна быть положительной: {width}.");
        var half = Math.Max(width, map.Terrain.Step) / 2.0;
        var cells = new HashSet<GridPos>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var pad = (int)Math.Ceiling(half);
            var box = new GridRect(Math.Min(a.X, b.X) - pad, Math.Min(a.Y, b.Y) - pad,
                Math.Abs(a.X - b.X) + 2 * pad + 1, Math.Abs(a.Y - b.Y) + 2 * pad + 1);
            foreach (var cell in map.Terrain.Samples(box))
                if (DistanceToSegment(cell, a, b) <= half)
                    cells.Add(cell);
        }
        // Сортируем, чтобы результат не зависел от порядка обхода HashSet.
        return cells.OrderBy(c => c.Y).ThenBy(c => c.X);
    }

    private static double DistanceToSegment(GridPos p, GridPos a, GridPos b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double wx = p.X - a.X, wy = p.Y - a.Y;
        var lengthSquared = vx * vx + vy * vy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp((wx * vx + wy * vy) / lengthSquared, 0, 1);
        double ex = wx - t * vx, ey = wy - t * vy;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
