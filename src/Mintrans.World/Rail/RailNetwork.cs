using Mintrans.Core.Geometry;
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

    public bool IsValid => Issues.Count == 0;

    public bool Committed { get; internal set; }
}

/// <summary>
/// Рельсовый граф (docs/02в, раздел 7; docs/10в): узлы, пути с точной метрической геометрией и длиной, стрелки
/// с конфликтными зонами. Клетки не вершины графа: соседство клеток ничего не соединяет, соединяют только общие узлы.
/// Переход в узле разрешён только без излома — по путям, продолжающим направление движения. Каждый путь — габарит
/// в общем пространственном мире, где и проверяются пересечения.
/// </summary>
public sealed class RailNetwork
{
    /// <summary>Допуск совпадения направлений путей в узле, рад.</summary>
    public const double HeadingToleranceRad = 1e-6;

    private readonly SortedDictionary<TrackNodeId, TrackNode> _nodes = new();
    private readonly SortedDictionary<TrackEdgeId, TrackEdge> _edges = new();
    private readonly Dictionary<TrackNodeId, List<TrackEdgeId>> _incident = new();
    private readonly SortedDictionary<TrackNodeId, TrackJunction> _junctions = new();

    public RailNetwork(SpatialWorld world, RailNetworkSettings settings)
    {
        World = world;
        Settings = settings;
    }

    public SpatialWorld World { get; }
    public RailNetworkSettings Settings { get; }

    /// <summary>Растёт с каждым применённым изменением: по ней транспорт узнаёт, что граф изменился.</summary>
    public int Revision { get; private set; }

    public TrackNodeId NextNodeId { get; private set; } = new(1);
    public TrackEdgeId NextEdgeId { get; private set; } = new(1);

    public IEnumerable<TrackNode> Nodes => _nodes.Values;
    public IEnumerable<TrackEdge> Edges => _edges.Values;
    public IEnumerable<TrackJunction> Junctions => _junctions.Values;

    public bool ContainsEdge(TrackEdgeId id) => _edges.ContainsKey(id);

    public TrackEdge Edge(TrackEdgeId id) =>
        _edges.TryGetValue(id, out var edge) ? edge : throw new KeyNotFoundException($"В сети нет пути {id}.");

    public TrackNode Node(TrackNodeId id) =>
        _nodes.TryGetValue(id, out var node) ? node : throw new KeyNotFoundException($"В сети нет узла {id}.");

    public IReadOnlyList<TrackEdge> EdgesAt(TrackNodeId node) =>
        _incident.TryGetValue(node, out var ids) ? ids.Select(Edge).ToList() : [];

    public TrackJunction? JunctionAt(TrackNodeId node) => _junctions.GetValueOrDefault(node);

    /// <summary>Путь, которому принадлежит габарит <paramref name="spatial"/>, если это рельсы этой сети.</summary>
    public TrackEdge? EdgeOf(ObjectId spatial) => _edges.Values.FirstOrDefault(e => e.Spatial == spatial);

    /// <summary>Свободные концы — тупики, к которым можно подключить новое соединение.</summary>
    public IEnumerable<TrackEnd> FreeEnds => _nodes.Values
        .Where(n => EdgesAt(n.Id).Count == 1)
        .Select(n => new TrackEnd(n.Id, n.At, Angles.Reverse(EdgesAt(n.Id)[0].HeadingOutOf(n.Id)), n.Level));

    public RailEdit Begin() => new(this);

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

    public double LengthOf(DirectedEdge d) => Edge(d.Edge).LengthM;

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

        var affected = new SortedSet<TrackNodeId>();
        foreach (var id in edit.RemovedEdges)
        {
            var edge = _edges[id];
            _edges.Remove(id);
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
        NextNodeId = new TrackNodeId(NextNodeId.Value + edit.NewNodes.Count);
        NextEdgeId = new TrackEdgeId(NextEdgeId.Value + edit.NewEdges.Count);
        Revision++;
        return new RailEditPreview(preview.Issues, spatial, preview.Edges) { Committed = true };
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

        foreach (var edge in edit.NewEdges)
            CheckEdge(edit, edge, issues);
        var touchedNodes = edit.NewEdges.SelectMany(e => new[] { e.From, e.To })
            .Concat(edit.RemovedEdges.SelectMany(id => new[] { _edges[id].From, _edges[id].To }))
            .ToHashSet();
        foreach (var node in touchedNodes.Order())
            if (incident.TryGetValue(node, out var list))
                CheckNode(node, list, issues);
        if (issues.Count > 0)
            return new RailEditPreview(issues, null, edit.NewEdges);

        // Габариты: старые пути уходят, новые встают; у общих узлов пересечение разрешено в пределах зоны стыка.
        change = World.Begin();
        foreach (var id in edit.RemovedEdges)
            change.Remove(_edges[id].Spatial);
        var placed = new Dictionary<TrackEdgeId, TrackEdge>();
        foreach (var edge in edit.NewEdges)
            placed[edge.Id] = edge with { Spatial = change.Add(Settings.Kind, edge.Level, Corridor(edge.Geometry)) };
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
        return new RailEditPreview(issues, spatial, placed.Values.ToList());
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

    private TrackJunction? BuildJunction(TrackNodeId node)
    {
        var edges = EdgesAt(node);
        if (edges.Count < 3)
            return null;
        var zone = edges.Select(edge =>
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
        return new TrackJunction(node, zone);
    }

    private string Describe(SpatialIssue issue)
    {
        var subject = EdgeOfNew(issue.Subject);
        return issue.Other is { } other && EdgeOf(other) is { } edge
            ? $"{subject}: {issue.Reason} — {edge.Id}"
            : $"{subject}: {issue.Reason}";
    }

    private string EdgeOfNew(ObjectId id) => id.Value == 0 ? "сеть" : $"габарит {id}";
}
