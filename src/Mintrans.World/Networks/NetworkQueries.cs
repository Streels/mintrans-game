using Mintrans.Core.Grid;

namespace Mintrans.World.Networks;

/// <summary>
/// Запросы связности по слою сети. Обход учитывает, как сеть устроена внутри клетки:
/// по пересечению рельсов нельзя свернуть, если поворот не уложен.
/// Поиск пути с весами появится в модуле транспорта.
/// </summary>
public static class NetworkQueries
{
    /// <summary>Все клетки, достижимые по сети из <paramref name="start"/>, включая её саму.</summary>
    public static HashSet<GridPos> ReachableFrom(NetworkLayer network, GridPos start)
    {
        var cells = new HashSet<GridPos>();
        if (!network.HasAny(start))
            return cells;
        cells.Add(start);

        // Состояние обхода — клетка и сторона, через которую из неё выезжаем.
        var visited = new HashSet<(GridPos, Direction)>();
        var queue = new Queue<(GridPos Cell, Direction Exit)>();
        foreach (var side in network.EdgesAt(start).Enumerate())
        {
            visited.Add((start, side));
            queue.Enqueue((start, side));
        }

        while (queue.Count > 0)
        {
            var (cell, exit) = queue.Dequeue();
            var next = cell.Step(exit);
            var entry = exit.Opposite();
            if (!network.Contains(next) || !network.EdgesAt(next).Has(entry))
                continue;
            cells.Add(next);
            foreach (var side in network.EdgesAt(next).Enumerate())
            {
                if (network.Connects(next, entry, side) && visited.Add((next, side)))
                    queue.Enqueue((next, side));
            }
        }
        return cells;
    }

    /// <summary>Есть ли путь по сети между любыми клетками двух множеств.</summary>
    public static bool AreConnected(NetworkLayer network, IEnumerable<GridPos> from, IEnumerable<GridPos> to)
    {
        var targets = to.ToHashSet();
        foreach (var start in from)
        {
            if (network.HasAny(start) && ReachableFrom(network, start).Overlaps(targets))
                return true;
        }
        return false;
    }
}
