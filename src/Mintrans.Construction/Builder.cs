using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Networks;
using Mintrans.World.Sites;

namespace Mintrans.Construction;

/// <summary>Результат проверки: что будет занято и что мешает. Пустой список проблем — можно строить.</summary>
public sealed record BuildPlan(IReadOnlyList<GridPos> Cells, IReadOnlyList<BuildProblem> Problems)
{
    public bool CanBuild => Problems.Count == 0 && Cells.Count > 0;
}

/// <summary>План станции: полоса клеток, предприятие, к которому она примкнёт, и ось пути сквозь неё.</summary>
public sealed record StationPlan(
    StationDefinition Station,
    GridRect? Area,
    Site? Target,
    bool AlongX,
    IReadOnlyList<BuildProblem> Problems)
{
    public bool CanBuild => Problems.Count == 0 && Area is not null && Target is not null;
    public IReadOnlyList<GridPos> Cells => Area?.Cells().ToList() ?? [];
}

/// <summary>План сооружения; после постройки <see cref="Built"/> — созданное сооружение.</summary>
public sealed record FacilityPlan(
    FacilityDefinition Definition,
    GridRect? Area,
    Direction? Entrance,
    SiteId Owner,
    IReadOnlyList<BuildProblem> Problems)
{
    public bool CanBuild => Problems.Count == 0 && Area is not null;
    public IReadOnlyList<GridPos> Cells => Area?.Cells().ToList() ?? [];
    public Facility? Built { get; init; }
}

/// <summary>
/// Строительство игроком: пути, дороги, станции и снос. Сначала план с проверкой
/// (для подсветки при протягивании мышью), затем постройка того же плана.
/// Меняет карту между шагами симуляции, поэтому изменения действуют со следующего часа (docs/03).
/// Денег пока нет — бюджет обсуждается в теме 09.
/// </summary>
public sealed class Builder
{
    private readonly WorldMap _map;
    private readonly ConstructionCatalog _catalog;
    private readonly IReadOnlyList<INetworkCellRule> _cellRules;
    private readonly SitePlacement _placement;
    private readonly List<IDemolishGuard> _guards = new();

    public Builder(WorldMap map, ConstructionCatalog catalog, IEnumerable<INetworkCellRule>? cellRules = null, SitePlacement? placement = null)
    {
        _map = map;
        _catalog = catalog;
        _cellRules = cellRules?.ToList() ??
        [
            new InsideMapCellRule(),
            new ForbiddenTerrainRule(catalog.ForbiddenTerrain),
            new SiteCellRule(),
            new FacilityCellRule(catalog),
        ];
        _placement = placement ?? SitePlacement.Basic();
    }

    /// <summary>Подключить запрет сноса из другого модуля (например, «в депо стоит техника»).</summary>
    public void AddDemolishGuard(IDemolishGuard guard) => _guards.Add(guard);

    public ConstructionCatalog Catalog => _catalog;

    // ---------- Пути и дороги ----------

    /// <summary>План прокладки сети протягиванием от клетки к клетке.</summary>
    public BuildPlan PlanPath(TypeId network, GridPos from, GridPos to)
    {
        var definition = _catalog.Network(network);
        var cells = NetworkPaths.Drag(from, to);
        var problems = cells
            .SelectMany(cell => _cellRules.Select(rule => rule.Check(_map, definition, cell)))
            .OfType<BuildProblem>()
            .ToList();
        return new BuildPlan(cells, problems);
    }

    public BuildPlan BuildPath(TypeId network, GridPos from, GridPos to)
    {
        var plan = PlanPath(network, from, to);
        if (plan.CanBuild)
            _map.Networks[network].LayPath(plan.Cells);
        return plan;
    }

    // ---------- Станции ----------

    /// <summary>
    /// План станции протягиванием: полоса шириной в одну клетку вдоль длинной стороны протяжки.
    /// Станция должна примыкать ровно к одному подходящему предприятию.
    /// </summary>
    public StationPlan PlanStation(TypeId stationId, GridPos from, GridPos to)
    {
        var station = _catalog.Station(stationId);
        var alongX = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        var area = alongX
            ? new GridRect(Math.Min(from.X, to.X), from.Y, Math.Abs(to.X - from.X) + 1, 1)
            : new GridRect(from.X, Math.Min(from.Y, to.Y), 1, Math.Abs(to.Y - from.Y) + 1);

        var problems = new List<BuildProblem>();
        var length = Math.Max(area.Width, area.Height);
        if (length > station.MaxLength)
            problems.Add(new BuildProblem("too_long", $"Станция длиннее {station.MaxLength} клеток."));

        var candidates = _map.Sites.All
            .Where(site => station.AttachToSiteTypes.Contains(site.Type) && site.Touches(area))
            .ToList();
        var target = candidates.Count == 1 ? candidates[0] : null;
        if (candidates.Count == 0)
            problems.Add(new BuildProblem("no_enterprise", "Станция должна примыкать к шахте или заводу."));
        else if (candidates.Count > 1)
            problems.Add(new BuildProblem("many_enterprises", "Станция примыкает сразу к нескольким предприятиям."));

        if (target is not null)
        {
            problems.AddRange(_placement.Check(_map, new AttachPartRequest(target.Key, station.Id, area))
                .Select(p => new BuildProblem(p.Code, p.Message)));
            // Полоса в одну клетку: ось пути параллельна стороне, которой станция касается предприятия.
            if (length == 1)
                alongX = Directions.All
                    .Where(side => target.Contains(area.Origin.Step(side)))
                    .Any(side => side is Direction.North or Direction.South);
        }

        foreach (var cell in area.Cells().Where(_map.Contains))
        {
            if (_map.Facilities.At(cell) is { } facility)
                problems.Add(new BuildProblem("facility", $"Клетка занята: {facility.Name}.", cell));
            if (TerrainChecks.ForbiddenTerrainAt(_map, _catalog.ForbiddenTerrain, cell) is { } terrain)
                problems.Add(terrain);
            var foreign = _map.Networks.Layers.FirstOrDefault(n => n.Type != station.Network && n.HasAny(cell));
            if (foreign is not null)
                problems.Add(new BuildProblem("other_network", "На месте станции проходит другая сеть.", cell));
        }

        return new StationPlan(station, area, target, alongX, problems);
    }

    public StationPlan BuildStation(TypeId stationId, GridPos from, GridPos to)
    {
        var plan = PlanStation(stationId, from, to);
        if (!plan.CanBuild)
            return plan;
        _placement.Attach(_map, new AttachPartRequest(plan.Target!.Key, plan.Station.Id, plan.Area!.Value));
        var (a, b) = plan.AlongX ? (Direction.West, Direction.East) : (Direction.North, Direction.South);
        var network = _map.Networks[plan.Station.Network];
        foreach (var cell in plan.Cells)
            network.LayThrough(cell, a, b);
        return plan;
    }

    // ---------- Депо, гаражи, городские пункты ----------

    /// <summary>
    /// План сооружения. Депо и гараж — одна клетка, въезд — в сторону протяжки мышью
    /// (без протяжки — к соседней клетке с сетью). Остановка — полоса по уже проложенной сети на площадке объекта.
    /// </summary>
    public FacilityPlan PlanFacility(TypeId facilityId, GridPos from, GridPos to)
    {
        var definition = _catalog.Facility(facilityId);
        return definition.Placement switch
        {
            FacilityPlacement.Depot => PlanDepot(definition, from, to),
            FacilityPlacement.StopOnNetwork => PlanStop(definition, from, to),
            _ => throw new ArgumentOutOfRangeException(nameof(facilityId)),
        };
    }

    public FacilityPlan BuildFacility(TypeId facilityId, GridPos from, GridPos to)
    {
        var plan = PlanFacility(facilityId, from, to);
        if (!plan.CanBuild)
            return plan;
        var definition = plan.Definition;
        var facility = _map.Facilities.Add(definition.Id, definition.Title, plan.Area!.Value, plan.Entrance, plan.Owner);
        if (definition.Placement == FacilityPlacement.Depot)
            _map.Networks[definition.Network].LayEnd(plan.Area.Value.Origin, plan.Entrance!.Value);
        return plan with { Built = facility };
    }

    private FacilityPlan PlanDepot(FacilityDefinition definition, GridPos from, GridPos to)
    {
        var area = new GridRect(from.X, from.Y, 1, 1);
        var problems = new List<BuildProblem>();
        if (!_map.Contains(from))
        {
            problems.Add(new BuildProblem("outside_map", "За пределами карты."));
            return new FacilityPlan(definition, area, Direction.South, SiteId.None, problems);
        }

        var network = _map.Networks[definition.Network];
        var entrance = from == to
            ? Directions.All.Where(side => _map.Contains(from.Step(side)) && network.HasAny(from.Step(side)))
                .Cast<Direction?>().FirstOrDefault() ?? Direction.South
            : DominantSide(from, to);

        if (_map.Sites.At(from) is { } site)
            problems.Add(new BuildProblem("site_occupied", $"Клетка занята: {site.Name}.", from));
        if (_map.Facilities.At(from) is { } facility)
            problems.Add(new BuildProblem("facility", $"Клетка занята: {facility.Name}.", from));
        if (_map.Networks.Layers.Any(n => n.HasAny(from)))
            problems.Add(new BuildProblem("network", "На клетке уже проходит путь или дорога.", from));
        if (TerrainChecks.ForbiddenTerrainAt(_map, _catalog.ForbiddenTerrain, from) is { } terrain)
            problems.Add(terrain);
        if (!_map.Contains(from.Step(entrance)))
            problems.Add(new BuildProblem("outside_map", "Въезд смотрит за пределы карты.", from));
        return new FacilityPlan(definition, area, entrance, SiteId.None, problems);
    }

    private FacilityPlan PlanStop(FacilityDefinition definition, GridPos from, GridPos to)
    {
        var alongX = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        var area = alongX
            ? new GridRect(Math.Min(from.X, to.X), from.Y, Math.Abs(to.X - from.X) + 1, 1)
            : new GridRect(from.X, Math.Min(from.Y, to.Y), 1, Math.Abs(to.Y - from.Y) + 1);
        var problems = new List<BuildProblem>();
        if (Math.Max(area.Width, area.Height) > definition.MaxLength)
            problems.Add(new BuildProblem("too_long", $"Пункт длиннее {definition.MaxLength} клеток."));

        var network = _map.Networks[definition.Network];
        var owners = new HashSet<Site>();
        foreach (var cell in area.Cells())
        {
            if (!_map.Contains(cell))
            {
                problems.Add(new BuildProblem("outside_map", "За пределами карты.", cell));
                continue;
            }
            if (_map.Sites.At(cell) is { } site && definition.SiteTypes.Contains(site.Type))
                owners.Add(site);
            else
                problems.Add(new BuildProblem("no_site", "Пункт ставится на улицах города.", cell));
            if (!network.HasAny(cell))
                problems.Add(new BuildProblem("no_network", "Пункт ставится на уже проложенную дорогу.", cell));
            if (_map.Facilities.At(cell) is { } facility)
                problems.Add(new BuildProblem("facility", $"Клетка занята: {facility.Name}.", cell));
        }
        if (owners.Count > 1)
            problems.Add(new BuildProblem("many_sites", "Пункт должен относиться к одному городу."));
        var owner = owners.Count == 1 ? owners.First().Id : SiteId.None;
        return new FacilityPlan(definition, area, null, owner, problems);
    }

    private static Direction DominantSide(GridPos from, GridPos to)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        return Math.Abs(dx) >= Math.Abs(dy)
            ? dx > 0 ? Direction.East : Direction.West
            : dy > 0 ? Direction.South : Direction.North;
    }

    // ---------- Снос ----------

    /// <summary>План сноса прямоугольником: все сети в клетках и станции, которые он задевает.</summary>
    public BuildPlan PlanDemolish(GridPos from, GridPos to)
    {
        var area = RectBetween(from, to);
        var problems = new List<BuildProblem>();
        foreach (var (site, part) in StationsIn(area))
        {
            if (!site.CanDetach(part))
                problems.Add(new BuildProblem("part_holds_others",
                    $"Станцию у «{site.Name}» нельзя снести: к ней примыкают другие части.", part.Area.Origin));
        }
        foreach (var facility in FacilitiesIn(area))
        {
            foreach (var reason in _guards.Select(g => g.CanRemove(facility)).OfType<string>())
                problems.Add(new BuildProblem("guarded", $"{facility.Name}: {reason}", facility.Area.Origin));
        }
        var cells = area.Cells().Where(_map.Contains).ToList();
        if (cells.Count == 0)
            problems.Add(new BuildProblem("outside_map", "За пределами карты."));
        return new BuildPlan(cells, problems);
    }

    public BuildPlan Demolish(GridPos from, GridPos to)
    {
        var plan = PlanDemolish(from, to);
        if (!plan.CanBuild)
            return plan;
        var area = RectBetween(from, to);
        foreach (var (site, part) in StationsIn(area))
        {
            _map.Sites.Detach(site, part);
            foreach (var cell in part.Area.Cells())
                ClearNetworks(cell);
        }
        foreach (var facility in FacilitiesIn(area))
        {
            _map.Facilities.Remove(facility.Id);
            if (_catalog.Facility(facility.Kind).Placement == FacilityPlacement.Depot)
            {
                foreach (var cell in facility.Area.Cells())
                    ClearNetworks(cell);
            }
        }
        foreach (var cell in plan.Cells)
            ClearNetworks(cell);
        return plan;
    }

    private List<Facility> FacilitiesIn(GridRect area) =>
        _map.Facilities.All.Where(f => f.Area.Intersects(area)).ToList();

    private void ClearNetworks(GridPos cell)
    {
        foreach (var network in _map.Networks.Layers)
            network.ClearCell(cell);
    }

    /// <summary>Станции (части площадок), которые задевает прямоугольник.</summary>
    private List<(Site Site, SitePart Part)> StationsIn(GridRect area) =>
        _map.Sites.All
            .SelectMany(site => site.Parts.Select(part => (Site: site, Part: part)))
            .Where(x => _catalog.IsStation(x.Part.Kind) && x.Part.Area.Intersects(area))
            .ToList();

    private static GridRect RectBetween(GridPos a, GridPos b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
}
