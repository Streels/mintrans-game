using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;
using Mintrans.World.Spatial;

namespace Mintrans.World.Rail;

/// <summary>
/// Неизменяемые свойства рельсовой сети.
/// </summary>
/// <param name="Kind">Вид габарита пути в пространственном мире (тип сети, например <c>rail</c>).</param>
/// <param name="HalfWidthM">Половина ширины строительного коридора пути. Два пути с осями через 5 м не мешают друг другу.</param>
/// <param name="MinRadiusM">Наименьший радиус кривой — один для всего подвижного состава (docs/10в). Круче сеть не принимает.</param>
public sealed record RailNetworkSettings(TypeId Kind, double HalfWidthM = 2.0, double MinRadiusM = 200);

/// <summary>Результат проверки или применения изменения рельсовой сети.</summary>
public sealed class RailEditPreview
{
    internal RailEditPreview(IReadOnlyList<string> issues, SpatialPreview? spatial, IReadOnlyList<TrackEdge> edges)
    {
        Issues = issues;
        Spatial = spatial;
        Edges = edges;
    }

    /// <summary>Причины отказа: геометрия, конфигурация узлов, пересечения, местность. Пусто — можно строить.</summary>
    public IReadOnlyList<string> Issues { get; }

    /// <summary>Проверка габаритов в пространственном мире: клетки новых путей и конфликты.</summary>
    public SpatialPreview? Spatial { get; }

    /// <summary>Новые пути с номерами их габаритов.</summary>
    public IReadOnlyList<TrackEdge> Edges { get; }

    /// <summary>Сигналы, которые исчезнут вместе со снесёнными путями или по прямому указанию.</summary>
    public IReadOnlyList<RailSignal> LostSignals { get; internal init; } = [];

    /// <summary>Платформы и стоянки депо, которые исчезнут вместе со снесёнными путями или по прямому указанию.</summary>
    public IReadOnlyList<RailBerth> LostBerths { get; internal init; } = [];

    public bool IsValid => Issues.Count == 0;

    public bool Committed { get; internal set; }
}

/// <summary>
/// Что изменилось в сети после применения: какие пути ушли и на какие части они разделились.
/// По ней модули, хранящие положения на путях (транспорт), переносят свои данные на новые номера путей.
/// </summary>
/// <param name="OldLengths">Длины прежних путей, м.</param>
public sealed record RailChange(IReadOnlyDictionary<TrackEdgeId, IReadOnlyList<EdgePiece>> Replaced,
    IReadOnlyDictionary<TrackEdgeId, double> OldLengths)
{
    /// <summary>Положение на прежнем пути в той же точке нового пути; null, если этот участок снесён.</summary>
    public (TrackEdgeId Edge, double DistanceM)? Map(TrackEdgeId edge, double distanceM)
    {
        if (!Replaced.TryGetValue(edge, out var pieces))
            return (edge, distanceM);
        foreach (var piece in pieces)
            if (piece.Covers(distanceM))
                return (piece.Edge, Math.Clamp(distanceM - piece.OffsetM, 0, piece.LengthM));
        return null;
    }
}

/// <summary>
/// Дополнительная проверка изменения сети — от других частей мира: площадки, дороги, вода, занятые поездами пути.
/// Правила подключаются списком <see cref="RailNetwork.Rules"/>; сама сеть о площадках и поездах не знает.
/// </summary>
public interface IRailRule
{
    IEnumerable<string> Check(RailNetwork network, RailEdit edit);
}

/// <summary>
/// Рельсовый граф (docs/02в, раздел 7; docs/10в): узлы, пути с точной метрической геометрией и длиной, стрелки
/// с конфликтными зонами, сигналы и стоянки (платформы, депо) в метрических точках путей. Клетки не вершины графа:
/// соседство клеток ничего не соединяет, соединяют только общие узлы. Переход в узле разрешён только без излома —
/// по путям, продолжающим направление движения. Каждый путь — габарит в общем пространственном мире,
/// где и проверяются пересечения; мост — путь уровня выше узлов, над тем, что лежит на земле.
/// </summary>
public sealed class RailNetwork
{
    /// <summary>Допуск совпадения направлений путей в узле, рад.</summary>
    public const double HeadingToleranceRad = 1e-6;

    /// <summary>Сигнал не ставится ближе этого к узлу: граница блока должна лежать внутри пути.</summary>
    public const double SignalNodeMarginM = 1.0;

    private readonly SortedDictionary<TrackNodeId, TrackNode> _nodes = new();
    private readonly SortedDictionary<TrackEdgeId, TrackEdge> _edges = new();
    private readonly Dictionary<TrackNodeId, List<TrackEdgeId>> _incident = new();
    private readonly SortedDictionary<TrackNodeId, TrackJunction> _junctions = new();
    private readonly Dictionary<ObjectId, TrackEdgeId> _bySpatial = new();
    private readonly SortedDictionary<RailSignalId, RailSignal> _signals = new();
    private readonly SortedDictionary<RailBerthId, RailBerth> _berths = new();

    public RailNetwork(SpatialWorld world, RailNetworkSettings settings)
    {
        World = world;
        Settings = settings;
    }

    public SpatialWorld World { get; }
    public RailNetworkSettings Settings { get; }

    /// <summary>Проверки от других частей мира. Порядок — порядок сообщений об отказе.</summary>
    public List<IRailRule> Rules { get; } = [];

    /// <summary>Растёт с каждым применённым изменением: по ней транспорт и отображение узнают, что сеть изменилась.</summary>
    public int Revision { get; private set; }

    /// <summary>Сообщает о применённом изменении — чтобы перенести положения на разделённые пути.</summary>
    public event Action<RailChange>? Changed;

    public TrackNodeId NextNodeId { get; private set; } = new(1);
    public TrackEdgeId NextEdgeId { get; private set; } = new(1);
    public RailSignalId NextSignalId { get; private set; } = new(1);
    public RailBerthId NextBerthId { get; private set; } = new(1);

    public IEnumerable<TrackNode> Nodes => _nodes.Values;
    public IEnumerable<TrackEdge> Edges => _edges.Values;
    public IEnumerable<TrackJunction> Junctions => _junctions.Values;
    public IEnumerable<RailSignal> Signals => _signals.Values;
    public IEnumerable<RailBerth> Berths => _berths.Values;

    public bool ContainsEdge(TrackEdgeId id) => _edges.ContainsKey(id);

    public TrackEdge Edge(TrackEdgeId id) =>
        _edges.TryGetValue(id, out var edge) ? edge : throw new KeyNotFoundException($"В сети нет пути {id}.");

    public TrackNode Node(TrackNodeId id) =>
        _nodes.TryGetValue(id, out var node) ? node : throw new KeyNotFoundException($"В сети нет узла {id}.");

    public IReadOnlyList<TrackEdge> EdgesAt(TrackNodeId node) =>
        _incident.TryGetValue(node, out var ids) ? ids.Select(Edge).ToList() : [];

    public TrackJunction? JunctionAt(TrackNodeId node) => _junctions.GetValueOrDefault(node);

    public RailSignal Signal(RailSignalId id) => _signals[id];

    public RailBerth Berth(RailBerthId id) => _berths[id];

    public bool ContainsBerth(RailBerthId id) => _berths.ContainsKey(id);

    public IEnumerable<RailSignal> SignalsOn(TrackEdgeId edge) => _signals.Values.Where(s => s.Edge == edge);

    public IEnumerable<RailBerth> BerthsOn(TrackEdgeId edge) => _berths.Values.Where(b => b.Edge == edge);

    /// <summary>Путь, которому принадлежит габарит <paramref name="spatial"/>, если это рельсы этой сети.</summary>
    public TrackEdge? EdgeOf(ObjectId spatial) => _bySpatial.TryGetValue(spatial, out var id) ? _edges[id] : null;

    /// <summary>Мост: путь выше своих узлов (опоры на земле, пролёт над ней).</summary>
    public bool IsBridge(TrackEdge edge) => edge.Level != Node(edge.From).Level;

    /// <summary>Свободные концы — тупики, к которым можно подключить новое соединение.</summary>
    public IEnumerable<TrackEnd> FreeEnds => _nodes.Values
        .Where(n => EdgesAt(n.Id).Count == 1)
        .Select(n => new TrackEnd(n.Id, n.At, Angles.Reverse(EdgesAt(n.Id)[0].HeadingOutOf(n.Id)), n.Level));

    /// <summary>Клетка, по которой считается правило «один сигнал на клетку».</summary>
    public GridPos CellOf(RailSignal signal) => SpatialScale.ToCell(Edge(signal.Edge).Geometry.PointAt(signal.DistanceM));

    public RailEdit Begin() => new(this);

    // ---------- Поиск по месту ----------

    /// <summary>
    /// Ближайшая точка путей к <paramref name="p"/> не дальше <paramref name="maxDistanceM"/>: путь и расстояние от его начала.
    /// Мосты учитываются, только если <paramref name="includeBridges"/>.
    /// </summary>
    public (TrackEdge Edge, double DistanceM, double OffsetM)? Nearest(WorldPoint p, double maxDistanceM, bool includeBridges = true)
    {
        var near = new WorldBounds(p.X, p.Y, p.X, p.Y).Expand(maxDistanceM);
        (TrackEdge, double, double)? best = null;
        foreach (var id in World.Index.Candidates(SpatialScale.CellsTouching(near)))
        {
            if (EdgeOf(id) is not { } edge || (!includeBridges && IsBridge(edge)))
                continue;
            var (s, offset) = edge.Geometry.Project(p);
            if (offset <= maxDistanceM && (best is null || offset < best.Value.Item3))
                best = (edge, s, offset);
        }
        return best;
    }

    /// <summary>Свободный конец не дальше <paramref name="maxDistanceM"/> от точки.</summary>
    public TrackEnd? NearestFreeEnd(WorldPoint p, double maxDistanceM) =>
        FreeEnds.Where(e => e.At.DistanceTo(p) <= maxDistanceM).MinBy(e => e.At.DistanceTo(p));

    // ---------- Движение по графу ----------

    public TrackNodeId DepartureNode(DirectedEdge d) => d.Forward ? Edge(d.Edge).From : Edge(d.Edge).To;

    public TrackNodeId ArrivalNode(DirectedEdge d) => d.Forward ? Edge(d.Edge).To : Edge(d.Edge).From;

    /// <summary>Куда можно проехать дальше, доехав до конца пути: только по путям, продолжающим направление без излома.</summary>
    public IEnumerable<DirectedEdge> Next(DirectedEdge d)
    {
        var edge = Edge(d.Edge);
        var node = ArrivalNode(d);
        var arriving = d.Forward ? edge.Geometry.EndHeading : Angles.Reverse(edge.Geometry.StartHeading);
        foreach (var other in EdgesAt(node))
        {
            if (other.Id == edge.Id || other.From == other.To)
                continue;
            if (Angles.Difference(other.HeadingOutOf(node), arriving) <= HeadingToleranceRad)
                yield return new DirectedEdge(other.Id, other.From == node);
        }
    }

    /// <summary>Откуда можно приехать на путь: обратное к <see cref="Next"/>.</summary>
    public IEnumerable<DirectedEdge> Previous(DirectedEdge d) => Next(d.Reverse()).Select(p => p.Reverse());

    public double LengthOf(DirectedEdge d) => Edge(d.Edge).LengthM;

    /// <summary>Расстояние вдоль пути от его начала (From) для положения по ходу движения.</summary>
    public double EdgeDistance(TrackPosition position) =>
        position.Edge.Forward ? position.DistanceM : LengthOf(position.Edge) - position.DistanceM;

    /// <summary>Положение по ходу движения <paramref name="d"/> для точки пути на расстоянии от его начала.</summary>
    public TrackPosition PositionAt(DirectedEdge d, double edgeDistanceM) =>
        new(d, d.Forward ? edgeDistanceM : LengthOf(d) - edgeDistanceM);

    /// <summary>Точка и направление движения в положении на пути.</summary>
    public (WorldPoint At, double Heading) Locate(TrackPosition position)
    {
        var edge = Edge(position.Edge.Edge);
        var s = Math.Clamp(position.DistanceM, 0, edge.LengthM);
        return position.Edge.Forward
            ? (edge.Geometry.PointAt(s), edge.Geometry.HeadingAt(s))
            : (edge.Geometry.PointAt(edge.LengthM - s), Angles.Reverse(edge.Geometry.HeadingAt(edge.LengthM - s)));
    }

    // ---------- Изменение ----------

    public RailEditPreview Preview(RailEdit edit) => Check(edit, out _);

    /// <summary>Применить изменение целиком: сначала вся проверка, затем габариты и граф. При отказе ничего не меняется.</summary>
    public RailEditPreview Commit(RailEdit edit)
    {
        var preview = Check(edit, out var change);
        if (!preview.IsValid || change is null)
            return preview;
        var spatial = World.Commit(change);
        if (!spatial.Committed)
            return new RailEditPreview(spatial.Issues.Select(Describe).ToList(), spatial, preview.Edges);

        var replaced = new Dictionary<TrackEdgeId, IReadOnlyList<EdgePiece>>();
        var oldLengths = new Dictionary<TrackEdgeId, double>();
        var affected = new SortedSet<TrackNodeId>();
        foreach (var id in edit.RemovedEdges)
        {
            var edge = _edges[id];
            replaced[id] = edit.PiecesOf(id);
            oldLengths[id] = edge.LengthM;
            _edges.Remove(id);
            _bySpatial.Remove(edge.Spatial);
            _incident[edge.From].Remove(id);
            _incident[edge.To].Remove(id);
            affected.Add(edge.From);
            affected.Add(edge.To);
        }
        foreach (var node in edit.NewNodes)
        {
            _nodes[node.Id] = node;
            _incident[node.Id] = [];
            affected.Add(node.Id);
        }
        foreach (var edge in preview.Edges)
        {
            _edges[edge.Id] = edge;
            _bySpatial[edge.Spatial] = edge.Id;
            _incident[edge.From].Add(edge.Id);
            _incident[edge.To].Add(edge.Id);
            affected.Add(edge.From);
            affected.Add(edge.To);
        }
        foreach (var node in affected)
        {
            _incident[node].Sort();
            if (_incident[node].Count == 0)
            {
                _nodes.Remove(node);
                _incident.Remove(node);
                _junctions.Remove(node);
                continue;
            }
            if (BuildJunction(node) is { } junction)
                _junctions[node] = junction;
            else
                _junctions.Remove(node);
        }

        // Сигналы и стоянки переезжают на части разделённых путей; на снесённых участках исчезают.
        var change2 = new RailChange(replaced, oldLengths);
        foreach (var signal in preview.LostSignals)
            _signals.Remove(signal.Id);
        foreach (var berth in preview.LostBerths)
            _berths.Remove(berth.Id);
        foreach (var signal in _signals.Values.Where(s => replaced.ContainsKey(s.Edge)).ToList())
        {
            var (edge, distance) = change2.Map(signal.Edge, signal.DistanceM)!.Value;
            _signals[signal.Id] = signal with { Edge = edge, DistanceM = distance };
        }
        foreach (var berth in _berths.Values.Where(b => replaced.ContainsKey(b.Edge)).ToList())
            _berths[berth.Id] = MapBerth(berth, replaced[berth.Edge])!;
        foreach (var signal in edit.NewSignals)
            _signals[signal.Id] = signal;
        foreach (var berth in edit.NewBerths)
            _berths[berth.Id] = berth;

        NextNodeId = new TrackNodeId(NextNodeId.Value + edit.NewNodes.Count);
        NextEdgeId = new TrackEdgeId(NextEdgeId.Value + edit.EdgeIdsUsed);
        NextSignalId = new RailSignalId(NextSignalId.Value + edit.NewSignals.Count);
        NextBerthId = new RailBerthId(NextBerthId.Value + edit.NewBerths.Count);
        Revision++;
        Changed?.Invoke(change2);
        return new RailEditPreview(preview.Issues, spatial, preview.Edges)
        {
            Committed = true,
            LostSignals = preview.LostSignals,
            LostBerths = preview.LostBerths,
        };
    }

    /// <summary>Стоянка на части разделённого пути или null, если её участок снесён или разрезан.</summary>
    private static RailBerth? MapBerth(RailBerth berth, IReadOnlyList<EdgePiece> pieces)
    {
        foreach (var piece in pieces)
            if (piece.Covers(berth.FromM) && piece.Covers(berth.ToM))
                return berth with
                {
                    Edge = piece.Edge,
                    FromM = Math.Max(0, berth.FromM - piece.OffsetM),
                    ToM = Math.Min(piece.LengthM, berth.ToM - piece.OffsetM),
                };
        return null;
    }

    private RailEditPreview Check(RailEdit edit, out SpatialChange? change)
    {
        change = null;
        var issues = new List<string>();
        if (edit.BaseRevision != Revision)
            issues.Add("сеть изменилась после начала постройки — повторите предпросмотр");

        // Сеть после изменения: какие пути у каких узлов.
        var edges = _edges.Values.Where(e => !edit.RemovedEdges.Contains(e.Id)).ToDictionary(e => e.Id);
        foreach (var edge in edit.NewEdges)
            edges[edge.Id] = edge;
        var incident = new Dictionary<TrackNodeId, List<TrackEdge>>();
        foreach (var edge in edges.Values)
        foreach (var node in new[] { edge.From, edge.To }.Distinct())
        {
            if (!incident.TryGetValue(node, out var list))
                incident[node] = list = [];
            list.Add(edge);
        }

        foreach (var edge in edit.NewEdges.Where(e => !edit.IsPiece(e.Id)))
            CheckEdge(edit, edge, issues);
        var touchedNodes = edit.NewEdges.SelectMany(e => new[] { e.From, e.To })
            .Concat(edit.RemovedEdges.SelectMany(id => new[] { _edges[id].From, _edges[id].To }))
            .ToHashSet();
        foreach (var node in touchedNodes.Order())
            if (incident.TryGetValue(node, out var list))
                CheckNode(node, list, issues);

        var (lostSignals, lostBerths) = CheckFixtures(edit, edges, incident, issues);
        foreach (var rule in Rules)
            issues.AddRange(rule.Check(this, edit));
        if (issues.Count > 0)
            return new RailEditPreview(issues, null, edit.NewEdges) { LostSignals = lostSignals, LostBerths = lostBerths };

        // Габариты: старые пути уходят, новые встают; у общих узлов пересечение разрешено в пределах зоны стыка.
        change = World.Begin();
        foreach (var id in edit.RemovedEdges)
            change.Remove(_edges[id].Spatial);
        var placed = new Dictionary<TrackEdgeId, TrackEdge>();
        foreach (var edge in edit.NewEdges)
        {
            placed[edge.Id] = edge with { Spatial = change.Add(Settings.Kind, edge.Level, Corridor(edge.Geometry)) };
            // Часть разделённого пути — та же, уже проверенная геометрия.
            if (edit.IsPiece(edge.Id))
                change.MarkVerified(placed[edge.Id].Spatial);
        }
        TrackEdge Placed(TrackEdge e) => placed.GetValueOrDefault(e.Id) ?? e;

        var joints = new Dictionary<(ObjectId, ObjectId), (List<PathInterval> A, List<PathInterval> B)>();
        foreach (var edge in placed.Values)
        foreach (var node in new[] { edge.From, edge.To }.Distinct())
        foreach (var other in incident[node].Select(Placed))
        {
            if (other.Id == edge.Id)
                continue;
            var (a, b) = edge.Spatial.CompareTo(other.Spatial) < 0 ? (edge, other) : (other, edge);
            if (!joints.TryGetValue((a.Spatial, b.Spatial), out var zone))
                joints[(a.Spatial, b.Spatial)] = zone = ([], []);
            zone.A.Add(ZoneAt(a, node, b));
            zone.B.Add(ZoneAt(b, node, a));
        }
        foreach (var ((a, b), zone) in joints)
            change.AllowJoint(a, b, zone.A, zone.B);

        var spatial = World.Preview(change);
        issues.AddRange(spatial.Issues.Select(Describe));
        return new RailEditPreview(issues, spatial, placed.Values.ToList()) { LostSignals = lostSignals, LostBerths = lostBerths };
    }

    private void CheckEdge(RailEdit edit, TrackEdge edge, List<string> issues)
    {
        var from = edit.Node(edge.From);
        var to = edit.Node(edge.To);
        var geometry = edge.Geometry;
        if (geometry.Length <= SpatialScale.EpsilonM)
            issues.Add($"{edge.Id}: нулевая длина");
        if (from.Id == to.Id)
            issues.Add($"{edge.Id}: петля из узла в тот же узел");
        if (!geometry.Start.ApproxEquals(from.At, SpatialScale.EpsilonM))
            issues.Add($"{edge.Id}: начало {geometry.Start} не совпадает с {from.Id} {from.At}");
        if (!geometry.End.ApproxEquals(to.At, SpatialScale.EpsilonM))
            issues.Add($"{edge.Id}: конец {geometry.End} не совпадает с {to.Id} {to.At}");
        if (from.Level != to.Level)
            issues.Add($"{edge.Id}: концы на разных уровнях — переход между уровнями даёт мост или тоннель");
        if (edge.Level != from.Level)
        {
            if (edge.Level != from.Level + 1)
                issues.Add($"{edge.Id}: мост поднимается над землёй на один уровень; тоннели появятся позже");
            if (!geometry.IsStraight)
                issues.Add($"{edge.Id}: мост строится по прямой");
        }
        foreach (var arc in geometry.Primitives.OfType<ArcPrimitive>())
            if (arc.Radius < Settings.MinRadiusM - SpatialScale.EpsilonM)
                issues.Add($"{edge.Id}: радиус {arc.Radius:0.#} м меньше допустимого {Settings.MinRadiusM:0.#} м");
    }

    /// <summary>
    /// Узел допустим, если все пути выходят из него вдоль одной прямой в две противоположные стороны:
    /// тупик (1), стык (1 + 1) или стрелка (1 + 2). Иначе поезд встретил бы излом.
    /// </summary>
    private static void CheckNode(TrackNodeId node, List<TrackEdge> edges, List<string> issues)
    {
        if (edges.Count == 1)
            return;
        if (edges.Count > 3)
        {
            issues.Add($"{node}: сходится {edges.Count} пути — в v1 узел объединяет не больше трёх");
            return;
        }
        var axis = edges[0].HeadingOutOf(node);
        int same = 0, opposite = 0;
        foreach (var edge in edges)
        {
            var heading = edge.HeadingOutOf(node);
            if (Angles.Difference(heading, axis) <= HeadingToleranceRad)
                same++;
            else if (Angles.Difference(heading, Angles.Reverse(axis)) <= HeadingToleranceRad)
                opposite++;
            else
            {
                issues.Add($"{node}: излом — пути сходятся под углом");
                return;
            }
        }
        if (same == 0 || opposite == 0)
            issues.Add($"{node}: пути выходят из узла в одну сторону — проехать с одного на другой нельзя");
    }

    /// <summary>
    /// Сигналы и стоянки после изменения (docs/10в, разделы 4–5): существующие переносятся на части разделённых путей,
    /// на снесённых участках исчезают; разрезать платформу или депо нельзя. Новые — на прямом участке пути (стоянки),
    /// не на мосту и не в зоне стрелки; сигнал — не больше одного в клетке и не на стоянке.
    /// </summary>
    private (List<RailSignal> LostSignals, List<RailBerth> LostBerths) CheckFixtures(RailEdit edit,
        Dictionary<TrackEdgeId, TrackEdge> edges, Dictionary<TrackNodeId, List<TrackEdge>> incident, List<string> issues)
    {
        var lostSignals = new List<RailSignal>();
        var lostBerths = new List<RailBerth>();
        var signals = new List<RailSignal>();
        var berths = new List<RailBerth>();

        foreach (var signal in _signals.Values)
        {
            if (edit.RemovedSignals.Contains(signal.Id))
            {
                lostSignals.Add(signal);
                continue;
            }
            if (!edit.RemovedEdges.Contains(signal.Edge))
            {
                signals.Add(signal);
                continue;
            }
            var pieces = edit.PiecesOf(signal.Edge);
            var piece = pieces.FirstOrDefault(p => p.Covers(signal.DistanceM));
            if (piece.Edge == default)
            {
                lostSignals.Add(signal);
                continue;
            }
            var local = signal.DistanceM - piece.OffsetM;
            if (local < SignalNodeMarginM || local > piece.LengthM - SignalNodeMarginM)
                issues.Add($"{signal.Id}: путь делится у сигнала — перенесите точку подключения или снимите сигнал");
            signals.Add(signal with { Edge = piece.Edge, DistanceM = local });
        }
        foreach (var berth in _berths.Values)
        {
            if (edit.RemovedBerths.Contains(berth.Id))
            {
                lostBerths.Add(berth);
                continue;
            }
            if (!edit.RemovedEdges.Contains(berth.Edge))
            {
                berths.Add(berth);
                continue;
            }
            var pieces = edit.PiecesOf(berth.Edge);
            if (MapBerth(berth, pieces) is { } moved)
                berths.Add(moved);
            else if (pieces.Any(p => p.OffsetM > berth.FromM + SpatialScale.EpsilonM && p.OffsetM < berth.ToM - SpatialScale.EpsilonM))
                issues.Add(berth.Kind == BerthKind.Platform
                    ? $"{berth.Id}: нельзя разделить платформу — подключайтесь за её пределами"
                    : $"{berth.Id}: нельзя разделить путь депо — подключайтесь за его пределами");
            else
                lostBerths.Add(berth);
        }

        foreach (var signal in edit.NewSignals)
        {
            if (!edges.TryGetValue(signal.Edge, out var edge))
            {
                issues.Add($"{signal.Id}: нет пути {signal.Edge}");
                continue;
            }
            if (signal.DistanceM < SignalNodeMarginM || signal.DistanceM > edge.LengthM - SignalNodeMarginM)
                issues.Add("сигнал ставится внутри пути, не у его конца или стрелки");
            if (edge.Level != edit.Node(edge.From).Level)
                issues.Add("на мосту сигнал не ставится");
            signals.Add(signal);
        }
        foreach (var berth in edit.NewBerths)
        {
            if (!edges.TryGetValue(berth.Edge, out var edge))
            {
                issues.Add($"{berth.Id}: нет пути {berth.Edge}");
                continue;
            }
            if (berth.FromM < -SpatialScale.EpsilonM || berth.ToM > edge.LengthM + SpatialScale.EpsilonM || berth.LengthM <= SpatialScale.EpsilonM)
                issues.Add(berth.Kind == BerthKind.Platform ? "платформа выходит за конец пути" : "депо выходит за конец пути");
            else if (!OnStraight(edge.Geometry, berth.FromM, berth.ToM))
                issues.Add(berth.Kind == BerthKind.Platform ? "платформа ставится только на прямой путь" : "депо ставится только на прямой путь");
            if (edge.Level != edit.Node(edge.From).Level)
                issues.Add(berth.Kind == BerthKind.Platform ? "на мосту платформа не ставится" : "на мосту депо не ставится");
            berths.Add(berth);
        }

        // Правила сочетания — только для того, что меняется: новые сигналы и стоянки и пути, на которые они легли.
        var newSignals = edit.NewSignals.Select(s => s.Id).ToHashSet();
        var newBerths = edit.NewBerths.Select(b => b.Id).ToHashSet();
        var zones = Zones(incident, edges);
        foreach (var signal in signals.Where(s => newSignals.Contains(s.Id)))
        {
            if (!edges.TryGetValue(signal.Edge, out var edge))
                continue;
            var cell = SpatialScale.ToCell(edge.Geometry.PointAt(signal.DistanceM));
            if (signals.Any(o => o.Id != signal.Id && edges.TryGetValue(o.Edge, out var oe) &&
                                 SpatialScale.ToCell(oe.Geometry.PointAt(o.DistanceM)) == cell))
                issues.Add($"в клетке {cell.X}, {cell.Y} уже стоит сигнал — не больше одного на клетку");
            if (zones.Any(z => z.Edge == signal.Edge && signal.DistanceM >= z.FromM - SpatialScale.EpsilonM && signal.DistanceM <= z.ToM + SpatialScale.EpsilonM))
                issues.Add("на стрелке сигнал не ставится — поставьте его до или после");
            if (berths.Any(b => b.Edge == signal.Edge && b.Covers(signal.DistanceM, SpatialScale.EpsilonM)))
                issues.Add("на станции и в депо сигналы встроены");
        }
        foreach (var berth in berths.Where(b => newBerths.Contains(b.Id)))
        {
            if (zones.Any(z => z.Edge == berth.Edge && z.FromM < berth.ToM && z.ToM > berth.FromM))
                issues.Add(berth.Kind == BerthKind.Platform ? "платформа заходит на стрелку" : "депо заходит на стрелку");
            if (berths.Any(o => o.Id != berth.Id && o.Edge == berth.Edge && o.FromM < berth.ToM - SpatialScale.EpsilonM && o.ToM > berth.FromM + SpatialScale.EpsilonM))
                issues.Add("здесь уже есть платформа или депо");
            if (signals.Any(s => s.Edge == berth.Edge && berth.Covers(s.DistanceM, SpatialScale.EpsilonM)))
                issues.Add("на месте стоянки сигнал — станции и депо работают как встроенные сигналы");
        }
        // Новый путь, врезанный стрелкой рядом со стоянкой: её участок не должен попасть в зону стрелки.
        foreach (var berth in berths.Where(b => !newBerths.Contains(b.Id)))
            if (zones.Any(z => z.Edge == berth.Edge && z.FromM < berth.ToM - SpatialScale.EpsilonM && z.ToM > berth.FromM + SpatialScale.EpsilonM) &&
                touchedJunction(berth.Edge))
                issues.Add(berth.Kind == BerthKind.Platform ? "стрелка заходит на платформу" : "стрелка заходит в депо");
        return (lostSignals, lostBerths);

        bool touchedJunction(TrackEdgeId edge) => edit.NewEdges.Any(e => e.Id == edge) ||
            edges.TryGetValue(edge, out var e2) && new[] { e2.From, e2.To }.Any(n => edit.NewEdges.Any(ne => ne.From == n || ne.To == n));
    }

    /// <summary>Лежит ли участок пути целиком на прямых примитивах.</summary>
    public static bool OnStraight(PathGeometry geometry, double fromM, double toM)
    {
        for (var i = 0; i < geometry.Primitives.Count; i++)
        {
            var start = geometry.StartOf(i);
            var end = start + geometry.Primitives[i].Length;
            if (end <= fromM + SpatialScale.EpsilonM || start >= toM - SpatialScale.EpsilonM)
                continue;
            if (geometry.Primitives[i] is not StraightPrimitive)
                return false;
        }
        return true;
    }

    /// <summary>Конфликтные зоны всех стрелок сети после изменения.</summary>
    private List<EdgeInterval> Zones(Dictionary<TrackNodeId, List<TrackEdge>> incident, Dictionary<TrackEdgeId, TrackEdge> edges)
    {
        var zones = new List<EdgeInterval>();
        foreach (var (node, list) in incident)
            if (list.Count >= 3)
                zones.AddRange(ZoneIntervals(node, list));
        return zones;
    }

    private CorridorShape Corridor(PathGeometry geometry) => new(geometry, Settings.HalfWidthM);

    /// <summary>Участок пути <paramref name="edge"/> у узла, где его габарит ещё пересекается с соседом по узлу.</summary>
    private PathInterval ZoneAt(TrackEdge edge, TrackNodeId node, TrackEdge other)
    {
        var (shape, otherShape) = (Corridor(edge.Geometry), Corridor(other.Geometry));
        if (node == edge.From)
            return new PathInterval(0, ShapeConflicts.OverlapLength(shape, 0, +1, otherShape));
        var length = ShapeConflicts.OverlapLength(shape, edge.LengthM, -1, otherShape);
        return new PathInterval(edge.LengthM - length, edge.LengthM);
    }

    private List<EdgeInterval> ZoneIntervals(TrackNodeId node, IReadOnlyList<TrackEdge> edges) =>
        edges.Select(edge =>
        {
            var reach = edges.Where(o => o.Id != edge.Id).Max(o =>
            {
                var interval = ZoneAt(edge, node, o);
                return interval.To - interval.From;
            });
            return node == edge.From
                ? new EdgeInterval(edge.Id, 0, reach)
                : new EdgeInterval(edge.Id, edge.LengthM - reach, edge.LengthM);
        }).ToList();

    private TrackJunction? BuildJunction(TrackNodeId node)
    {
        var edges = EdgesAt(node);
        return edges.Count < 3 ? null : new TrackJunction(node, ZoneIntervals(node, edges));
    }

    private string Describe(SpatialIssue issue)
    {
        var subject = issue.Subject.Value == 0 ? "сеть" : "путь";
        if (issue.Other is not { } other)
            return $"{subject}: {issue.Reason}";
        if (EdgeOf(other) is not null)
            return "пересекается с другим путём — независимые линии на одном уровне не пересекаются, нужен мост";
        var kind = World.Contains(other) ? World.Get(other).Kind.Value : "объект";
        return $"{subject}: {issue.Reason} ({kind})";
    }
}
