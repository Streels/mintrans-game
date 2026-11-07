using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;

namespace Mintrans.World.Rail;

/// <summary>
/// Переезд (docs/10а): клетка дороги, которую пересекает путь на земле, и участок пути над ней.
/// Переезд не соединяет дорогу с рельсами: машины ждут поезд, поезд — машину, уже стоящую на переезде.
/// </summary>
/// <param name="FromM">Начало участка пути в клетке переезда, м от начала пути (с запасом на ширину коридора).</param>
/// <param name="RoadAlongY">Дорога идёт с севера на юг (иначе — с запада на восток).</param>
public sealed record RailCrossing(GridPos Cell, TrackEdgeId Edge, double FromM, double ToM, bool RoadAlongY);

/// <summary>
/// Все переезды карты: клетки клеточных сетей (дорог), через которые проходят рельсы на земле.
/// Вычисляются из геометрии и пересобираются, когда меняются рельсы или дороги.
/// </summary>
public sealed class RailCrossings(WorldMap map)
{
    private readonly List<RailCrossing> _all = [];
    private readonly Dictionary<GridPos, List<RailCrossing>> _byCell = new();
    private readonly Dictionary<TrackEdgeId, List<RailCrossing>> _byEdge = new();
    private int _builtRevision = -1;

    public IReadOnlyList<RailCrossing> All
    {
        get
        {
            Refresh();
            return _all;
        }
    }

    public IReadOnlyList<RailCrossing> At(GridPos cell)
    {
        Refresh();
        return _byCell.TryGetValue(cell, out var list) ? list : [];
    }

    public IReadOnlyList<RailCrossing> On(TrackEdgeId edge)
    {
        Refresh();
        return _byEdge.TryGetValue(edge, out var list) ? list : [];
    }

    public bool IsCrossing(GridPos cell) => At(cell).Count > 0;

    private int Revision() => (map.Rails?.Revision ?? 0) * 7919 + map.Networks.Layers.Sum(l => l.Revision);

    private void Refresh()
    {
        if (map.Rails is not { } rails || Revision() == _builtRevision)
            return;
        _all.Clear();
        _byCell.Clear();
        _byEdge.Clear();
        foreach (var edge in rails.Edges.Where(e => !rails.IsBridge(e)))
        {
            var corridor = new CorridorShape(edge.Geometry, rails.Settings.HalfWidthM);
            foreach (var cell in rails.World.Index.CellsOf(edge.Spatial))
            {
                if (!map.Contains(cell) || !map.Networks.Layers.Any(l => l.HasAny(cell)))
                    continue;
                var square = SpatialScale.CellBounds(cell);
                if (!ShapeConflicts.Conflict(corridor, AreaShape.OfCells(new GridRect(cell.X, cell.Y, 1, 1))))
                    continue;
                var (from, to) = (double.PositiveInfinity, double.NegativeInfinity);
                var reach = square.Expand(rails.Settings.HalfWidthM);
                foreach (var seg in edge.Geometry.Flat)
                    if (Clip(seg.A, seg.B, reach) is var (t0, t1))
                        (from, to) = (Math.Min(from, seg.FromS + (seg.ToS - seg.FromS) * t0), Math.Max(to, seg.FromS + (seg.ToS - seg.FromS) * t1));
                var alongY = map.Networks.Layers.Select(l => RoadAxis(l.EdgesAt(cell))).FirstOrDefault(a => a is not null) ?? false;
                var crossing = new RailCrossing(cell, edge.Id, Math.Max(0, from), Math.Min(edge.LengthM, to), alongY);
                _all.Add(crossing);
                Add(_byCell, cell, crossing);
                Add(_byEdge, edge.Id, crossing);
            }
        }
        _builtRevision = Revision();
    }

    private static void Add<TKey>(Dictionary<TKey, List<RailCrossing>> index, TKey key, RailCrossing crossing) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var list))
            index[key] = list = [];
        list.Add(crossing);
    }

    /// <summary>Доли отрезка внутри прямоугольника (Лианг — Барски) или null.</summary>
    internal static (double, double)? Clip(WorldPoint a, WorldPoint b, WorldBounds r)
    {
        double t0 = 0, t1 = 1;
        var d = b - a;
        bool Edge(double p, double q)
        {
            if (Math.Abs(p) < 1e-12)
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
        return Edge(-d.X, a.X - r.MinX) && Edge(d.X, r.MaxX - a.X) && Edge(-d.Y, a.Y - r.MinY) && Edge(d.Y, r.MaxY - a.Y) && t1 > t0
            ? (t0, t1)
            : null;
    }

    /// <summary>Прямая дорога в клетке: true — с севера на юг, false — с запада на восток, null — не прямая.</summary>
    public static bool? RoadAxis(DirectionMask edges) => edges switch
    {
        DirectionMask.North | DirectionMask.South => true,
        DirectionMask.East | DirectionMask.West => false,
        _ => null,
    };

    /// <summary>
    /// Идёт ли путь через клетку только прямыми примитивами (с запасом на полуширину коридора) и в каком направлении.
    /// </summary>
    public static bool StraightIn(PathGeometry axis, GridPos cell, double halfWidthM, out double heading)
    {
        heading = 0;
        var square = SpatialScale.CellBounds(cell);
        var found = false;
        for (var i = 0; i < axis.Primitives.Count; i++)
        {
            var primitive = axis.Primitives[i];
            var near = primitive.Flatten(PathGeometry.FlattenToleranceM, PathGeometry.FlattenSegmentM)
                .Zip(primitive.Flatten(PathGeometry.FlattenToleranceM, PathGeometry.FlattenSegmentM).Skip(1))
                .Any(p => Distances.SegmentRect(p.First, p.Second, square) < halfWidthM);
            if (!near)
                continue;
            if (primitive is not StraightPrimitive straight)
                return false;
            heading = straight.Heading;
            found = true;
        }
        return found;
    }
}
