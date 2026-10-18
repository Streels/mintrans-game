using Mintrans.Core.Grid;

namespace Mintrans.Data.Maps;

/// <summary>Геометрия для операций карты: какие клетки входят в фигуру. Всё обрезается границами карты.</summary>
internal static class Shapes
{
    public static IEnumerable<GridPos> Clip(GridRect bounds, IEnumerable<GridPos> cells) => cells.Where(bounds.Contains);

    /// <summary>Клетки эллипса и нормированное расстояние до центра (0 — центр, 1 — край).</summary>
    public static IEnumerable<(GridPos Cell, double Distance)> Ellipse(GridRect bounds, GridPos center, int radiusX, int radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0)
            throw new ArgumentException($"Радиусы эллипса должны быть положительными: {radiusX}, {radiusY}.");
        for (var y = center.Y - radiusY; y <= center.Y + radiusY; y++)
        for (var x = center.X - radiusX; x <= center.X + radiusX; x++)
        {
            var dx = (x - center.X) / (double)radiusX;
            var dy = (y - center.Y) / (double)radiusY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var cell = new GridPos(x, y);
            if (distance <= 1.0 && bounds.Contains(cell))
                yield return (cell, distance);
        }
    }

    /// <summary>Клетки широкой ломаной: всё, что ближе width/2 к любому её отрезку.</summary>
    public static IEnumerable<GridPos> ThickPolyline(GridRect bounds, IReadOnlyList<GridPos> points, double width)
    {
        if (points.Count < 2)
            throw new ArgumentException("Ломаной нужно хотя бы две точки.");
        if (width <= 0)
            throw new ArgumentException($"Ширина должна быть положительной: {width}.");
        var half = width / 2.0;
        var cells = new HashSet<GridPos>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var pad = (int)Math.Ceiling(half);
            var minX = Math.Min(a.X, b.X) - pad;
            var maxX = Math.Max(a.X, b.X) + pad;
            var minY = Math.Min(a.Y, b.Y) - pad;
            var maxY = Math.Max(a.Y, b.Y) + pad;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var cell = new GridPos(x, y);
                if (bounds.Contains(cell) && DistanceToSegment(cell, a, b) <= half)
                    cells.Add(cell);
            }
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
