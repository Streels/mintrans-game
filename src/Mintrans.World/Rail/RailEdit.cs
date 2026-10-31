using Mintrans.Core.Geometry;
using Mintrans.Core.Spatial;

namespace Mintrans.World.Rail;

/// <summary>
/// Изменение рельсовой сети одной транзакцией (docs/10в, раздел 7): новые узлы, новые пути, деление пути стрелкой, снос.
/// Само ничего не меняет: сеть проверяет его целиком и применяет всё или ничего.
/// Номера новых узлов и путей известны сразу — по порядку от следующих свободных номеров сети.
/// </summary>
public sealed class RailEdit
{
    private readonly RailNetwork _network;
    private readonly List<TrackNode> _newNodes = [];
    private readonly List<TrackEdge> _newEdges = [];
    private readonly SortedSet<TrackEdgeId> _removed = [];

    internal RailEdit(RailNetwork network)
    {
        _network = network;
        BaseRevision = network.Revision;
    }

    public int BaseRevision { get; }

    public IReadOnlyList<TrackNode> NewNodes => _newNodes;

    /// <summary>Новые пути; геометрия габарита и номер в пространственном мире назначаются при проверке.</summary>
    public IReadOnlyList<TrackEdge> NewEdges => _newEdges;

    public IReadOnlySet<TrackEdgeId> RemovedEdges => _removed;

    public TrackNodeId NewNode(WorldPoint at, int level = 0)
    {
        var id = new TrackNodeId(_network.NextNodeId.Value + _newNodes.Count);
        _newNodes.Add(new TrackNode(id, at, level));
        return id;
    }

    public TrackEdgeId AddEdge(TrackNodeId from, TrackNodeId to, PathGeometry geometry)
    {
        var id = new TrackEdgeId(_network.NextEdgeId.Value + _newEdges.Count);
        var level = NodeLevel(from);
        _newEdges.Add(new TrackEdge(id, from, to, geometry, level, default));
        return id;
    }

    public void RemoveEdge(TrackEdgeId edge)
    {
        if (!_network.ContainsEdge(edge))
            throw new ArgumentException($"В сети нет пути {edge}.", nameof(edge));
        _removed.Add(edge);
    }

    /// <summary>
    /// Разделить построенный путь в точке <paramref name="distanceM"/> от его начала: путь заменяется двумя с общим новым узлом.
    /// Прямой ход сохраняется — геометрия не меняется, только делится.
    /// </summary>
    public TrackNodeId SplitEdge(TrackEdgeId edge, double distanceM)
    {
        var existing = _network.Edge(edge);
        var (before, after) = existing.Geometry.Split(distanceM);
        RemoveEdge(edge);
        var node = NewNode(before.End, existing.Level);
        AddEdge(existing.From, node, before);
        AddEdge(node, existing.To, after);
        return node;
    }

    internal TrackNode Node(TrackNodeId id) =>
        _newNodes.FirstOrDefault(n => n.Id == id) ?? _network.Node(id);

    private int NodeLevel(TrackNodeId id) => Node(id).Level;
}
