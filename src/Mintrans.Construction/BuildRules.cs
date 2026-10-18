using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Facilities;

namespace Mintrans.Construction;

/// <summary>Почему нельзя строить: код для логики, текст для игрока и клетка, где проблема.</summary>
public sealed record BuildProblem(string Code, string Message, GridPos? Cell = null);

/// <summary>
/// Правило для одной клетки прокладываемой сети. Будущие ограничения (уклон, мосты, стоимость)
/// добавляются новыми правилами без изменения остальных.
/// </summary>
public interface INetworkCellRule
{
    BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, GridPos cell);
}

public sealed class InsideMapCellRule : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, GridPos cell) =>
        map.Contains(cell) ? null : new BuildProblem("outside_map", "За пределами карты.", cell);
}

/// <summary>Нельзя строить на запрещённой природе (пока — на воде: мостов ещё нет).</summary>
public sealed class ForbiddenTerrainRule(IReadOnlyList<ForbiddenTerrain> forbidden) : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, GridPos cell) =>
        TerrainChecks.ForbiddenTerrainAt(map, forbidden, cell);
}

/// <summary>На площадки объектов можно только там, где это разрешено (город для дорог, станции своей сети).</summary>
public sealed class SiteCellRule : INetworkCellRule
{
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, GridPos cell)
    {
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
    public BuildProblem? Check(WorldMap map, NetworkBuildDefinition network, GridPos cell)
    {
        if (!map.Contains(cell) || map.Facilities.At(cell) is not { } facility)
            return null;
        return catalog.Facility(facility.Kind).Placement == FacilityPlacement.StopOnNetwork
            ? null
            : new BuildProblem("facility", $"Клетка занята: {facility.Name}.", cell);
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
