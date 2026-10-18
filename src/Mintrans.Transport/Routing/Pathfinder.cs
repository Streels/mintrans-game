using Mintrans.Core.Grid;
using Mintrans.World.Networks;

namespace Mintrans.Transport.Routing;

/// <summary>
/// Кратчайший путь по сети (docs/12). Учитывает устройство клетки: по рельсам можно свернуть
/// только по уложенному куску. В начальной клетке техника может ехать в любую сторону
/// (разворот на станции и в депо).
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// Путь от <paramref name="start"/> до одной из клеток <paramref name="targets"/> — список клеток от старта до цели.
    /// <paramref name="farthest"/>: выбрать самую дальнюю из достижимых целей (поезд проезжает станцию до конца).
    /// <paramref name="passable"/>: можно ли заезжать в клетку (например, нельзя сквозь чужое депо).
    /// Null, если пути нет.
    /// </summary>
    public static List<GridPos>? Find(
        NetworkLayer network,
        GridPos start,
        IReadOnlySet<GridPos> targets,
        Func<GridPos, bool>? passable = null,
        bool farthest = false)
    {
        if (!network.Contains(start) || !network.HasAny(start) || targets.Count == 0)
            return null;
        if (targets.Contains(start) && !farthest)
            return [start];

        // Состояние обхода — клетка и сторона, через которую в неё въехали (null — стартовая клетка).
        var parent = new Dictionary<(GridPos Cell, Direction? Entry), (GridPos Cell, Direction? Entry)?>();
        var distance = new Dictionary<(GridPos, Direction?), int>();
        var queue = new Queue<(GridPos Cell, Direction? Entry)>();
        var startState = (start, (Direction?)null);
        parent[startState] = null;
        distance[startState] = 0;
        queue.Enqueue(startState);

        // Для «самой дальней цели» сравниваются кратчайшие расстояния до клеток цели (первое посещение клетки).
        var firstReach = new Dictionary<GridPos, (GridPos Cell, Direction? Entry)>();

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            var (cell, entry) = state;
            if (targets.Contains(cell) && state != startState)
            {
                if (!farthest)
                    return Build(parent, state);
                firstReach.TryAdd(cell, state);
            }

            var forward = 0;
            foreach (var exit in network.EdgesAt(cell).Enumerate())
            {
                if (entry is { } e && (exit == e || !network.Connects(cell, e, exit)))
                    continue;
                var next = cell.Step(exit);
                var nextEntry = exit.Opposite();
                if (!network.Contains(next) || !network.EdgesAt(next).Has(nextEntry))
                    continue;
                if (passable is not null && !passable(next))
                    continue;
                forward++;
                Visit(state, (next, nextEntry), distance[state] + 1);
            }

            // Тупик: дальше ехать некуда — разворот на месте (как поезд в конце пути в OpenTTD).
            if (forward == 0 && entry is not null)
                Visit(state, (cell, null), distance[state] + 1);
        }

        if (firstReach.Count == 0)
            return targets.Contains(start) ? [start] : null;
        var best = firstReach.Values.MaxBy(s => distance[s]);
        return Build(parent, best);

        void Visit((GridPos, Direction?) from, (GridPos, Direction?) to, int d)
        {
            if (parent.ContainsKey(to))
                return;
            parent[to] = from;
            distance[to] = d;
            queue.Enqueue(to);
        }
    }

    private static List<GridPos> Build(
        Dictionary<(GridPos Cell, Direction? Entry), (GridPos Cell, Direction? Entry)?> parent,
        (GridPos Cell, Direction? Entry) end)
    {
        var path = new List<GridPos>();
        for (var state = ((GridPos, Direction?)?)end; state is { } s; state = parent[s])
        {
            // Разворот в тупике — та же клетка дважды подряд; в пути она одна.
            if (path.Count == 0 || path[^1] != s.Item1)
                path.Add(s.Item1);
        }
        path.Reverse();
        return path;
    }
}
