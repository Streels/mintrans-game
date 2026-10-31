using Mintrans.Core.Grid;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.World;
using Mintrans.World.Networks;

namespace Mintrans.Transport.Rail;

/// <summary>
/// Граница блока, которую поезд пересекает (docs/10а): сигнал игрока или встроенный сигнал станции и депо.
/// <paramref name="SafeStop"/> — перед ней можно остановиться, поэтому маршрутный сигнал закрепляет путь только до неё.
/// </summary>
public sealed record RailBoundary(SignalLogic Logic, bool SafeStop, Signal? Signal, string Where)
{
    /// <summary>Односторонний маршрутный сигнал: от него нельзя развернуться и сквозь него нельзя ехать навстречу.</summary>
    public bool OneWayPath => Signal is { Logic: SignalLogic.Path, TwoWay: false };
}

/// <summary>
/// Сигнализация и занятость путей одной рельсовой сети (docs/10а): какие участки заняты поездами,
/// какие закреплены маршрутными сигналами, где проходят границы блоков, свободен ли блок, закрыт ли переезд.
/// Не двигает поезда — это делает <see cref="Systems.TransportSystem"/>.
/// </summary>
public sealed class RailTraffic
{
    private readonly WorldMap _map;
    private readonly TransportState _transport;
    private readonly HashSet<(GridPos Cell, Direction Side)> _stationEnds = new();
    private readonly HashSet<(GridPos Cell, Direction Side)> _depotEnds = new();
    private int _builtRevision = -1;

    internal RailTraffic(WorldMap map, TransportState transport, NetworkTransport network)
    {
        _map = map;
        _transport = transport;
        Network = network;
        Layer = map.Networks[network.Network] as TrackNetworkLayer
            ?? throw new ArgumentException($"Поезда ездят по сети «{network.Network}», но она не рельсовая.");
    }

    public NetworkTransport Network { get; }
    public TrackNetworkLayer Layer { get; }

    /// <summary>Участки, занятые составами (и обломками). Обновляется по ходу движения.</summary>
    public SegmentIndex Occupancy { get; } = new();

    /// <summary>Участки, закреплённые за поездами маршрутными сигналами, станциями и депо.</summary>
    public SegmentIndex Reservations { get; } = new();

    // ---------- Участки ----------

    /// <summary>Участок пути, который занимает точка маршрута.</summary>
    public TrackSegment Segment(RoutePoint point)
    {
        if (point.Elevated)
        {
            var side = point.Entry ?? point.Exit ?? Layer.BridgeOver(point.Cell)?.Axis ?? Direction.North;
            return new TrackSegment(point.Cell, Tracks.StraightThrough(side), true);
        }
        return new TrackSegment(point.Cell, Layer.PieceFor(point.Cell, point.Entry, point.Exit), false);
    }

    /// <summary>Какие участки занимает поезд. Поезд, ещё не выехавший из депо, путей не занимает.</summary>
    public IEnumerable<TrackSegment> SegmentsOf(Vehicle train)
    {
        if (train.State is VehicleState.InDepot or VehicleState.Servicing || InsideDepot(train))
            yield break;
        yield return Segment(train.Head);
        foreach (var point in train.TrailPoints)
            yield return Segment(point);
    }

    private bool InsideDepot(Vehicle train) =>
        train.TrailPoints.Count == 0 && _map.Facilities.At(train.Position) is { } facility && facility.Kind == Network.Depot;

    /// <summary>Занятость заново по положению всех поездов — в начале каждого часа.</summary>
    internal void RebuildOccupancy()
    {
        Occupancy.Clear();
        foreach (var train in Trains())
        {
            foreach (var segment in SegmentsOf(train))
                Occupancy.Add(segment, train.Id);
        }
    }

    /// <summary>
    /// Поезд сдвинулся: обновить его занятость и снять закрепление с участков, которые покинул хвост.
    /// </summary>
    internal void Moved(Vehicle train, IReadOnlyCollection<TrackSegment> before)
    {
        Occupancy.RemoveAll(train.Id);
        var after = SegmentsOf(train).ToHashSet();
        foreach (var segment in after)
            Occupancy.Add(segment, train.Id);
        foreach (var segment in before.Where(s => !after.Contains(s)))
            Reservations.Remove(segment, train.Id);
    }

    /// <summary>Снять закрепление с участков, которые поезд не занимает (он сменил маршрут или ушёл в депо).</summary>
    internal void ReleaseAhead(Vehicle train)
    {
        var occupied = SegmentsOf(train).ToHashSet();
        foreach (var segment in Reservations.Of(train.Id).Where(s => !occupied.Contains(s)).ToList())
            Reservations.Remove(segment, train.Id);
    }

    internal void Forget(Vehicle train)
    {
        Occupancy.RemoveAll(train.Id);
        Reservations.RemoveAll(train.Id);
    }

    private IEnumerable<Vehicle> Trains() => _transport.Vehicles.Where(v => v.Network.Network == Network.Network);

    // ---------- Границы блоков ----------

    private void Refresh()
    {
        var revision = Layer.Revision * 31 + _map.Sites.Revision * 17 + _map.Facilities.Revision;
        if (revision == _builtRevision)
            return;
        _stationEnds.Clear();
        _depotEnds.Clear();
        // Встроенные сигналы станций (docs/10а): на каждом выезде с платформы за её пределы.
        foreach (var stop in _transport.Stops.All.Where(s => s.Network == Network.Network))
        {
            foreach (var cell in stop.Area.Cells())
            {
                foreach (var side in Layer.EdgesAt(cell).Enumerate())
                {
                    if (Layer.Follow(cell, side) is { } link && !stop.Area.Contains(link.Cell))
                        _stationEnds.Add((cell, side));
                }
            }
        }
        foreach (var depot in _map.Facilities.All.Where(f => f.Kind == Network.Depot && f.Entrance is not null))
            _depotEnds.Add((depot.Area.Origin, depot.Entrance!.Value));
        _builtRevision = revision;
    }

    /// <summary>
    /// Граница, которую пересекает поезд, выезжая из точки <paramref name="here"/> по переходу <paramref name="next"/>, или null.
    /// Сигнал игрока действует только в ту сторону, куда смотрит; станция и депо — в обе.
    /// </summary>
    public RailBoundary? Boundary(RoutePoint here, NetworkLink next)
    {
        if (here.Elevated || next.Elevated || here.Exit is not { } exit)
            return null;
        Refresh();
        var piece = Layer.PieceFor(here.Cell, here.Entry, exit);
        if (Layer.SignalOn(here.Cell, piece) is { } signal && signal.Controls(exit))
            return new RailBoundary(signal.Logic, true, signal, "сигнала");
        if (_stationEnds.Contains((here.Cell, exit)) || _stationEnds.Contains((next.Cell, next.Entry)))
            return new RailBoundary(SignalLogic.Path, false, null, "станции");
        if (_depotEnds.Contains((here.Cell, exit)) || _depotEnds.Contains((next.Cell, next.Entry)))
            return new RailBoundary(SignalLogic.Path, false, null, "депо");
        return null;
    }

    /// <summary>Разделяет ли стык двух участков блоки — в любом направлении движения.</summary>
    private bool IsBlockEdge(TrackSegment from, Direction exit, NetworkLink link, TrackSegment to)
    {
        if (from.Elevated || to.Elevated)
            return false;
        if (Layer.SignalOn(from.Cell, from.Piece)?.Controls(exit) == true || Layer.SignalOn(to.Cell, to.Piece)?.Controls(link.Entry) == true)
            return true;
        return _stationEnds.Contains((from.Cell, exit)) || _stationEnds.Contains((to.Cell, link.Entry))
            || _depotEnds.Contains((from.Cell, exit)) || _depotEnds.Contains((to.Cell, link.Entry));
    }

    /// <summary>Участки, на которые можно попасть переходом: на пролёте — прямой, на земле — все куски, выходящие к стороне въезда.</summary>
    public IEnumerable<TrackSegment> SegmentsEntered(NetworkLink link)
    {
        if (link.Elevated)
        {
            yield return new TrackSegment(link.Cell, Tracks.StraightThrough(link.Entry), true);
            yield break;
        }
        foreach (var piece in Layer.PiecesAt(link.Cell).Enumerate().Where(p => p.Touches(link.Entry)))
            yield return new TrackSegment(link.Cell, piece, false);
    }

    /// <summary>
    /// Свободен ли блок, начинающийся с участка <paramref name="start"/>, для поезда <paramref name="self"/>:
    /// в нём нет других составов и чужих закреплённых участков (блочный сигнал, docs/10а).
    /// Возвращает поезд, который мешает, или null, если блок свободен.
    /// </summary>
    public VehicleId? BlockBlocker(TrackSegment start, VehicleId self)
    {
        Refresh();
        var seen = new HashSet<TrackSegment> { start };
        var queue = new Queue<TrackSegment>();
        queue.Enqueue(start);
        while (queue.Count > 0 && seen.Count < 20000)
        {
            var segment = queue.Dequeue();
            if ((Occupancy.ConflictWith(segment, self) ?? Reservations.ConflictWith(segment, self)) is { } blocker)
                return blocker;
            var (a, b) = segment.Piece.Sides();
            foreach (var side in new[] { a, b })
            {
                if (Layer.Follow(segment.Cell, side, segment.Elevated) is not { } link)
                    continue;
                foreach (var next in SegmentsEntered(link))
                {
                    if (!IsBlockEdge(segment, side, link, next) && seen.Add(next))
                        queue.Enqueue(next);
                }
            }
            // Сходящиеся куски одной клетки — физически одно место: стрелка входит в блок целиком.
            if (!segment.Elevated)
            {
                foreach (var piece in Layer.PiecesAt(segment.Cell).Enumerate().Where(p => p != segment.Piece && Tracks.Overlap(p, segment.Piece)))
                {
                    var other = new TrackSegment(segment.Cell, piece, false);
                    if (seen.Add(other))
                        queue.Enqueue(other);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Маршрутный сигнал (docs/10а): закрепить за поездом участки маршрута, начиная с точки <paramref name="from"/>,
    /// до ближайшего безопасного места — сигнала по ходу движения, конца маршрута или тупика.
    /// Всё или ничего: если хоть один участок занят или закреплён другим поездом, ничего не закрепляется.
    /// Возвращает поезд, который мешает, или null при успехе.
    /// </summary>
    public VehicleId? TryReserve(Vehicle train, IReadOnlyList<RoutePoint> route, int from)
    {
        var segments = new List<TrackSegment>();
        for (var k = from; k < route.Count; k++)
        {
            var point = route[k];
            var segment = Segment(point);
            if ((Occupancy.ConflictWith(segment, train.Id) ?? Reservations.ConflictWith(segment, train.Id)) is { } blocker)
                return blocker;
            segments.Add(segment);
            if (k == route.Count - 1 || point.IsReversal)
                break;
            var next = route[k + 1];
            if (Boundary(point, new NetworkLink(next.Cell, next.Entry!.Value, next.Elevated)) is { SafeStop: true })
                break;
        }
        foreach (var segment in segments)
            Reservations.Add(segment, train.Id);
        return null;
    }

    /// <summary>
    /// Задержка шага при выборе маршрута поезда (docs/12: минимальное ожидаемое время): занятые и закреплённые
    /// другими поездами участки дороже; ехать навстречу одностороннему маршрутному сигналу нельзя.
    /// </summary>
    internal double? StepDelay(Vehicle train, RoutePoint here, NetworkLink next)
    {
        if (!here.Elevated && here.Exit is { } exit)
        {
            var piece = Layer.PieceFor(here.Cell, here.Entry, exit);
            if (Layer.SignalOn(here.Cell, piece) is { Logic: SignalLogic.Path } signal && !signal.Controls(exit))
                return null;
        }
        var delay = 0.0;
        foreach (var segment in SegmentsEntered(next).Take(1))
        {
            if (Occupancy.ConflictWith(segment, train.Id) is not null || Reservations.ConflictWith(segment, train.Id) is not null)
                delay += _transport.Catalog.Rail.OccupiedDelayHours;
        }
        return delay;
    }

    // ---------- Сигналы для игрока ----------

    /// <summary>
    /// Открыт ли сигнал в сторону <paramref name="exit"/>: блочный — следующий блок свободен,
    /// маршрутный — за ним закреплён путь какого-нибудь поезда (как в OpenTTD, по умолчанию он красный).
    /// </summary>
    public bool IsGreen(Signal signal, Direction exit)
    {
        if (Layer.Follow(signal.Cell, exit) is not { } link)
            return false;
        var next = SegmentsEntered(link).ToList();
        return signal.Logic == SignalLogic.Block
            ? next.All(s => BlockBlocker(s, new VehicleId(0)) is null)
            : next.Any(s => Reservations.OwnerOf(s) is not null);
    }

    // ---------- Переезды ----------

    /// <summary>
    /// Закрыт ли переезд в клетке для машин (docs/10а): на нём или за ним закреплён путь поезда,
    /// стоит поезд или поезд подходит ближе чем на <see cref="RailTrafficRules.CrossingCloseCells"/> клеток.
    /// </summary>
    public bool IsCrossingClosed(GridPos cell)
    {
        if (!Layer.HasAny(cell))
            return false;
        if (Occupancy.AnyAt(cell) || Reservations.AnyAt(cell))
            return true;
        var ahead = _transport.Catalog.Rail.CrossingCloseCells;
        foreach (var train in Trains().Where(t => t.State == VehicleState.Travelling))
        {
            var path = train.Path;
            for (var k = train.PathIndex; k < path.Count && k <= train.PathIndex + ahead; k++)
            {
                if (path[k].Cell == cell && !path[k].Elevated)
                    return true;
            }
        }
        return false;
    }
}
