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

/// <summary>План станции: прямоугольник клеток, предприятие, к которому она примкнёт, и ось путей сквозь неё.</summary>
public sealed record StationPlan(
    StationDefinition Station,
    GridRect? Area,
    Site? Target,
    bool AlongX,
    IReadOnlyList<BuildProblem> Problems)
{
    public bool CanBuild => Problems.Count == 0 && Area is not null && Target is not null;
    public IReadOnlyList<GridPos> Cells => Area?.Cells().ToList() ?? [];

    /// <summary>Длина платформы в клетках — вдоль путей.</summary>
    public int Length => Area is { } a ? AlongX ? a.Width : a.Height : 0;

    /// <summary>Число параллельных станционных путей — поперёк.</summary>
    public int Tracks => Area is { } a ? AlongX ? a.Height : a.Width : 0;
}

/// <summary>План моста: опоры, все клетки от опоры до опоры; после постройки <see cref="Built"/> — созданный мост.</summary>
public sealed record BridgePlan(BridgeDefinition Definition, IReadOnlyList<GridPos> Cells, IReadOnlyList<BuildProblem> Problems)
{
    public bool CanBuild => Problems.Count == 0 && Cells.Count >= 2;
    public Bridge? Built { get; init; }
}

/// <summary>План сигнала: клетка и сигнал, который в ней встанет (null — поставить нельзя).</summary>
public sealed record SignalPlan(SignalDefinition Definition, GridPos Cell, Signal? Signal, IReadOnlyList<BuildProblem> Problems)
{
    public bool CanBuild => Problems.Count == 0 && Signal is not null;
    public IReadOnlyList<GridPos> Cells => [Cell];
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
            new BridgeHeadRule(),
            new TrackLayoutRule(),
            new NetworkOverlapRule(catalog),
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
        var outside = cells.Where(c => !_map.Contains(c)).ToList();
        if (cells.Count == 0 || outside.Count > 0)
            return new BuildPlan(cells, outside.Select(c => new BuildProblem("outside_map", "За пределами карты.", c)).ToList());
        // Правила видят, что именно ляжет в клетку: с какой стороны путь входит и куда выходит.
        var problems = _map.Networks[network].PlanSteps(cells)
            .SelectMany(step => _cellRules.Select(rule => rule.Check(_map, definition, step)))
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
    /// План станции протягиванием (docs/10): прямоугольник между клетками. Вдоль длинной стороны — платформа,
    /// поперёк — параллельные станционные пути, по составу на каждый. Квадрат кладёт пути вдоль стороны предприятия.
    /// Станция должна примыкать ровно к одному подходящему предприятию.
    /// </summary>
    public StationPlan PlanStation(TypeId stationId, GridPos from, GridPos to)
    {
        var station = _catalog.Station(stationId);
        var area = RectBetween(from, to);
        var alongX = area.Width >= area.Height;
        // Однопутная станция — полоса вдоль преобладающего направления протяжки, как бы ни дрогнула мышь.
        if (station.MaxTracks == 1)
            area = alongX
                ? new GridRect(area.X, from.Y, area.Width, 1)
                : new GridRect(from.X, area.Y, 1, area.Height);

        var problems = new List<BuildProblem>();
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
            // Квадрат: ось путей параллельна стороне, которой станция касается предприятия.
            if (area.Width == area.Height)
                alongX = area.Cells()
                    .SelectMany(cell => Directions.All.Where(side => target.Contains(cell.Step(side))))
                    .Any(side => side is Direction.North or Direction.South);
        }

        var (length, tracks) = alongX ? (area.Width, area.Height) : (area.Height, area.Width);
        if (length > station.MaxLength)
            problems.Add(new BuildProblem("too_long", $"Станция длиннее {station.MaxLength} клеток."));
        if (tracks > station.MaxTracks)
            problems.Add(new BuildProblem("too_wide", station.MaxTracks == 1
                ? "Станция — полоса в одну клетку."
                : $"У станции не больше {station.MaxTracks} путей."));

        var own = _map.Networks[station.Network];
        var axis = alongX ? (Direction.West, Direction.East) : (Direction.North, Direction.South);
        foreach (var cell in area.Cells().Where(_map.Contains))
        {
            if (_map.Facilities.At(cell) is { } facility)
                problems.Add(new BuildProblem("facility", $"Клетка занята: {facility.Name}.", cell));
            if (TerrainChecks.ForbiddenTerrainAt(_map, _catalog.ForbiddenTerrain, cell) is { } terrain)
                problems.Add(terrain);
            var foreign = _map.Networks.Layers.FirstOrDefault(n => n.Type != station.Network && n.HasAny(cell));
            if (foreign is not null)
                problems.Add(new BuildProblem("other_network", "На месте станции проходит другая сеть.", cell));
            if (_map.Networks.Layers.Any(n => n.BridgeAt(cell) is not null || n.BridgeOver(cell) is not null))
                problems.Add(new BuildProblem("bridge", "На месте станции мост.", cell));
            // Уже лежащий путь станции не мешает, если идёт вдоль платформы; повороты и стрелки внутри — мешают.
            if (own is TrackNetworkLayer rails)
            {
                var pieces = rails.PiecesAt(cell);
                if ((pieces & ~Tracks.Between(axis.Item1, axis.Item2)) != 0)
                    problems.Add(new BuildProblem("station_track", "Станция ставится только на прямой путь вдоль платформы.", cell));
                if (rails.SignalsAt(cell).Any())
                    problems.Add(new BuildProblem("signal", "На месте станции сигнал: станции и депо работают как встроенные сигналы.", cell));
            }
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

    // ---------- Мосты ----------

    /// <summary>
    /// План моста протягиванием от опоры до опоры (docs/10б): прямой, не короче трёх клеток.
    /// Опоры — на свободной земле; под пролётом могут быть пути, дороги и вода, но не площадки и не другой мост.
    /// </summary>
    public BridgePlan PlanBridge(TypeId bridgeId, GridPos from, GridPos to)
    {
        var definition = _catalog.Bridge(bridgeId);
        // Мост идёт по прямой вдоль преобладающего направления протяжки.
        var end = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y) ? new GridPos(to.X, from.Y) : new GridPos(from.X, to.Y);
        List<GridPos> cells = from == end ? [from] : NetworkPaths.Polyline([from, end]);
        var problems = new List<BuildProblem>();
        if (cells.Count < 3)
            problems.Add(new BuildProblem("too_short", "Мост — от опоры до опоры, хотя бы через одну клетку."));
        if (cells.Count > definition.MaxLength)
            problems.Add(new BuildProblem("too_long", $"Мост длиннее {definition.MaxLength} клеток."));

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (!_map.Contains(cell))
            {
                problems.Add(new BuildProblem("outside_map", "За пределами карты.", cell));
                continue;
            }
            var head = i == 0 || i == cells.Count - 1;
            if (_map.Sites.At(cell) is { } site)
                problems.Add(new BuildProblem("site_occupied", $"Клетка занята: {site.Name}.", cell));
            if (_map.Facilities.At(cell) is { } facility)
                problems.Add(new BuildProblem("facility", $"Клетка занята: {facility.Name}.", cell));
            if (_map.Networks.Layers.Any(n => n.BridgeAt(cell) is not null || n.BridgeOver(cell) is not null))
                problems.Add(new BuildProblem("bridge_crossing", "Мосты не пересекаются и не стоят друг на друге.", cell));
            if (!head)
                continue;
            if (_map.Networks.Layers.Any(n => n.HasAny(cell)))
                problems.Add(new BuildProblem("network", "Опора ставится на свободную клетку — путь подводят к ней потом.", cell));
            if (TerrainChecks.ForbiddenTerrainAt(_map, _catalog.ForbiddenTerrain, cell) is { } terrain)
                problems.Add(terrain);
        }
        return new BridgePlan(definition, cells, problems);
    }

    public BridgePlan BuildBridge(TypeId bridgeId, GridPos from, GridPos to)
    {
        var plan = PlanBridge(bridgeId, from, to);
        if (!plan.CanBuild)
            return plan;
        var bridge = _map.Networks[plan.Definition.Network].AddBridge(plan.Definition.Id, plan.Cells[0], plan.Cells[^1]);
        return plan with { Built = bridge };
    }

    // ---------- Сигналы ----------

    /// <summary>
    /// План сигнала (docs/10а). Щелчок по клетке — двусторонний сигнал; протяжка к соседней клетке вдоль пути —
    /// односторонний, регулирует движение в сторону протяжки. Сигнал ставится на путь без стрелки:
    /// не на стрелку, переезд, опору моста, станцию или депо (у них сигналы встроены).
    /// </summary>
    public SignalPlan PlanSignal(TypeId signalId, GridPos from, GridPos to)
    {
        var definition = _catalog.Signal(signalId);
        var problems = new List<BuildProblem>();
        if (!_map.Contains(from))
            return new SignalPlan(definition, from, null, [new BuildProblem("outside_map", "За пределами карты.", from)]);
        if (_map.Networks[definition.Network] is not TrackNetworkLayer rails)
            throw new InvalidOperationException($"Сигналы ставятся только на рельсовую сеть, а «{definition.Network}» — не она.");

        var pieces = rails.PiecesAt(from);
        if (pieces == TrackPieces.None)
            problems.Add(new BuildProblem("no_track", "Сигнал ставится на ж/д путь.", from));
        else if (!pieces.IsSimple())
            problems.Add(new BuildProblem("junction", "На стрелке и кресте сигнал не ставится — поставьте его до или после.", from));
        if (_map.Networks.Layers.Any(n => n.BridgeAt(from) is not null))
            problems.Add(new BuildProblem("bridge_head", "На опоре моста сигнал не ставится.", from));
        if (_map.Networks.Layers.Any(n => n.Type != definition.Network && n.HasAny(from)))
            problems.Add(new BuildProblem("crossing", "На переезде сигнал не ставится.", from));
        if (_map.Sites.At(from) is not null || _map.Facilities.At(from) is not null)
            problems.Add(new BuildProblem("station", "На станции и в депо сигналы встроены.", from));
        if (problems.Count > 0)
            return new SignalPlan(definition, from, null, problems);

        Direction? facing = from == to ? null : DominantSide(from, to);
        TrackPieces piece;
        if (facing is { } side)
        {
            piece = pieces.Enumerate().FirstOrDefault(p => p.Touches(side));
            if (piece == TrackPieces.None)
                return new SignalPlan(definition, from, null,
                    [new BuildProblem("wrong_direction", "Протяните мышью вдоль пути: так задаётся направление сигнала.", from)]);
        }
        else if (pieces.Count() == 1)
            piece = pieces;
        else
            return new SignalPlan(definition, from, null,
                [new BuildProblem("ambiguous", "Здесь два пути — протяните мышью вдоль нужного.", from)]);

        var (a, b) = piece.Sides();
        var faces = facing is { } f ? f.ToMask() : a.ToMask() | b.ToMask();
        return new SignalPlan(definition, from, new Signal(from, piece, definition.Id, definition.Logic, faces), problems);
    }

    public SignalPlan BuildSignal(TypeId signalId, GridPos from, GridPos to)
    {
        var plan = PlanSignal(signalId, from, to);
        if (plan.CanBuild)
            ((TrackNetworkLayer)_map.Networks[plan.Definition.Network]).SetSignal(plan.Signal!);
        return plan;
    }

    /// <summary>План снятия сигналов прямоугольником: пути остаются.</summary>
    public BuildPlan PlanRemoveSignals(GridPos from, GridPos to)
    {
        var cells = RectBetween(from, to).Cells().Where(_map.Contains)
            .Where(cell => SignalLayers().Any(l => l.SignalsAt(cell).Any()))
            .ToList();
        return new BuildPlan(cells, cells.Count == 0 ? [new BuildProblem("no_signals", "Здесь нет сигналов.")] : []);
    }

    public BuildPlan RemoveSignals(GridPos from, GridPos to)
    {
        var plan = PlanRemoveSignals(from, to);
        foreach (var cell in plan.Cells)
        foreach (var layer in SignalLayers())
        foreach (var signal in layer.SignalsAt(cell).ToList())
            layer.RemoveSignal(signal);
        return plan;
    }

    private IEnumerable<TrackNetworkLayer> SignalLayers() => _map.Networks.Layers.OfType<TrackNetworkLayer>();

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
