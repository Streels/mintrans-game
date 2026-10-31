namespace Mintrans.World.Rail;

/// <summary>Маршрут по рельсам: пути по порядку движения и длина от начального до конечного положения, м.</summary>
public sealed record RailRoute(IReadOnlyList<DirectedEdge> Edges, double LengthM);

/// <summary>
/// Кратчайший маршрут по рельсовому графу (docs/02в, раздел 7): вершины поиска — пути в направлении движения,
/// вес — точная длина в метрах. Разворотов внутри маршрута нет: проехать можно только туда, куда ведёт путь без излома.
/// </summary>
public static class RailRouter
{
    public static RailRoute? Find(RailNetwork network, TrackPosition from, TrackPosition to)
    {
        if (from.Edge == to.Edge && to.DistanceM >= from.DistanceM)
            return new RailRoute([from.Edge], to.DistanceM - from.DistanceM);

        // Расстояние от начального положения до начала каждого пути в направлении движения.
        var best = new Dictionary<DirectedEdge, double>();
        var previous = new Dictionary<DirectedEdge, DirectedEdge>();
        var queue = new PriorityQueue<DirectedEdge, (double, int, bool)>();
        void Relax(DirectedEdge next, DirectedEdge via, double distance)
        {
            if (best.TryGetValue(next, out var old) && old <= distance)
                return;
            best[next] = distance;
            previous[next] = via;
            queue.Enqueue(next, (distance, next.Edge.Value, !next.Forward));
        }

        var startRemaining = network.LengthOf(from.Edge) - from.DistanceM;
        foreach (var next in network.Next(from.Edge))
            Relax(next, from.Edge, startRemaining);
        var settled = new HashSet<DirectedEdge>();
        while (queue.TryDequeue(out var current, out var key))
        {
            if (!settled.Add(current))
                continue;
            if (current == to.Edge)
                return new RailRoute(Unwind(previous, from.Edge, current), key.Item1 + to.DistanceM);
            foreach (var next in network.Next(current))
                Relax(next, current, key.Item1 + network.LengthOf(current));
        }
        return null;
    }

    private static List<DirectedEdge> Unwind(Dictionary<DirectedEdge, DirectedEdge> previous, DirectedEdge start, DirectedEdge end)
    {
        var edges = new List<DirectedEdge> { end };
        var at = end;
        while (previous.TryGetValue(at, out var before))
        {
            edges.Add(before);
            if (before == start)
                break;
            at = before;
        }
        edges.Reverse();
        return edges;
    }
}
