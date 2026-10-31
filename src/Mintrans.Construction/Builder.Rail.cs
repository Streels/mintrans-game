using Mintrans.Construction.Rail;
using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Rail;
using Mintrans.World.Sites;

namespace Mintrans.Construction;

/// <summary>
/// План рельсовой постройки: изменение сети, его проверка, оси путей и участков для подсветки и пояснение.
/// После постройки <see cref="Result"/> — результат применения, <see cref="Built"/> — созданное депо.
/// </summary>
public sealed record RailPlan(RailEdit? Edit, IReadOnlyList<BuildProblem> Problems, IReadOnlyList<PathGeometry> Shapes, string Summary)
{
    public bool CanBuild => Problems.Count == 0 && Edit is not null;

    /// <summary>Предприятие, к которому относится платформа.</summary>
    public Site? Target { get; init; }

    /// <summary>Клетки сооружения (депо).</summary>
    public IReadOnlyList<GridPos> Cells { get; init; } = [];

    public RailEditPreview? Result { get; init; }
    public Facility? Built { get; init; }
}

/// <summary>Что снесёт прямоугольник на рельсах: изменение сети и депо, которые уйдут вместе со своими путями.</summary>
internal sealed record RailDemolition(RailEdit Edit, RailEditPreview Check, IReadOnlyList<Facility> Depots);

/// <summary>Есть ли в клетке рельсы — для правил клеточных построек.</summary>
public static class RailCellQuery
{
    /// <summary>Заходит ли в клетку габарит пути уровня <paramref name="level"/> (0 — на земле, 1 — мост).</summary>
    public static bool Covers(WorldMap map, GridPos cell, int level = 0) => EdgesIn(map, cell, level).Any();

    /// <summary>Пути уровня <paramref name="level"/>, габарит которых заходит в клетку.</summary>
    public static IEnumerable<TrackEdge> EdgesIn(WorldMap map, GridPos cell, int level = 0)
    {
        if (map.Rails is not { } rails)
            yield break;
        var square = AreaShape.OfCells(new GridRect(cell.X, cell.Y, 1, 1));
        foreach (var obj in map.Spatial.At(cell, level))
            if (rails.EdgeOf(obj.Id) is { } edge && ShapeConflicts.Conflict(obj.Shape, square))
                yield return edge;
    }
}

public sealed partial class Builder
{
    /// <summary>
    /// На сколько метров щелчок притягивается к свободному концу пути: меньше шага параллельных путей (5 м),
    /// чтобы конец соседнего пути не перехватывал щелчок.
    /// </summary>
    public const double SnapEndM = 3.0;

    /// <summary>На сколько метров щелчок притягивается к пути (берётся ближайший путь).</summary>
    public const double SnapTrackM = 3.0;

    /// <summary>Инструмент «Соединить А→Б» (docs/10в); null, если на карте нет рельсов.</summary>
    public RailConnectTool? RailTool { get; }

    private RailNetwork Rails => _map.RequireRails();

    private static WorldPoint CellCenter(WorldPoint p) => SpatialScale.CellCenter(SpatialScale.ToCell(p));

    // ---------- Соединение А→Б ----------

    /// <summary>
    /// Точка подключения под курсором: свободный конец рядом, иначе точка на прямом пути (стрелка), иначе новая точка
    /// в центре клетки. Мосты для стрелок не подходят.
    /// </summary>
    public RailAnchor AnchorAt(WorldPoint p)
    {
        if (Rails.NearestFreeEnd(p, SnapEndM) is { } end)
            return new FreeEndAnchor(end.Node);
        var center = CellCenter(p);
        if (Rails.Nearest(p, SnapTrackM, includeBridges: false) is { } near)
        {
            var (s, _) = near.Edge.Geometry.Project(center);
            return new EdgePointAnchor(near.Edge.Id, s);
        }
        return new NewPointAnchor(center);
    }

    /// <summary>
    /// Предпросмотр соединения от точки <paramref name="from"/> к точке <paramref name="to"/> (docs/10в, раздел 2).
    /// Мир не меняется: колесо мыши — <see cref="RailConnection.ScrollRadius"/>, Tab — <see cref="RailConnection.NextVariant"/>,
    /// подтверждение — <see cref="RailConnection.Confirm"/>.
    /// </summary>
    public RailConnection PlanRail(WorldPoint from, WorldPoint to)
    {
        var tool = RailTool ?? throw new InvalidOperationException("На карте нет рельсовой сети.");
        return tool.Preview(AnchorAt(from), AnchorAt(to));
    }

    /// <summary>Построить соединение по умолчанию (для сценариев и тестов); null, если соединить нельзя.</summary>
    public RailEditPreview? BuildRail(WorldPoint from, WorldPoint to)
    {
        var connection = PlanRail(from, to);
        return connection.CanConfirm ? connection.Confirm() : null;
    }

    // ---------- Мост ----------

    /// <summary>
    /// План моста (docs/10б, docs/10в, раздел 7): прямой путь над землёй от опоры до опоры, не короче трёх клеток.
    /// Начинается у свободного конца пути (продолжает его) или в свободной клетке; длина кратна клетке.
    /// Опоры — на суше и не на других путях; под пролётом — пути, дороги, вода, но не площадки и не другой мост.
    /// </summary>
    public RailPlan PlanRailBridge(TypeId bridgeId, WorldPoint from, WorldPoint to)
    {
        var definition = _catalog.Bridge(bridgeId);
        var rails = Rails;
        var start = rails.NearestFreeEnd(from, SnapEndM);
        var origin = start?.At ?? CellCenter(from);
        var target = CellCenter(to);
        var heading = start?.Heading ?? (target - origin).Angle;
        var direction = WorldPoint.FromAngle(heading);
        var length = Math.Round((target - origin).Dot(direction) / SpatialScale.CellM) * SpatialScale.CellM;
        if (length < 2 * SpatialScale.CellM)
            return new RailPlan(null, [new BuildProblem("too_short", "Мост — от опоры до опоры, хотя бы через одну клетку.")], [],
                "Протяните от опоры до опоры по прямой: пролёт пройдёт над путями, дорогами и водой");
        var end = origin + direction * length;
        var geometry = new PathGeometry(new StraightPrimitive(origin, end));

        var problems = new List<BuildProblem>();
        if (length / SpatialScale.CellM + 1 > definition.MaxLength + 1e-9)
            problems.Add(new BuildProblem("too_long", $"Мост длиннее {definition.MaxLength} клеток."));
        var edit = rails.Begin();
        var a = start?.Node ?? edit.NewNode(origin);
        var join = rails.FreeEnds.FirstOrDefault(e => e.At.DistanceTo(end) <= SnapEndM / 2 &&
                                                      Angles.Difference(e.Heading, Angles.Reverse(heading)) <= RailNetwork.HeadingToleranceRad);
        TrackNodeId b;
        if (join is not null)
        {
            b = join.Node;
            geometry = new PathGeometry(new StraightPrimitive(origin, join.At));
        }
        else
            b = edit.NewNode(end);
        edit.AddEdge(a, b, geometry, level: 1);
        foreach (var (head, node) in new[] { (origin, start?.Node), (end, join?.Node) })
            if (node is null && rails.Nearest(head, rails.Settings.HalfWidthM * 2, includeBridges: false) is not null)
                problems.Add(new BuildProblem("network", "Опора ставится на свободное место — путь подводят к ней потом.", SpatialScale.ToCell(head)));
        return Check(edit, problems, [geometry], $"Мост: {length / SpatialScale.CellM + 1:0} кл.");
    }

    public RailPlan BuildRailBridge(TypeId bridgeId, WorldPoint from, WorldPoint to) => Commit(PlanRailBridge(bridgeId, from, to));

    // ---------- Платформа станции ----------

    /// <summary>
    /// План платформы ж/д станции (docs/10в, раздел 4): протяжка вдоль прямого пути, по клеткам. Платформа стоит рядом
    /// с одним подходящим предприятием (не дальше <see cref="StationDefinition.MaxDistanceM"/> от его площадки);
    /// у предприятия до <see cref="StationDefinition.MaxTracks"/> платформ — по составу на каждую.
    /// </summary>
    public RailPlan PlanPlatform(TypeId stationId, WorldPoint from, WorldPoint to)
    {
        var station = _catalog.Station(stationId);
        var interval = Interval(from, to, null, BerthKind.Platform);
        if (interval is not { Problem: null } found)
            return new RailPlan(null, [interval?.Problem ?? new BuildProblem("no_track", "Платформа ставится на ж/д путь.")], [],
                "Протяните вдоль прямого пути");
        var (edge, a, b, _) = found;
        var cells = (int)Math.Round((b - a) / SpatialScale.CellM);
        var segment = new PathGeometry(new StraightPrimitive(edge.Geometry.PointAt(a), edge.Geometry.PointAt(b)));

        var problems = new List<BuildProblem>();
        var candidates = _map.Sites.All
            .Where(site => station.AttachToSiteTypes.Contains(site.Type) && DistanceToSite(segment, site) <= station.MaxDistanceM + 1e-9)
            .ToList();
        var target = candidates.Count == 1 ? candidates[0] : null;
        if (candidates.Count == 0)
            problems.Add(new BuildProblem("no_enterprise", $"Платформа должна стоять у шахты или завода — не дальше {station.MaxDistanceM:0} м от площадки."));
        else if (candidates.Count > 1)
            problems.Add(new BuildProblem("many_enterprises", "Платформа стоит сразу у нескольких предприятий."));
        if (target is not null)
        {
            var tracks = Rails.Berths.Count(berth => berth.Kind == BerthKind.Platform && berth.Site == target.Id);
            if (tracks >= station.MaxTracks)
                problems.Add(new BuildProblem("too_many_tracks", $"У станции «{target.Name}» не больше {station.MaxTracks} путей."));
        }
        var edit = Rails.Begin();
        edit.AddBerth(BerthKind.Platform, edge.Id, a, b, target?.Id ?? SiteId.None, null);
        var summary = target is null ? $"Платформа: {cells} кл." : $"Станция у «{target.Name}», платформа {cells} кл.";
        return Check(edit, problems, [segment], summary) with { Target = target };
    }

    public RailPlan BuildPlatform(TypeId stationId, WorldPoint from, WorldPoint to) => Commit(PlanPlatform(stationId, from, to));

    /// <summary>Расстояние от оси участка до площадки объекта, м.</summary>
    private static double DistanceToSite(PathGeometry segment, Site site) =>
        site.Parts.Min(part =>
        {
            var rect = AreaShape.OfCells(part.Area).Rect;
            return Distances.SegmentRect(segment.Start, segment.End, rect);
        });

    /// <summary>
    /// Участок прямого пути под протяжкой: клетка начала протяжки — первая клетка участка, дальше по клеткам
    /// в сторону протяжки. Длина — <paramref name="fixedCells"/> клеток или по протяжке.
    /// </summary>
    private (TrackEdge Edge, double From, double To, BuildProblem? Problem)? Interval(WorldPoint from, WorldPoint to, int? fixedCells, BerthKind kind)
    {
        if (Rails.Nearest(from, SnapTrackM, includeBridges: false) is not { } near)
            return null;
        var edge = near.Edge;
        var (start, _) = edge.Geometry.Project(CellCenter(from));
        var (finish, _) = edge.Geometry.Project(to);
        var sign = finish >= start - SpatialScale.CellM / 2 ? 1 : -1;
        var cells = fixedCells ?? Math.Max(1, (int)Math.Round(Math.Abs(finish - start) / SpatialScale.CellM) + 1);
        var first = start - sign * SpatialScale.CellM / 2;
        var last = first + sign * cells * SpatialScale.CellM;
        var (a, b) = (Math.Min(first, last), Math.Max(first, last));
        // Короткий путь — участок прижимается к его краю, если помещается.
        if (a < 0 && b - a <= edge.LengthM)
            (a, b) = (0, b - a);
        if (b > edge.LengthM && b - a <= edge.LengthM)
            (a, b) = (edge.LengthM - (b - a), edge.LengthM);
        if (a < -SpatialScale.EpsilonM || b > edge.LengthM + SpatialScale.EpsilonM)
            return (edge, a, b, new BuildProblem("too_long", kind == BerthKind.Platform
                ? "Платформа не помещается на прямом участке пути."
                : "Депо не помещается на прямом участке пути."));
        return (edge, a, b, null);
    }

    // ---------- Депо ----------

    /// <summary>
    /// План ж/д депо (docs/10, docs/10в): стоянка на прямом участке пути длиной в <see cref="FacilityDefinition.MaxLength"/> клеток,
    /// от клетки начала протяжки в её сторону. Здесь покупают и обслуживают поезда; выехать можно в обе стороны пути.
    /// Чужие поезда сквозь депо не ездят.
    /// </summary>
    public RailPlan PlanRailDepot(TypeId facilityId, WorldPoint from, WorldPoint to)
    {
        var definition = _catalog.Facility(facilityId);
        if (definition.Placement != FacilityPlacement.RailDepot)
            throw new InvalidOperationException($"«{definition.Title}» ставится не на путь: PlanFacility.");
        var interval = Interval(from, to, Math.Max(1, definition.MaxLength), BerthKind.Depot);
        if (interval is not { Problem: null } found)
            return new RailPlan(null, [interval?.Problem ?? new BuildProblem("no_track", "Депо ставится на прямой ж/д путь.")], [],
                $"{definition.Title}: на прямой путь, протяните в сторону депо");
        var (edge, a, b, _) = found;
        var segment = new PathGeometry(new StraightPrimitive(edge.Geometry.PointAt(a), edge.Geometry.PointAt(b)));
        var cells = RailCells.Covered(new CorridorShape(segment, Rails.Settings.HalfWidthM), _map).ToList();
        var problems = new List<BuildProblem>();
        foreach (var cell in cells)
        {
            if (_map.Facilities.At(cell) is { } facility)
                problems.Add(new BuildProblem("facility", $"Клетка занята: {facility.Name}.", cell));
            if (_map.Sites.At(cell) is { } site)
                problems.Add(new BuildProblem("site_occupied", $"Клетка занята: {site.Name}.", cell));
        }
        var edit = Rails.Begin();
        edit.AddBerth(BerthKind.Depot, edge.Id, a, b, SiteId.None, _map.Facilities.NextId);
        return Check(edit, problems, [segment], definition.Title) with { Cells = cells };
    }

    public RailPlan BuildRailDepot(TypeId facilityId, WorldPoint from, WorldPoint to)
    {
        var plan = PlanRailDepot(facilityId, from, to);
        if (!plan.CanBuild)
            return plan;
        var definition = _catalog.Facility(facilityId);
        var cells = plan.Cells;
        var (x0, y0) = (cells.Min(c => c.X), cells.Min(c => c.Y));
        var area = new GridRect(x0, y0, cells.Max(c => c.X) - x0 + 1, cells.Max(c => c.Y) - y0 + 1);
        var facility = _map.Facilities.Add(definition.Id, definition.Title, area, null, SiteId.None, cells);
        var result = Rails.Commit(plan.Edit!);
        if (!result.Committed)
        {
            _map.Facilities.Remove(facility.Id);
            return plan with { Problems = result.Issues.Select(i => new BuildProblem("rail", Sentence(i))).ToList(), Result = result };
        }
        return plan with { Result = result, Built = facility, Summary = facility.Name };
    }

    /// <summary>Стоянка депо на путях, если сооружение — ж/д депо.</summary>
    public RailBerth? DepotBerth(FacilityId facility) =>
        _map.Rails?.Berths.FirstOrDefault(b => b.Kind == BerthKind.Depot && b.Facility == facility);

    // ---------- Сигналы ----------

    /// <summary>
    /// План сигнала (docs/10а, docs/10в, раздел 5): щелчок по пути — двусторонний сигнал в точке напротив центра клетки,
    /// протяжка вдоль пути — односторонний в сторону протяжки. Не больше одного сигнала в клетке; не на стрелке,
    /// переезде, мосту, станции и в депо (у них сигналы встроены).
    /// </summary>
    public RailPlan PlanRailSignal(TypeId signalId, WorldPoint from, WorldPoint to)
    {
        var definition = _catalog.Signal(signalId);
        if (Rails.Nearest(from, SnapTrackM) is not { } near)
            return new RailPlan(null, [new BuildProblem("no_track", "Сигнал ставится на ж/д путь.", SpatialScale.ToCell(from))], [],
                "Щёлкните по пути или протяните вдоль него");
        var edge = near.Edge;
        var (s, _) = edge.Geometry.Project(CellCenter(from));
        var drag = to - from;
        var facing = drag.Length < SpatialScale.CellM / 2
            ? SignalFacing.Both
            : drag.Dot(WorldPoint.FromAngle(edge.Geometry.HeadingAt(s))) >= 0 ? SignalFacing.Forward : SignalFacing.Backward;
        var edit = Rails.Begin();
        edit.AddSignal(edge.Id, s, definition.Id, definition.Logic, facing);
        var at = edge.Geometry.PointAt(s);
        var mark = new PathGeometry(new StraightPrimitive(at, at + WorldPoint.FromAngle(edge.Geometry.HeadingAt(s)) * 0.5));
        var summary = facing == SignalFacing.Both ? "Двусторонний: протяните вдоль пути, чтобы сделать односторонним"
            : "Односторонний — в сторону протяжки";
        return Check(edit, [], [mark], summary) with { Cells = [SpatialScale.ToCell(at)] };
    }

    public RailPlan BuildRailSignal(TypeId signalId, WorldPoint from, WorldPoint to) => Commit(PlanRailSignal(signalId, from, to));

    /// <summary>План снятия сигналов прямоугольником: пути остаются.</summary>
    public BuildPlan PlanRemoveSignals(GridPos from, GridPos to)
    {
        var area = RectBetween(from, to);
        var cells = SignalsIn(area).Select(s => Rails.CellOf(s)).Distinct().ToList();
        return new BuildPlan(cells, cells.Count == 0 ? [new BuildProblem("no_signals", "Здесь нет сигналов.")] : []);
    }

    public BuildPlan RemoveSignals(GridPos from, GridPos to)
    {
        var plan = PlanRemoveSignals(from, to);
        if (!plan.CanBuild)
            return plan;
        var edit = Rails.Begin();
        foreach (var signal in SignalsIn(RectBetween(from, to)))
            edit.RemoveSignal(signal.Id);
        var result = Rails.Commit(edit);
        return result.Committed ? plan : plan with { Problems = result.Issues.Select(i => new BuildProblem("rail", Sentence(i))).ToList() };
    }

    private List<RailSignal> SignalsIn(GridRect area) =>
        _map.Rails is { } rails ? rails.Signals.Where(s => area.Contains(rails.CellOf(s))).ToList() : [];

    // ---------- Снос путей ----------

    /// <summary>
    /// Участки путей, ось которых проходит через прямоугольник клеток: путь режется по краю прямоугольника
    /// (узлы появляются на границе), части внутри сносятся. Платформы и депо, которых касается снос, уходят целиком.
    /// </summary>
    private RailDemolition? PlanRailDemolition(GridRect area)
    {
        if (_map.Rails is not { } rails)
            return null;
        var bounds = AreaShape.OfCells(area).Rect;
        var edit = rails.Begin();
        var touched = false;
        foreach (var obj in rails.World.Index.Candidates(area.Cells()).Select(rails.World.Get))
        {
            if (rails.EdgeOf(obj.Id) is not { } edge)
                continue;
            var inside = Inside(edge.Geometry, bounds);
            if (inside.Count == 0)
                continue;
            touched = true;
            foreach (var berth in rails.BerthsOn(edge.Id).Where(b => inside.Any(i => i.From < b.ToM && i.To > b.FromM)))
                edit.RemoveBerth(berth.Id);
            if (inside.Count == 1 && inside[0].From <= SpatialScale.EpsilonM && inside[0].To >= edge.LengthM - SpatialScale.EpsilonM)
            {
                edit.RemoveEdge(edge.Id);
                continue;
            }
            // Делим путь в точках входа и выхода из прямоугольника и сносим части внутри.
            var cuts = inside.SelectMany(i => new[] { i.From, i.To })
                .Where(s => s > SpatialScale.EpsilonM && s < edge.LengthM - SpatialScale.EpsilonM)
                .Distinct().Order().ToList();
            var pieces = new List<(TrackEdgeId Id, double From, double To)>();
            var (current, offset) = (edge.Id, 0.0);
            foreach (var cut in cuts)
            {
                var (_, before, after) = edit.Split(current, cut - offset);
                pieces.Add((before, offset, cut));
                (current, offset) = (after, cut);
            }
            pieces.Add((current, offset, edge.LengthM));
            foreach (var piece in pieces)
            {
                var middle = (piece.From + piece.To) / 2;
                if (inside.Any(i => middle >= i.From && middle <= i.To))
                    edit.RemoveEdge(piece.Id);
            }
        }
        if (!touched)
            return null;
        var check = rails.Preview(edit);
        var depots = check.LostBerths.Where(b => b.Kind == BerthKind.Depot && b.Facility is { } f && _map.Facilities.TryGet(f, out _))
            .Select(b => _map.Facilities[b.Facility!.Value]).ToList();
        return new RailDemolition(edit, check, depots);
    }

    /// <summary>Участки оси (м от начала пути) внутри прямоугольника, слитые в непрерывные.</summary>
    private static List<PathInterval> Inside(PathGeometry axis, WorldBounds rect)
    {
        var result = new List<PathInterval>();
        foreach (var seg in axis.Flat)
        {
            if (Clip(seg.A, seg.B, rect) is not var (t0, t1))
                continue;
            var (from, to) = (seg.FromS + (seg.ToS - seg.FromS) * t0, seg.FromS + (seg.ToS - seg.FromS) * t1);
            if (result.Count > 0 && from <= result[^1].To + 1e-6)
                result[^1] = result[^1] with { To = Math.Max(result[^1].To, to) };
            else
                result.Add(new PathInterval(from, to));
        }
        return result.Where(i => i.To - i.From > SpatialScale.EpsilonM).ToList();
    }

    /// <summary>Доли отрезка внутри прямоугольника (Лианг — Барски) или null.</summary>
    private static (double, double)? Clip(WorldPoint a, WorldPoint b, WorldBounds r)
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

    // ---------- Общее ----------

    private RailPlan Check(RailEdit edit, List<BuildProblem> problems, IReadOnlyList<PathGeometry> shapes, string summary)
    {
        if (problems.Count == 0)
        {
            var check = Rails.Preview(edit);
            problems.AddRange(check.Issues.Select(i => new BuildProblem("rail", Sentence(i))));
        }
        return new RailPlan(edit, problems, shapes, problems.Count > 0 ? problems[0].Message : summary);
    }

    private RailPlan Commit(RailPlan plan)
    {
        if (!plan.CanBuild)
            return plan;
        var result = Rails.Commit(plan.Edit!);
        return result.Committed
            ? plan with { Result = result }
            : plan with { Problems = result.Issues.Select(i => new BuildProblem("rail", Sentence(i))).ToList(), Result = result };
    }
}
