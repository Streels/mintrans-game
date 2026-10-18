using Mintrans.Core.Grid;
using Mintrans.Data.Json;
using Mintrans.World;
using Mintrans.World.Networks;

namespace Mintrans.Data.Maps.Networks;

// Операции раздела "networks": готовые участки сетей на старте (например, улицы города).
// Они работают с любой раскладкой сети: как уложить путь в клетке, решает сама сеть.

/// <summary>
/// { "op": "line", "network": "rail", "points": [[x, y], ...] } — ломаная из горизонталей,
/// вертикалей и диагоналей под 45° (лесенкой). Изломы становятся поворотами.
/// </summary>
public sealed class LineOperation : IMapOperation
{
    public string Name => "line";

    public void Apply(JsonNode args, WorldMap map)
    {
        var network = Network(args, map);
        var points = args.Get("points").Items().Select(p => p.AsPos()).ToList();
        if (points.Count < 2)
            throw args.Get("points").Error("нужно хотя бы две точки");
        network.LayPath(NetworkPaths.Polyline(points));
    }

    internal static NetworkLayer Network(JsonNode args, WorldMap map)
    {
        var id = args.String("network");
        return map.Networks.TryGet(id, out var layer)
            ? layer
            : throw args.Get("network").Error(
                $"нет сети «{id}». Доступны: {string.Join(", ", map.Networks.Layers.Select(l => l.Type))}");
    }
}

/// <summary>
/// { "op": "grid", "network": "road", "rect": [x, y, w, h], "spacing": 4 } — квартальная сетка улиц:
/// линии по краям прямоугольника и через каждые spacing клеток.
/// </summary>
public sealed class GridOperation : IMapOperation
{
    public string Name => "grid";

    public void Apply(JsonNode args, WorldMap map)
    {
        var network = LineOperation.Network(args, map);
        var rect = args.Rect("rect");
        var spacing = args.Int("spacing");
        if (spacing <= 0)
            throw args.Get("spacing").Error("шаг сетки должен быть положительным");

        foreach (var x in Lines(rect.X, rect.Right - 1, spacing))
            network.LayPath(NetworkPaths.Polyline([new GridPos(x, rect.Y), new GridPos(x, rect.Bottom - 1)]));
        foreach (var y in Lines(rect.Y, rect.Bottom - 1, spacing))
            network.LayPath(NetworkPaths.Polyline([new GridPos(rect.X, y), new GridPos(rect.Right - 1, y)]));
    }

    private static IEnumerable<int> Lines(int first, int last, int spacing)
    {
        for (var v = first; v < last; v += spacing)
            yield return v;
        yield return last;
    }
}
