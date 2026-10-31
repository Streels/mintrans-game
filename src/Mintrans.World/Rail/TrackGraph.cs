using Mintrans.Core.Geometry;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;

namespace Mintrans.World.Rail;

public readonly record struct TrackNodeId(int Value) : IComparable<TrackNodeId>
{
    public int CompareTo(TrackNodeId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"узел {Value}";
}

public readonly record struct TrackEdgeId(int Value) : IComparable<TrackEdgeId>
{
    public int CompareTo(TrackEdgeId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"путь {Value}";
}

/// <summary>
/// Узел рельсового графа (docs/10в): точка, где сходятся концы путей. Один путь — тупик, два — стык,
/// три — стрелка. Узел не привязан к центру клетки.
/// </summary>
public sealed record TrackNode(TrackNodeId Id, WorldPoint At, int Level);

/// <summary>
/// Рельсовый путь между двумя узлами: метрическая ось из прямых и дуг, её точная длина и уровень.
/// <paramref name="Spatial"/> — номер габарита пути в пространственном мире.
/// </summary>
public sealed record TrackEdge(TrackEdgeId Id, TrackNodeId From, TrackNodeId To, PathGeometry Geometry, int Level, ObjectId Spatial)
{
    public double LengthM => Geometry.Length;

    /// <summary>Направление «наружу из узла» вдоль пути: в каком направлении поезд покидает узел по этому пути.</summary>
    public double HeadingOutOf(TrackNodeId node) =>
        node == From ? Geometry.StartHeading :
        node == To ? Angles.Reverse(Geometry.EndHeading) :
        throw new ArgumentException($"{Id} не выходит к {node}.");

    public TrackNodeId Other(TrackNodeId node) => node == From ? To : From;
}

/// <summary>Путь в направлении движения: <paramref name="Forward"/> — от <see cref="TrackEdge.From"/> к <see cref="TrackEdge.To"/>.</summary>
public readonly record struct DirectedEdge(TrackEdgeId Edge, bool Forward)
{
    public DirectedEdge Reverse() => this with { Forward = !Forward };

    public override string ToString() => Forward ? $"{Edge} →" : $"{Edge} ←";
}

/// <summary>Положение на пути: сколько метров пройдено по нему в направлении движения.</summary>
public readonly record struct TrackPosition(DirectedEdge Edge, double DistanceM);

/// <summary>Участок пути по расстоянию от его начала (<see cref="TrackEdge.From"/>), м.</summary>
public readonly record struct EdgeInterval(TrackEdgeId Edge, double FromM, double ToM);

/// <summary>
/// Стрелка (docs/10в, раздел 3): узел, где путь расходится на два. Конфликтная зона — участки путей у узла,
/// где их габариты ещё пересекаются: два поезда не могут одновременно занимать конфликтующие маршруты через неё.
/// </summary>
public sealed record TrackJunction(TrackNodeId Node, IReadOnlyList<EdgeInterval> ConflictZone);

/// <summary>
/// Свободный конец пути — точка подключения нового соединения. <paramref name="Heading"/> — направление
/// продолжения пути наружу из тупика.
/// </summary>
public sealed record TrackEnd(TrackNodeId Node, WorldPoint At, double Heading, int Level);
