using Mintrans.Core.Spatial;

namespace Mintrans.Core.Geometry;

/// <summary>
/// Точная проверка пересечения габаритов (docs/02в, раздел 3). Общая клетка индекса — только кандидат;
/// конфликт — когда реальные контуры заходят друг в друга. Касание без захода конфликтом не считается.
/// </summary>
public static class ShapeConflicts
{
    /// <summary>
    /// Пересекаются ли габариты. <paramref name="ignoreA"/> и <paramref name="ignoreB"/> — участки осей коридоров,
    /// где пересечение явно разрешено (общая зона стрелки или стыка): отрезки ломаной, целиком лежащие в них, не проверяются.
    /// </summary>
    public static bool Conflict(SpatialShape a, SpatialShape b,
        IReadOnlyList<PathInterval>? ignoreA = null, IReadOnlyList<PathInterval>? ignoreB = null)
    {
        if (!a.Bounds.Overlaps(b.Bounds))
            return false;
        return (a, b) switch
        {
            (CorridorShape ca, CorridorShape cb) => CorridorCorridor(ca, ignoreA, cb, ignoreB),
            (CorridorShape ca, AreaShape ab) => CorridorArea(ca, ignoreA, ab),
            (AreaShape aa, CorridorShape cb) => CorridorArea(cb, ignoreB, aa),
            (AreaShape aa, AreaShape ab) => aa.Shrunk.Overlaps(ab.Shrunk),
            _ => throw new NotSupportedException($"Нет проверки пересечения для {a.GetType().Name} и {b.GetType().Name}."),
        };
    }

    private static bool Ignored(FlatSegment segment, IReadOnlyList<PathInterval>? ignore) =>
        ignore is not null && ignore.Any(i => i.Covers(segment.FromS, segment.ToS, SpatialScale.EpsilonM));

    private static bool CorridorCorridor(CorridorShape a, IReadOnlyList<PathInterval>? ignoreA,
        CorridorShape b, IReadOnlyList<PathInterval>? ignoreB)
    {
        var limit = a.HalfWidthM + b.HalfWidthM - SpatialScale.EpsilonM;
        // Отрезки второго коридора раскладываются по клеткам: сравниваются только близкие пары.
        var buckets = Buckets(b, ignoreB);
        if (buckets.Count == 0)
            return false;
        var reach = a.HalfWidthM + b.HalfWidthM;
        foreach (var segment in a.Axis.Flat)
        {
            if (Ignored(segment, ignoreA))
                continue;
            foreach (var cell in SpatialScale.CellsTouching(segment.Bounds.Expand(reach)))
            {
                if (!buckets.TryGetValue(cell, out var near))
                    continue;
                if (near.Any(other => Distances.SegmentSegment(segment.A, segment.B, other.A, other.B) < limit))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Отрезки ломаной оси по клеткам, которых они касаются.</summary>
    private static Dictionary<Grid.GridPos, List<FlatSegment>> Buckets(CorridorShape shape, IReadOnlyList<PathInterval>? ignore)
    {
        var buckets = new Dictionary<Grid.GridPos, List<FlatSegment>>();
        foreach (var segment in shape.Axis.Flat)
        {
            if (Ignored(segment, ignore))
                continue;
            foreach (var cell in SpatialScale.CellsTouching(segment.Bounds))
            {
                if (!buckets.TryGetValue(cell, out var list))
                    buckets[cell] = list = [];
                list.Add(segment);
            }
        }
        return buckets;
    }

    private static bool CorridorArea(CorridorShape corridor, IReadOnlyList<PathInterval>? ignore, AreaShape area)
    {
        var limit = corridor.HalfWidthM - SpatialScale.EpsilonM;
        var near = area.Rect.Expand(corridor.HalfWidthM);
        foreach (var segment in corridor.Axis.Flat)
        {
            if (!segment.Bounds.Touches(near) || Ignored(segment, ignore))
                continue;
            var distance = Distances.SegmentRect(segment.A, segment.B, area.Rect);
            if (distance < limit || (distance == 0 && Distances.SegmentRect(segment.A, segment.B, area.Shrunk) == 0))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Сколько метров от точки <paramref name="fromS"/> оси коридора <paramref name="moving"/> в сторону <paramref name="direction"/>
    /// (+1 — к концу, −1 — к началу) его габарит пересекается с <paramref name="other"/>. Служит для зоны стрелки и стыка:
    /// у общей точки пути сходятся, дальше расходятся. Предполагается, что расхождение монотонно.
    /// Результат с запасом в один отрезок ломаной, чтобы зона целиком покрывала пограничный отрезок.
    /// </summary>
    public static double OverlapLength(CorridorShape moving, double fromS, int direction, CorridorShape other, double stepM = 0.25)
    {
        var required = moving.HalfWidthM + other.HalfWidthM;
        var end = direction > 0 ? moving.Axis.Length : 0;
        var available = Math.Abs(end - fromS);
        var buckets = Buckets(other, null);
        bool Clear(WorldPoint p)
        {
            var near = new WorldBounds(p.X, p.Y, p.X, p.Y).Expand(required);
            foreach (var cell in SpatialScale.CellsTouching(near))
                if (buckets.TryGetValue(cell, out var segments) &&
                    segments.Any(seg => Distances.PointSegment(p, seg.A, seg.B) < required + SpatialScale.EpsilonM))
                    return false;
            return true;
        }
        for (var travelled = 0.0; travelled <= available; travelled += stepM)
            if (Clear(moving.Axis.PointAt(fromS + direction * travelled)))
                return Math.Min(available, travelled + PathGeometry.FlattenSegmentM);
        return available;
    }
}
