using Mintrans.Core.Grid;

namespace Mintrans.World.Networks;

/// <summary>
/// Запросы связности по слою сети. Обход учитывает, как сеть устроена внутри клетки
/// (по рельсам нельзя свернуть, если поворот не уложен) и мосты: пролёт соединяет только свои опоры.
/// Поиск пути с весами — в модуле транспорта.
/// </summary>
public static class NetworkQueries
{
    /// <summary>Все клетки на земле, достижимые по сети из <paramref name="start"/>, включая её саму. Клетки пролётов не входят.</summary>
    public static HashSet<GridPos> ReachableFrom(NetworkLayer network, GridPos start) => ReachableFrom(network, [start]);

    /// <summary>Все клетки на земле, достижимые по сети хотя бы из одной стартовой клетки — одним обходом.</summary>
    public static HashSet<GridPos> ReachableFrom(NetworkLayer network, IEnumerable<GridPos> starts)
    {
        var cells = new HashSet<GridPos>();
        // Состояние обхода — клетка, на мосту ли она и сторона, через которую из неё выезжаем.
        var visited = new HashSet<(GridPos, bool, Direction)>();
        var queue = new Queue<(GridPos Cell, bool Elevated, Direction Exit)>();
        foreach (var start in starts)
        {
            if (!network.Contains(start) || !network.HasAny(start) || !cells.Add(start))
                continue;
            foreach (var side in network.ExitsFrom(start, null))
            {
                if (visited.Add((start, false, side)))
                    queue.Enqueue((start, false, side));
            }
        }

        while (queue.Count > 0)
        {
            var (cell, elevated, exit) = queue.Dequeue();
            if (network.Follow(cell, exit, elevated) is not { } link)
                continue;
            if (!link.Elevated)
                cells.Add(link.Cell);
            foreach (var side in network.ExitsFrom(link.Cell, link.Entry, link.Elevated))
            {
                if (visited.Add((link.Cell, link.Elevated, side)))
                    queue.Enqueue((link.Cell, link.Elevated, side));
            }
        }
        return cells;
    }

    /// <summary>Есть ли путь по сети между любыми клетками двух множеств.</summary>
    public static bool AreConnected(NetworkLayer network, IEnumerable<GridPos> from, IEnumerable<GridPos> to) =>
        ReachableFrom(network, from).Overlaps(to);
}
