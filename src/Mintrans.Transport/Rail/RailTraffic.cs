using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Runtime;
using Mintrans.World;
using Mintrans.World.Rail;

namespace Mintrans.Transport.Rail;

/// <summary>
/// Граница блока в точке пути (docs/10а): сигнал игрока или встроенный сигнал на конце платформы и депо.
/// <paramref name="SafeStop"/> — перед ней можно остановиться, поэтому маршрутный сигнал закрепляет путь только до неё.
/// </summary>
/// <param name="DistanceM">Метры от начала пути (From).</param>
public sealed record RailBoundary(TrackEdgeId Edge, double DistanceM, SignalLogic Logic, bool SafeStop, RailSignal? Signal, string Where)
{
    /// <summary>Односторонний маршрутный сигнал: от него нельзя развернуться и сквозь него нельзя ехать навстречу.</summary>
    public bool OneWayPath => Signal is { Logic: SignalLogic.Path, TwoWay: false };

    /// <summary>Действует ли граница на поезд, идущий по пути в направлении <paramref name="forward"/>.</summary>
    public bool Controls(bool forward) => Signal?.Controls(forward) ?? true;
}

/// <summary>
/// Сигнализация и занятость путей рельсовой сети (docs/10а, docs/10в, раздел 5): какие участки заняты составами,
/// какие закреплены маршрутными сигналами, где проходят границы блоков, свободен ли блок, закрыт ли переезд.
/// Занятость считается по длине состава в метрах вдоль путей и конфликтным зонам стрелок.
/// Не двигает поезда — это делает <see cref="Systems.TransportSystem"/>.
/// </summary>
public sealed class RailTraffic
{
    /// <summary>Насколько за границу блока заглядывает проверка блока и закрепление пути, м.</summary>
    public const double BeyondM = 0.01;

    private readonly WorldMap _map;
    private readonly TransportState _transport;
    private readonly Dictionary<TrackEdgeId, List<RailBoundary>> _boundaries = new();
    private readonly Dictionary<VehicleId, List<(DirectedEdge Edge, double From, double To)>> _reserved = new();
    private int _builtRevision = -1;

    internal RailTraffic(WorldMap map, TransportState transport, NetworkTransport network)
    {
        _map = map;
        _transport = transport;
        Network = network;
        Rails = map.RequireRails();
        if (Rails.Settings.Kind != network.Network)
            throw new ArgumentException($"Поезда ездят по сети «{network.Network}», а рельсовый граф карты — «{Rails.Settings.Kind}».");
    }

    public NetworkTransport Network { get; }
    public RailNetwork Rails { get; }

    /// <summary>Участки, занятые составами (и обломками). Обновляется по ходу движения.</summary>
    public SpanIndex Occupancy { get; } = new();

    /// <summary>Участки, закреплённые за поездами маршрутными сигналами, станциями и депо.</summary>
    public SpanIndex Reservations { get; } = new();

    // ---------- Занятость ----------

    /// <summary>Какие участки занимает поезд. Поезд в депо и на обслуживании путей не занимает.</summary>
    public IEnumerable<TrackSpan> SpansOf(Vehicle train) =>
        train.State is VehicleState.InDepot or VehicleState.Servicing || train.Body is not { } body ? [] : body.Spans(Rails);

    /// <summary>Занятость заново по положению всех поездов — в начале каждого часа.</summary>
    internal void RebuildOccupancy()
    {
        Occupancy.Clear();
        foreach (var train in Trains())
            foreach (var span in SpansOf(train))
                Occupancy.Add(span, train.Id);
    }

    /// <summary>Поезд сдвинулся: обновить его занятость и снять закрепление с того, что уже позади головы.</summary>
    internal void Moved(Vehicle train)
    {
        Occupancy.Set(train.Id, SpansOf(train));
        if (train.Body is not { } body || !_reserved.TryGetValue(train.Id, out var held))
            return;
        var behind = body.Edges.Skip(1).ToHashSet();
        var kept = new List<(DirectedEdge, double, double)>();
        foreach (var (edge, from, to) in held)
        {
            if (behind.Contains(edge) && edge != body.HeadEdge)
                continue;
            if (edge == body.HeadEdge)
            {
                if (to <= body.HeadM + SpatialScale.EpsilonM)
                    continue;
                kept.Add((edge, Math.Max(from, body.HeadM), to));
            }
            else
                kept.Add((edge, from, to));
        }
        SetReserved(train, kept);
    }

    /// <summary>Снять закрепление пути впереди (поезд сменил маршрут, остановлен или ушёл в депо).</summary>
    internal void ReleaseAhead(Vehicle train) => SetReserved(train, []);

    internal void Forget(Vehicle train)
    {
        Occupancy.RemoveAll(train.Id);
        SetReserved(train, []);
    }

    private void SetReserved(Vehicle train, List<(DirectedEdge Edge, double From, double To)> held)
    {
        if (held.Count == 0)
            _reserved.Remove(train.Id);
        else
            _reserved[train.Id] = held;
        Reservations.Set(train.Id, held.Select(h => TrackSpan.Along(Rails, h.Edge, h.From, h.To)));
    }

    /// <summary>Закреплён ли за поездом путь сразу за точкой <paramref name="at"/> по ходу движения.</summary>
    public bool HoldsAhead(Vehicle train, TrackPosition at) =>
        Reservations.Owns(TrackSpan.Along(Rails, at.Edge, at.DistanceM, at.DistanceM + BeyondM), train.Id);

    private IEnumerable<Vehicle> Trains() => _transport.Vehicles.Where(v => v.Network.Network == Network.Network);

    /// <summary>
    /// Перенести положения, занятость и закрепления на части разделённых путей (docs/10в, раздел 3).
    /// Поезд, под которым снесли путь, теряет положение и маршрут.
    /// </summary>
    internal void Remap(RailChange change)
    {
        foreach (var train in Trains().ToList())
        {
            if (train.Body is { } body)
            {
                var moved = body.Remapped(Rails, change);
                train.Body = moved;
                if (moved is null && train.State is not (VehicleState.InDepot or VehicleState.Servicing))
                    train.SetState(VehicleState.Stuck, "путь под составом снесён");
            }
            train.RailRoute = null;
            if (_reserved.TryGetValue(train.Id, out var held))
            {
                var kept = new List<(DirectedEdge, double, double)>();
                foreach (var (edge, from, to) in held)
                {
                    if (!change.Replaced.ContainsKey(edge.Edge))
                    {
                        kept.Add((edge, from, to));
                        continue;
                    }
                    // Закреплённый участок переходит на части пути, которые его покрывают.
                    var length = change.OldLengths[edge.Edge];
                    var (a, b) = edge.Forward ? (from, to) : (length - to, length - from);
                    foreach (var piece in change.Replaced[edge.Edge])
                    {
                        var (pa, pb) = (Math.Max(a, piece.OffsetM), Math.Min(b, piece.OffsetM + piece.LengthM));
                        if (pb - pa <= SpatialScale.EpsilonM)
                            continue;
                        var (la, lb) = (pa - piece.OffsetM, pb - piece.OffsetM);
                        var directed = new DirectedEdge(piece.Edge, edge.Forward);
                        kept.Add(edge.Forward ? (directed, la, lb) : (directed, piece.LengthM - lb, piece.LengthM - la));
                    }
                }
                SetReserved(train, kept);
            }
        }
        _builtRevision = -1;
        RebuildOccupancy();
    }

    // ---------- Границы блоков ----------

    private void Refresh()
    {
        if (Rails.Revision == _builtRevision)
            return;
        _boundaries.Clear();
        foreach (var signal in Rails.Signals)
            Add(new RailBoundary(signal.Edge, signal.DistanceM, signal.Logic, true, signal, "сигнала"));
        // Встроенные сигналы станций и депо (docs/10а): на обоих концах стоянки.
        foreach (var berth in Rails.Berths)
        {
            var where = berth.Kind == BerthKind.Platform ? "станции" : "депо";
            Add(new RailBoundary(berth.Edge, berth.FromM, SignalLogic.Path, false, null, where));
            Add(new RailBoundary(berth.Edge, berth.ToM, SignalLogic.Path, false, null, where));
        }
        foreach (var list in _boundaries.Values)
            list.Sort((a, b) => a.DistanceM.CompareTo(b.DistanceM));
        _builtRevision = Rails.Revision;

        void Add(RailBoundary boundary)
        {
            if (!_boundaries.TryGetValue(boundary.Edge, out var list))
                _boundaries[boundary.Edge] = list = [];
            list.Add(boundary);
        }
    }

    public IReadOnlyList<RailBoundary> BoundariesOn(TrackEdgeId edge)
    {
        Refresh();
        return _boundaries.TryGetValue(edge, out var list) ? list : [];
    }

    /// <summary>
    /// Первая граница по ходу <paramref name="edge"/>, действующая в эту сторону, строго после <paramref name="fromM"/>
    /// и не дальше <paramref name="toM"/> метров хода, и её положение в метрах хода.
    /// </summary>
    public (RailBoundary Boundary, double AtM)? BoundaryAhead(DirectedEdge edge, double fromM, double toM)
    {
        var length = Rails.LengthOf(edge);
        (RailBoundary, double)? best = null;
        foreach (var boundary in BoundariesOn(edge.Edge))
        {
            if (!boundary.Controls(edge.Forward))
                continue;
            var at = edge.Forward ? boundary.DistanceM : length - boundary.DistanceM;
            if (at > fromM + 1e-6 && at <= toM + 1e-9 && (best is null || at < best.Value.Item2))
                best = (boundary, at);
        }
        return best;
    }

    /// <summary>
    /// Свободен ли блок, начинающийся сразу за точкой <paramref name="start"/>, для поезда <paramref name="self"/>:
    /// в нём нет других составов и чужих закреплённых участков (блочный сигнал, docs/10а). Блок — все пути,
    /// связанные с точкой без пересечения границ, в обе стороны и через все стрелки.
    /// Возвращает поезд, который мешает, или null, если блок свободен.
    /// </summary>
    public VehicleId? BlockBlocker(TrackPosition start, VehicleId self)
    {
        Refresh();
        var startEdge = start.Edge;
        var s = Rails.EdgeDistance(start);
        var queue = new Queue<(TrackEdgeId Edge, double From, int Direction)>();
        var seen = new HashSet<(TrackEdgeId, long, int)>();
        void Push(TrackEdgeId edge, double from, int direction)
        {
            if (seen.Add((edge, (long)Math.Round(from * 1000), direction)))
                queue.Enqueue((edge, from, direction));
        }
        Push(startEdge.Edge, s, +1);
        Push(startEdge.Edge, s, -1);
        while (queue.Count > 0 && seen.Count < 50_000)
        {
            var (edgeId, from, direction) = queue.Dequeue();
            var edge = Rails.Edge(edgeId);
            var stop = direction > 0 ? edge.LengthM : 0.0;
            var boundary = BoundariesOn(edgeId)
                .Where(b => direction > 0 ? b.DistanceM > from + 1e-9 : b.DistanceM < from - 1e-9)
                .Select(b => b.DistanceM)
                .DefaultIfEmpty(double.NaN)
                .Aggregate(double.NaN, (acc, d) => double.IsNaN(acc) ? d : direction > 0 ? Math.Min(acc, d) : Math.Max(acc, d));
            var end = double.IsNaN(boundary) ? stop : boundary;
            var span = new TrackSpan(edgeId, Math.Min(from, end), Math.Max(from, end));
            if ((Occupancy.ConflictWith(Rails, span, self) ?? Reservations.ConflictWith(Rails, span, self)) is { } blocker)
                return blocker;
            if (!double.IsNaN(boundary))
                continue;
            var node = direction > 0 ? edge.To : edge.From;
            foreach (var other in Rails.EdgesAt(node).Where(e => e.Id != edgeId))
            {
                if (other.From == node)
                    Push(other.Id, 0, +1);
                if (other.To == node)
                    Push(other.Id, other.LengthM, -1);
            }
        }
        return null;
    }

    /// <summary>
    /// Маршрутный сигнал (docs/10а): закрепить за поездом путь маршрута от точки <paramref name="fromM"/> пути
    /// с номером <paramref name="index"/> до ближайшего безопасного места — сигнала по ходу движения, конца маршрута
    /// или разворота. Всё или ничего: если хоть один участок занят или закреплён другим поездом, ничего не закрепляется.
    /// Возвращает поезд, который мешает, или null при успехе.
    /// </summary>
    public VehicleId? TryReserve(Vehicle train, TrainRoute route, int index, double fromM)
    {
        var parts = new List<(DirectedEdge Edge, double From, double To)>();
        var start = fromM;
        for (var i = index; i < route.Edges.Count; i++)
        {
            var edge = route.Edges[i];
            var last = i == route.Edges.Count - 1;
            var end = last ? route.TargetM : Rails.LengthOf(edge);
            var safe = BoundaryAhead(edge, start, end) is { Boundary.SafeStop: true } b ? b.AtM : (double?)null;
            var to = safe ?? end;
            if (to > start + 1e-9)
                parts.Add((edge, start, to));
            if (safe is not null || last || route.ReversesAfter(i))
                break;
            start = 0;
        }
        foreach (var (edge, from, to) in parts)
        {
            var span = TrackSpan.Along(Rails, edge, from, to);
            if ((Occupancy.ConflictWith(Rails, span, train.Id) ?? Reservations.ConflictWith(Rails, span, train.Id)) is { } blocker)
                return blocker;
        }
        var held = _reserved.TryGetValue(train.Id, out var existing) ? existing.ToList() : [];
        held.AddRange(parts);
        SetReserved(train, held);
        return null;
    }

    /// <summary>
    /// Ожидаемая задержка въезда на путь при выборе маршрута поезда (docs/12: минимальное ожидаемое время): занятые и
    /// закреплённые другими поездами пути дороже; ехать навстречу одностороннему маршрутному сигналу нельзя.
    /// </summary>
    internal double? EnterDelay(Vehicle train, DirectedEdge next)
    {
        if (BoundariesOn(next.Edge).Any(b => b is { OneWayPath: true } && !b.Controls(next.Forward)))
            return null;
        var whole = new TrackSpan(next.Edge, 0, Rails.LengthOf(next));
        return Occupancy.ConflictWith(Rails, whole, train.Id) is not null || Reservations.ConflictWith(Rails, whole, train.Id) is not null
            ? _transport.Catalog.Rail.OccupiedDelayHours
            : 0;
    }

    // ---------- Сигналы для игрока ----------

    /// <summary>
    /// Открыт ли сигнал в сторону движения по пути <paramref name="forward"/>: блочный — следующий блок свободен,
    /// маршрутный — за ним закреплён путь какого-нибудь поезда (как в OpenTTD, по умолчанию он красный).
    /// </summary>
    public bool IsGreen(RailSignal signal, bool forward)
    {
        var edge = new DirectedEdge(signal.Edge, forward);
        var at = Rails.PositionAt(edge, signal.DistanceM);
        var beyond = at with { DistanceM = at.DistanceM + BeyondM };
        return signal.Logic == SignalLogic.Block
            ? BlockBlocker(beyond, new VehicleId(0)) is null
            : Reservations.Any(TrackSpan.Along(Rails, edge, at.DistanceM, at.DistanceM + 1));
    }

    // ---------- Переезды ----------

    /// <summary>
    /// Закрыт ли переезд в клетке для машин (docs/10а): на нём или за ним закреплён путь поезда, стоит поезд
    /// или поезд по маршруту подходит ближе чем на <see cref="RailTrafficRules.CrossingCloseCells"/> клеток.
    /// </summary>
    public bool IsCrossingClosed(GridPos cell)
    {
        var crossings = _map.Crossings.At(cell);
        if (crossings.Count == 0)
            return false;
        foreach (var crossing in crossings)
        {
            var span = new TrackSpan(crossing.Edge, crossing.FromM, crossing.ToM);
            if (Occupancy.Any(span) || Reservations.Any(span))
                return true;
        }
        var ahead = _transport.Catalog.Rail.CrossingCloseCells * SpatialScale.CellM;
        foreach (var train in Trains().Where(t => t.State == VehicleState.Travelling && t.Body is not null && t.RailRoute is not null))
            foreach (var span in Ahead(train, ahead))
                if (crossings.Any(c => span.Overlaps(new TrackSpan(c.Edge, c.FromM, c.ToM), 0)))
                    return true;
        return false;
    }

    /// <summary>Участки маршрута на <paramref name="metres"/> метров впереди головы поезда.</summary>
    public IEnumerable<TrackSpan> Ahead(Vehicle train, double metres)
    {
        if (train.Body is not { } body || train.RailRoute is not { } route)
            yield break;
        var index = train.RailRouteIndex;
        var from = body.HeadM;
        var left = metres;
        for (var i = index; i < route.Edges.Count && left > 0; i++)
        {
            var edge = route.Edges[i];
            var end = i == route.Edges.Count - 1 ? route.TargetM : Rails.LengthOf(edge);
            var to = Math.Min(end, from + left);
            if (to > from)
                yield return TrackSpan.Along(Rails, edge, from, to);
            left -= to - from;
            if (route.ReversesAfter(i))
                yield break;
            from = 0;
        }
    }
}
