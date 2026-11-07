using Mintrans.Core.Geometry;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;
using Mintrans.World.Facilities;

namespace Mintrans.World.Rail;

/// <summary>
/// Часть прежнего пути после деления: новый путь <paramref name="Edge"/> повторяет участок старого
/// с <paramref name="OffsetM"/> метров от его начала, в том же направлении.
/// </summary>
public readonly record struct EdgePiece(TrackEdgeId Edge, double OffsetM, double LengthM)
{
    public bool Covers(double distanceM) =>
        distanceM >= OffsetM - SpatialScale.EpsilonM && distanceM <= OffsetM + LengthM + SpatialScale.EpsilonM;
}

/// <summary>
/// Изменение рельсовой сети одной транзакцией (docs/10в, раздел 7): новые узлы и пути, деление пути стрелкой, снос,
/// сигналы и стоянки (платформы, депо). Само ничего не меняет: сеть проверяет его целиком и применяет всё или ничего.
/// Номера новых узлов, путей, сигналов и стоянок известны сразу — по порядку от следующих свободных номеров сети.
/// </summary>
public sealed class RailEdit
{
    private readonly RailNetwork _network;
    private readonly List<TrackNode> _newNodes = [];
    private readonly List<TrackEdge> _newEdges = [];
    private readonly SortedSet<TrackEdgeId> _removed = [];
    private readonly Dictionary<TrackEdgeId, (TrackEdgeId Root, double OffsetM)> _origins = new();
    private readonly List<RailSignal> _newSignals = [];
    private readonly SortedSet<RailSignalId> _removedSignals = [];
    private readonly List<RailBerth> _newBerths = [];
    private readonly SortedSet<RailBerthId> _removedBerths = [];
    private int _edgeCounter;

    internal RailEdit(RailNetwork network)
    {
        _network = network;
        BaseRevision = network.Revision;
    }

    public int BaseRevision { get; }

    public IReadOnlyList<TrackNode> NewNodes => _newNodes;

    /// <summary>Новые пути, которые останутся после изменения; номер габарита назначается при проверке.</summary>
    public IReadOnlyList<TrackEdge> NewEdges => _newEdges;

    /// <summary>Построенные пути, которые изменение убирает (в том числе разделённые на части).</summary>
    public IReadOnlySet<TrackEdgeId> RemovedEdges => _removed;

    public IReadOnlyList<RailSignal> NewSignals => _newSignals;
    public IReadOnlySet<RailSignalId> RemovedSignals => _removedSignals;
    public IReadOnlyList<RailBerth> NewBerths => _newBerths;
    public IReadOnlySet<RailBerthId> RemovedBerths => _removedBerths;

    public bool IsEmpty => _newNodes.Count == 0 && _newEdges.Count == 0 && _removed.Count == 0 &&
                           _newSignals.Count == 0 && _removedSignals.Count == 0 && _newBerths.Count == 0 && _removedBerths.Count == 0;

    /// <summary>Новый путь — часть уже построенного (после деления): его геометрию заново не проверяют.</summary>
    public bool IsPiece(TrackEdgeId edge) => _origins.ContainsKey(edge);

    public TrackNodeId NewNode(WorldPoint at, int level = 0)
    {
        var id = new TrackNodeId(_network.NextNodeId.Value + _newNodes.Count);
        _newNodes.Add(new TrackNode(id, at, level));
        return id;
    }

    /// <summary>
    /// Новый путь между узлами. Уровень габарита по умолчанию — уровень узлов; мост — путь уровня 1 между узлами
    /// на земле: опоры стоят на земле, пролёт проходит над тем, что под ним.
    /// </summary>
    public TrackEdgeId AddEdge(TrackNodeId from, TrackNodeId to, PathGeometry geometry, int? level = null)
    {
        var id = new TrackEdgeId(_network.NextEdgeId.Value + _edgeCounter++);
        _newEdges.Add(new TrackEdge(id, from, to, geometry, level ?? NodeLevel(from), default));
        return id;
    }

    public void RemoveEdge(TrackEdgeId edge)
    {
        var pending = _newEdges.FindIndex(e => e.Id == edge);
        if (pending >= 0)
        {
            _newEdges.RemoveAt(pending);
            _origins.Remove(edge);
            return;
        }
        if (!_network.ContainsEdge(edge))
            throw new ArgumentException($"В сети нет пути {edge}.", nameof(edge));
        _removed.Add(edge);
    }

    /// <summary>
    /// Разделить путь (построенный или новый в этом же изменении) в точке <paramref name="distanceM"/> от его начала:
    /// путь заменяется двумя с общим новым узлом. Прямой ход сохраняется — геометрия не меняется, только делится.
    /// </summary>
    public TrackNodeId SplitEdge(TrackEdgeId edge, double distanceM) => Split(edge, distanceM).Node;

    /// <summary>То же, что <see cref="SplitEdge"/>, с номерами частей до и после точки деления.</summary>
    public (TrackNodeId Node, TrackEdgeId Before, TrackEdgeId After) Split(TrackEdgeId edge, double distanceM)
    {
        var existing = Edge(edge);
        var (before, after) = existing.Geometry.Split(distanceM);
        var (root, offset) = _origins.TryGetValue(edge, out var origin) ? origin
            : _newEdges.Any(e => e.Id == edge) ? (default, double.NaN)
            : (edge, 0.0);
        RemoveEdge(edge);
        _origins.Remove(edge);
        var node = NewNode(before.End, Node(existing.From).Level);
        var first = AddEdge(existing.From, node, before, existing.Level);
        var second = AddEdge(node, existing.To, after, existing.Level);
        if (!double.IsNaN(offset))
        {
            _origins[first] = (root, offset);
            _origins[second] = (root, offset + before.Length);
        }
        return (node, first, second);
    }

    /// <summary>Путь сети или новый путь этого изменения.</summary>
    public TrackEdge Edge(TrackEdgeId id) =>
        _newEdges.FirstOrDefault(e => e.Id == id) ?? _network.Edge(id);

    /// <summary>Части построенного пути <paramref name="old"/>, которые останутся после изменения.</summary>
    public IReadOnlyList<EdgePiece> PiecesOf(TrackEdgeId old) =>
        _origins.Where(p => p.Value.Root == old)
            .Select(p => new EdgePiece(p.Key, p.Value.OffsetM, Edge(p.Key).LengthM))
            .OrderBy(p => p.OffsetM)
            .ToList();

    public RailSignalId AddSignal(TrackEdgeId edge, double distanceM, TypeId kind, SignalLogic logic, SignalFacing facing)
    {
        var id = new RailSignalId(_network.NextSignalId.Value + _newSignals.Count);
        _newSignals.Add(new RailSignal(id, edge, distanceM, kind, logic, facing));
        return id;
    }

    public void RemoveSignal(RailSignalId signal) => _removedSignals.Add(signal);

    public RailBerthId AddBerth(BerthKind kind, TrackEdgeId edge, double fromM, double toM, SiteId site, FacilityId? facility)
    {
        var id = new RailBerthId(_network.NextBerthId.Value + _newBerths.Count);
        _newBerths.Add(new RailBerth(id, kind, edge, Math.Min(fromM, toM), Math.Max(fromM, toM), site, facility));
        return id;
    }

    public void RemoveBerth(RailBerthId berth) => _removedBerths.Add(berth);

    /// <summary>Узел сети или новый узел этого изменения.</summary>
    public TrackNode Node(TrackNodeId id) => _newNodes.FirstOrDefault(n => n.Id == id) ?? _network.Node(id);

    /// <summary>Сколько номеров путей выдано этим изменением (часть могла уйти при делении).</summary>
    internal int EdgeIdsUsed => _edgeCounter;

    private int NodeLevel(TrackNodeId id) => Node(id).Level;
}
