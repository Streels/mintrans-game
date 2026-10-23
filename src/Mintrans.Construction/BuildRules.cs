using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Networks;

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

/// <summary>
/// Рельсы (docs/10б): в клетке — только стандартная конфигурация кусков. Крест двух прямых допустим,
/// но без поворотов в той же клетке. Где стоит сигнал, стрелку или крест не построить: на них сигнал не ставится.
/// </summary>
public sealed class TrackLayoutRule : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, PathStep step)
    {
        if (map.Networks[network.Network] is not TrackNetworkLayer tracks)
            return null;
        var after = tracks.PiecesAt(step.Cell) | TrackNetworkLayer.PieceOf(step);
        if (!after.IsValid())
            return new BuildProblem("cross_with_turn", "На кресте путей нельзя повернуть — перенесите стрелку на соседнюю клетку.", step.Cell);
        if (!after.IsSimple() && tracks.SignalsAt(step.Cell).Any())
            return new BuildProblem("signal_on_junction", "На клетке сигнал — стрелку или крест здесь не построить.", step.Cell);
        return null;
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

/// <summary>Форма сети в клетке — для правил переездов.</summary>
public static class NetworkShapes
{
    /// <summary>
    /// Идёт ли сеть в клетке одной прямой (с учётом шага <paramref name="adding"/>, если он будет уложен):
    /// true — с севера на юг, false — с востока на запад, null — не прямая (поворот, стрелка, тупик, перекрёсток).
    /// </summary>
    public static bool? StraightAxis(NetworkLayer layer, GridPos cell, PathStep? adding)
    {
        if (layer is TrackNetworkLayer tracks)
        {
            var pieces = tracks.PiecesAt(cell) | (adding is { } step ? TrackNetworkLayer.PieceOf(step) : TrackPieces.None);
            return pieces switch
            {
                TrackPieces.NorthSouth => true,
                TrackPieces.EastWest => false,
                _ => null,
            };
        }
        var edges = layer is EdgeNetworkLayer roads && adding is { } a ? roads.EdgesAfter(a) : layer.EdgesAt(cell);
        return edges switch
        {
            DirectionMask.North | DirectionMask.South => true,
            DirectionMask.East | DirectionMask.West => false,
            _ => null,
        };
    }

    /// <summary>Переезд: в клетке две сети, обе прямые и поперёк друг друга.</summary>
    public static bool IsLevelCrossing(WorldMap map, GridPos cell)
    {
        var layers = map.Networks.Layers.Where(l => l.HasAny(cell)).ToList();
        return layers.Count == 2
            && StraightAxis(layers[0], cell, null) is { } a
            && StraightAxis(layers[1], cell, null) is { } b
            && a != b;
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
