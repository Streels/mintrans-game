using Mintrans.Core.Grid;
using Mintrans.Core.Ids;

namespace Mintrans.World.Sites;

/// <summary>Заявка на занятие клеток: новый объект или новая часть существующего.</summary>
public interface IPlacementRequest
{
    string Key { get; }
    GridRect Area { get; }
}

/// <summary>Поставить новый объект с базовой областью <paramref name="Area"/>.</summary>
public sealed record NewSiteRequest(string Key, TypeId Type, string Name, GridRect Area) : IPlacementRequest;

/// <summary>Присоединить к объекту новую часть: модуль предприятия, грузовую станцию.</summary>
public sealed record AttachPartRequest(string Key, TypeId Kind, GridRect Area) : IPlacementRequest;

/// <summary>Почему клетки нельзя занять: короткий код для логики и текст для игрока.</summary>
public sealed record PlacementProblem(string Code, string Message);

/// <summary>
/// Одно правило размещения. Новые ограничения (вода, уклон, расстояние до соседей)
/// добавляются новыми правилами, без изменения уже написанных.
/// </summary>
public interface IPlacementRule
{
    PlacementProblem? Check(WorldMap map, IPlacementRequest request);
}

public sealed class InsideMapRule : IPlacementRule
{
    public PlacementProblem? Check(WorldMap map, IPlacementRequest request) =>
        map.Bounds.Contains(request.Area)
            ? null
            : new PlacementProblem("outside_map", $"Площадка {request.Area} выходит за границы карты.");
}

public sealed class FreeCellsRule : IPlacementRule
{
    public PlacementProblem? Check(WorldMap map, IPlacementRequest request) =>
        map.Sites.IsFree(request.Area)
            ? null
            : new PlacementProblem("cells_occupied", $"Клетки {request.Area} уже заняты.");
}

/// <summary>Новая часть должна примыкать к площадке объекта общей стороной клетки (docs/06, docs/10).</summary>
public sealed class AttachedToSiteRule : IPlacementRule
{
    public PlacementProblem? Check(WorldMap map, IPlacementRequest request)
    {
        if (request is not AttachPartRequest attach)
            return null;
        if (!map.Sites.TryGetByKey(attach.Key, out var site))
            return new PlacementProblem("no_site", $"Нет объекта «{attach.Key}».");
        return site.Touches(attach.Area)
            ? null
            : new PlacementProblem("not_attached", $"Площадка {attach.Area} не примыкает к «{site.Name}».");
    }
}

/// <summary>Проверяет заявку набором правил и меняет карту только если проблем нет.</summary>
public sealed class SitePlacement
{
    private readonly IReadOnlyList<IPlacementRule> _rules;

    public SitePlacement(IEnumerable<IPlacementRule> rules)
    {
        _rules = rules.ToList();
    }

    /// <summary>Базовые правила, обязательные для любого объекта.</summary>
    public static SitePlacement Basic() => new([new InsideMapRule(), new FreeCellsRule(), new AttachedToSiteRule()]);

    public IReadOnlyList<PlacementProblem> Check(WorldMap map, IPlacementRequest request) =>
        _rules.Select(rule => rule.Check(map, request)).OfType<PlacementProblem>().ToList();

    public Site Place(WorldMap map, NewSiteRequest request)
    {
        RequireNoProblems(map, request);
        return map.Sites.Add(request.Key, request.Type, request.Name, request.Area);
    }

    public Site Attach(WorldMap map, AttachPartRequest request)
    {
        RequireNoProblems(map, request);
        var site = map.Sites.GetByKey(request.Key);
        map.Sites.Attach(site, new SitePart(request.Kind, request.Area));
        return site;
    }

    private void RequireNoProblems(WorldMap map, IPlacementRequest request)
    {
        var problems = Check(map, request);
        if (problems.Count > 0)
            throw new SitePlacementException(request, problems);
    }
}

public sealed class SitePlacementException(IPlacementRequest request, IReadOnlyList<PlacementProblem> problems)
    : Exception($"Нельзя занять {request.Area} для «{request.Key}»: {string.Join("; ", problems.Select(p => p.Message))}")
{
    public IPlacementRequest Request { get; } = request;
    public IReadOnlyList<PlacementProblem> Problems { get; } = problems;
}
