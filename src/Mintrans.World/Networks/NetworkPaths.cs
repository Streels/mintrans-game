using Mintrans.Core.Grid;

namespace Mintrans.World.Networks;

/// <summary>Форма прокладываемого пути: цепочки соседних клеток для <see cref="NetworkLayer.LayPath"/>.</summary>
public static class NetworkPaths
{
    /// <summary>
    /// Цепочка соседних (через сторону) клеток по ломаной. Каждый отрезок — горизонталь, вертикаль
    /// или ровно 45°; диагональ становится лесенкой: шаг по X, затем по Y (как в OpenTTD).
    /// </summary>
    public static List<GridPos> Polyline(IReadOnlyList<GridPos> points)
    {
        if (points.Count < 2)
            throw new ArgumentException("Нужно хотя бы две точки.");
        var path = new List<GridPos> { points[0] };
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var (dx, dy) = (Math.Sign(b.X - a.X), Math.Sign(b.Y - a.Y));
            var (lx, ly) = (Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            if (lx == 0 && ly == 0)
                throw new ArgumentException($"Точка {a} повторяется.");
            if (lx != 0 && ly != 0 && lx != ly)
                throw new ArgumentException($"Отрезок {a} → {b} должен быть горизонтальным, вертикальным или под 45°.");

            var cell = a;
            if (lx == ly)
            {
                for (var s = 0; s < lx; s++)
                {
                    path.Add(cell = new GridPos(cell.X + dx, cell.Y));
                    path.Add(cell = new GridPos(cell.X, cell.Y + dy));
                }
            }
            else
            {
                for (var s = 0; s < lx + ly; s++)
                    path.Add(cell = new GridPos(cell.X + dx, cell.Y + dy));
            }
        }
        return path;
    }

    /// <summary>
    /// Путь при протягивании мышью от <paramref name="from"/> до <paramref name="to"/>:
    /// сначала прямо вдоль длинной оси, затем диагональ под 45° до конечной клетки.
    /// Пустой список, если начальная и конечная клетки совпадают.
    /// </summary>
    public static List<GridPos> Drag(GridPos from, GridPos to)
    {
        if (from == to)
            return [];
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
        var straight = Math.Abs(ax - ay);
        if (straight == 0 || ax == 0 || ay == 0)
            return Polyline([from, to]);
        var bend = ax > ay
            ? new GridPos(from.X + Math.Sign(dx) * straight, from.Y)
            : new GridPos(from.X, from.Y + Math.Sign(dy) * straight);
        return Polyline([from, bend, to]);
    }
}
