using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.Construction.Rail;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Networks;
using Mintrans.World.Rail;

namespace Mintrans.Construction;

/// <summary>Почему нельзя строить: код для логики, текст для игрока и клетка, где проблема.</summary>
public sealed record BuildProblem(string Code, string Message, GridPos? Cell = null);

/// <summary>
/// Правило для одной клетки прокладываемой сети: шаг прокладки говорит, что ляжет в клетку (стороны входа и выхода).
/// Будущие ограничения (уклон, стоимость) добавляются новыми правилами без изменения остальных.
/// </summary>
public interface INetworkCellRule
{
    BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step);
}

public sealed class InsideMapCellRule : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step) =>
        map.Contains(step.Cell) ? null : new BuildProblem("outside_map", "За пределами карты.", step.Cell);
}

/// <summary>Нельзя строить на запрещённой природе (на воде — только мостом).</summary>
public sealed class ForbiddenTerrainRule(IReadOnlyList<ForbiddenTerrain> forbidden) : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step) =>
        TerrainChecks.ForbiddenTerrainAt(map, forbidden, step.Cell);
}

/// <summary>На площадки объектов можно только там, где это разрешено (город для дорог, станции своей сети).</summary>
public sealed class SiteCellRule : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step)
    {
        var cell = step.Cell;
        if (!map.Contains(cell) || map.Sites.At(cell) is not { } site)
            return null;
        var part = site.PartAt(cell)!;
        return network.AllowedSiteTypes.Contains(site.Type) || network.AllowedParts.Contains(part.Kind)
            ? null
            : new BuildProblem("site_occupied", $"Клетка занята: {site.Name}.", cell);
    }
}

/// <summary>
/// Сквозь депо и гаражи сеть не прокладывается: они соединяются с сетью только через въезд.
/// Остановки на улицах (городской пункт) прокладке не мешают.
/// </summary>
public sealed class FacilityCellRule(ConstructionCatalog catalog) : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step)
    {
        if (!map.Contains(step.Cell) || map.Facilities.At(step.Cell) is not { } facility)
            return null;
        return catalog.Facility(facility.Kind).Placement == FacilityPlacement.StopOnNetwork
            ? null
            : new BuildProblem("facility", $"Клетка занята: {facility.Name}.", step.Cell);
    }
}

/// <summary>В клетку с опорой моста путь не прокладывается: к мосту подводят путь с тыльной стороны опоры.</summary>
public sealed class BridgeHeadRule : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step) =>
        map.Networks.Layers.Any(layer => layer.BridgeAt(step.Cell) is not null)
            ? new BuildProblem("bridge_head", "Здесь опора моста — подведите путь к ней сзади.", step.Cell)
            : null;
}

/// <summary>
/// Две разные сети в одной клетке — только переезд (docs/10а): пары сетей перечислены в правилах,
/// и обе должны идти прямо и поперёк друг друга. Переезд не соединяет сети между собой.
/// </summary>
public sealed class NetworkOverlapRule(ConstructionCatalog catalog) : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step)
    {
        var own = map.Networks[network.Network];
        foreach (var other in map.Networks.Layers.Where(l => l.Type != network.Network && l.HasAny(step.Cell)))
        {
            if (!catalog.AllowsLevelCrossing(network.Network, other.Type))
                return new BuildProblem("other_network", "На клетке уже проходит другая сеть.", step.Cell);
            var mine = NetworkShapes.StraightAxis(own, step.Cell, step);
            var theirs = NetworkShapes.StraightAxis(other, step.Cell, null);
            if (mine is null || theirs is null || mine == theirs)
                return new BuildProblem("crossing_shape", "Переезд строится только поперёк прямого пути и прямой дороги.", step.Cell);
        }
        return null;
    }
}

/// <summary>
/// Запрет сноса со стороны других модулей: например, транспорт не даёт снести депо, в котором стоит техника.
/// Возвращает причину отказа или null.
/// </summary>
public interface IDemolishGuard
{
    string? CanRemove(Facility facility);
}

/// <summary>Запрет сноса из функции — чтобы другой модуль подключался без зависимости от строительства.</summary>
public sealed class DelegateDemolishGuard(Func<Facility, string?> check) : IDemolishGuard
{
    public string? CanRemove(Facility facility) => check(facility);
}

/// <summary>Форма клеточной сети в клетке — для правил переездов.</summary>
public static class NetworkShapes
{
    /// <summary>
    /// Идёт ли сеть в клетке одной прямой (с учётом шага <paramref name="adding"/>, если он будет уложен):
    /// true — с севера на юг, false — с востока на запад, null — не прямая (поворот, перекрёсток, тупик).
    /// </summary>
    public static bool? StraightAxis(NetworkLayer layer, GridPos cell, PathStep? adding)
    {
        var edges = layer is EdgeNetworkLayer roads && adding is { } a ? roads.EdgesAfter(a) : layer.EdgesAt(cell);
        return RailCrossings.RoadAxis(edges);
    }
}

/// <summary>
/// Дорога через рельсы (docs/10а): только переездом — прямая дорога поперёк прямого пути на земле, угол не меньше
/// <see cref="RailCrossingRule.MinAngleDeg"/>°. Под мостом рельсов дорога проходит свободно.
/// </summary>
public sealed class RailInCellRule(ConstructionCatalog catalog) : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step)
    {
        if (map.Rails is not { } rails || !map.Contains(step.Cell))
            return null;
        var edges = RailCellQuery.EdgesIn(map, step.Cell).ToList();
        if (edges.Count == 0)
            return null;
        if (!catalog.AllowsLevelCrossing(network.Network, rails.Settings.Kind))
            return new BuildProblem("other_network", "На клетке уже проходит железная дорога.", step.Cell);
        var road = NetworkShapes.StraightAxis(map.Networks[network.Network], step.Cell, step);
        if (road is null)
            return new BuildProblem("crossing_shape", "Переезд строится только поперёк прямого пути и прямой дороги.", step.Cell);
        foreach (var edge in edges)
        {
            if (!RailCrossings.StraightIn(edge.Geometry, step.Cell, rails.Settings.HalfWidthM, out var heading))
                return new BuildProblem("crossing_shape", "Переезд строится только на прямом участке пути.", step.Cell);
            var angle = Math.Abs(Angles.NormalizePi(heading - (road.Value ? Math.PI / 2 : 0)));
            angle = Math.Min(angle, Math.PI - angle);
            if (angle + 1e-9 < Angles.ToRadians(RailCrossingRule.MinAngleDeg))
                return new BuildProblem("crossing_shape", "Дорога пересекает путь слишком полого.", step.Cell);
            if (rails.SignalsOn(edge.Id).Any(sig => rails.CellOf(sig) == step.Cell) ||
                rails.BerthsOn(edge.Id).Any(berth => map.Crossings.At(step.Cell).Count == 0 &&
                    Covers(edge, berth, step.Cell, rails.Settings.HalfWidthM)))
                return new BuildProblem("crossing_fixture", "Здесь сигнал, платформа или депо — переезд не строится.", step.Cell);
        }
        return null;
    }

    private static bool Covers(TrackEdge edge, RailBerth berth, GridPos cell, double halfWidth)
    {
        var square = SpatialScale.CellBounds(cell);
        return edge.Geometry.Flat.Any(seg => seg.ToS > berth.FromM && seg.FromS < berth.ToM &&
                                            Distances.SegmentRect(seg.A, seg.B, square) < halfWidth);
    }
}

internal static class TerrainChecks
{
    public static BuildProblem? ForbiddenTerrainAt(WorldMap map, IReadOnlyList<ForbiddenTerrain> forbidden, GridPos cell)
    {
        if (!map.Contains(cell))
            return null;
        foreach (var rule in forbidden)
        {
            if (map.Terrain.TryGet(rule.Category, out var layer) && rule.Kinds.Contains(layer.KindAt(cell).Id))
                return new BuildProblem("forbidden_terrain", $"Нельзя строить здесь: {layer.KindAt(cell).Id}.", cell);
        }
        return null;
    }
}
