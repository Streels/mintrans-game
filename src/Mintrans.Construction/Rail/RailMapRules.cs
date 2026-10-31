using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Rail;

namespace Mintrans.Construction.Rail;

/// <summary>Общие для правил рельсов вопросы к клеточной карте.</summary>
internal static class RailCells
{
    /// <summary>Шаг проверочных точек вдоль оси пути, м.</summary>
    public const double ProbeStepM = 2.5;

    /// <summary>Клетки, в которые реально заходит габарит пути (а не только касается их угла).</summary>
    public static IEnumerable<GridPos> Covered(CorridorShape corridor, WorldMap map) =>
        corridor.TouchedCells().Where(cell => map.Contains(cell) &&
            ShapeConflicts.Conflict(corridor, AreaShape.OfCells(new GridRect(cell.X, cell.Y, 1, 1))));

    public static CorridorShape Corridor(RailNetwork network, TrackEdge edge) => new(edge.Geometry, network.Settings.HalfWidthM);

    /// <summary>Новые пути изменения, кроме частей уже построенных (их место проверено при постройке).</summary>
    public static IEnumerable<TrackEdge> Fresh(RailEdit edit) => edit.NewEdges.Where(e => !edit.IsPiece(e.Id));

    /// <summary>Мост: путь выше своих узлов.</summary>
    public static bool IsBridge(RailEdit edit, TrackEdge edge) => edge.Level != edit.Node(edge.From).Level;

    /// <summary>Точки оси с шагом <see cref="ProbeStepM"/>; у моста — только у опор.</summary>
    public static IEnumerable<WorldPoint> Probes(RailEdit edit, TrackEdge edge)
    {
        var axis = edge.Geometry;
        var count = Math.Max(1, (int)Math.Ceiling(axis.Length / ProbeStepM));
        var bridge = IsBridge(edit, edge);
        for (var i = 0; i <= count; i++)
        {
            var s = axis.Length * i / count;
            if (!bridge || s <= SpatialScale.CellM || s >= axis.Length - SpatialScale.CellM)
                yield return axis.PointAt(s);
        }
    }
}

/// <summary>Запрещённая природа (docs/02): по воде рельсы не кладут — её пересекают мостом; опоры моста — на суше.</summary>
public sealed class RailTerrainRule(WorldMap map, IReadOnlyList<ForbiddenTerrain> forbidden) : IRailRule
{
    public IEnumerable<string> Check(RailNetwork network, RailEdit edit)
    {
        foreach (var edge in RailCells.Fresh(edit))
        foreach (var point in RailCells.Probes(edit, edge))
        {
            var cell = SpatialScale.ToCell(point);
            if (!map.Contains(cell))
                continue;
            foreach (var rule in forbidden)
                if (map.Terrain.TryGet(rule.Category, out var layer) && rule.Kinds.Contains(layer.KindAt(cell).Id))
                {
                    yield return RailCells.IsBridge(edit, edge)
                        ? $"опора моста стоит на запрещённой природе: {layer.KindAt(cell).Id}"
                        : $"путь проходит по запрещённой природе: {layer.KindAt(cell).Id} — пересеките мостом";
                    goto nextEdge;
                }
        }
        nextEdge: ;
    }
}

/// <summary>
/// Площадки объектов (docs/06): сквозь шахту, завод и город рельсы не проходят — ни по земле, ни мостом.
/// Станция ставится рядом с предприятием (платформа у пути), а не внутри площадки.
/// </summary>
public sealed class RailSiteRule(WorldMap map) : IRailRule
{
    public IEnumerable<string> Check(RailNetwork network, RailEdit edit)
    {
        foreach (var edge in RailCells.Fresh(edit))
        {
            var corridor = RailCells.Corridor(network, edge);
            var site = RailCells.Covered(corridor, map).Select(map.Sites.At).FirstOrDefault(s => s is not null);
            if (site is not null)
                yield return $"путь проходит через площадку «{site.Name}»";
        }
    }
}

/// <summary>
/// Сооружения (docs/10): сквозь автогараж и городской пункт рельсы не проходят. Ж/д депо стоит на самом пути,
/// поэтому его клетки рельсам не мешают — пересечь чужой путь всё равно не даст проверка габаритов.
/// </summary>
public sealed class RailFacilityRule(WorldMap map, ConstructionCatalog catalog) : IRailRule
{
    public IEnumerable<string> Check(RailNetwork network, RailEdit edit)
    {
        foreach (var edge in RailCells.Fresh(edit).Where(e => !RailCells.IsBridge(edit, e)))
        {
            var corridor = RailCells.Corridor(network, edge);
            var facility = RailCells.Covered(corridor, map).Select(map.Facilities.At)
                .FirstOrDefault(f => f is not null && catalog.Facility(f.Kind).Placement != FacilityPlacement.RailDepot);
            if (facility is not null)
                yield return $"путь проходит через «{facility.Name}»";
        }
    }
}

/// <summary>
/// Переезд (docs/10а, docs/10в, раздел 7): рельсы на земле пересекают клеточную дорогу только прямым участком,
/// поперёк прямой дороги (угол не меньше <see cref="MinAngleDeg"/>°), не у перекрёстка и не у опоры моста.
/// Переезд не соединяет сети. Мост проходит над дорогой без ограничений.
/// </summary>
public sealed class RailCrossingRule(WorldMap map, ConstructionCatalog catalog) : IRailRule
{
    public const double MinAngleDeg = 45;

    public IEnumerable<string> Check(RailNetwork network, RailEdit edit)
    {
        foreach (var edge in RailCells.Fresh(edit).Where(e => !RailCells.IsBridge(edit, e)))
        {
            var corridor = RailCells.Corridor(network, edge);
            foreach (var cell in RailCells.Covered(corridor, map))
            foreach (var layer in map.Networks.Layers.Where(l => l.HasAny(cell)))
                if (Problem(layer, cell, edge.Geometry, network.Settings.HalfWidthM) is { } problem)
                {
                    yield return problem;
                    goto nextEdge;
                }
            nextEdge: ;
        }
        foreach (var signal in edit.NewSignals)
        {
            var at = edit.Edge(signal.Edge).Geometry.PointAt(signal.DistanceM);
            var cell = SpatialScale.ToCell(at);
            if (map.Contains(cell) && map.Networks.Layers.Any(l => l.HasAny(cell)))
                yield return "на переезде сигнал не ставится";
        }
        foreach (var berth in edit.NewBerths)
        {
            var geometry = edit.Edge(berth.Edge).Geometry;
            var piece = SubPath(geometry, berth.FromM, berth.ToM);
            var corridor = new CorridorShape(piece, network.Settings.HalfWidthM);
            if (RailCells.Covered(corridor, map).Any(c => map.Networks.Layers.Any(l => l.HasAny(c))))
                yield return berth.Kind == BerthKind.Platform ? "платформа заходит на переезд" : "депо заходит на переезд";
        }
    }

    private string? Problem(NetworkLayer layer, GridPos cell, PathGeometry axis, double halfWidth)
    {
        if (!catalog.AllowsLevelCrossing(layer.Type, map.Rails!.Settings.Kind))
            return $"путь пересекает сеть «{layer.Type}» — переезд с ней не строится";
        if (layer.BridgeAt(cell) is not null)
            return "здесь опора моста — переезд не строится";
        var roadAlongY = RailCrossings.RoadAxis(layer.EdgesAt(cell));
        if (roadAlongY is null)
            return $"переезд строится только поперёк прямой дороги — в клетке {cell.X}, {cell.Y} перекрёсток, поворот или тупик";
        if (!RailCrossings.StraightIn(axis, cell, halfWidth, out var heading))
            return "переезд строится только на прямом участке пути — уберите кривую с дороги";
        var road = roadAlongY.Value ? Math.PI / 2 : 0;
        var angle = Math.Abs(Angles.NormalizePi(heading - road));
        angle = Math.Min(angle, Math.PI - angle);
        return angle + 1e-9 < Angles.ToRadians(MinAngleDeg) ? $"путь пересекает дорогу слишком полого — нужен угол не меньше {MinAngleDeg:0}°" : null;
    }

    private static PathGeometry SubPath(PathGeometry geometry, double fromM, double toM)
    {
        var (a, b) = (geometry.PointAt(fromM), geometry.PointAt(toM));
        return new PathGeometry(new StraightPrimitive(a, b));
    }
}
