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
/// { "op": "bridge", "network": "rail", "kind": "rail_bridge", "from": [x, y], "to": [x, y] } — мост между двумя
/// опорами на одной прямой (docs/10б). Путь к опорам подводится отдельными линиями с тыльной стороны.
/// </summary>
public sealed class BridgeOperation : IMapOperation
{
    public string Name => "bridge";

    public void Apply(JsonNode args, WorldMap map) =>
        LineOperation.Network(args, map).AddBridge(args.String("kind"), args.Pos("from"), args.Pos("to"));
}

/// <summary>
/// { "op": "signal", "network": "rail", "kind": "block_signal", "logic": "block", "at": [x, y], "toward": "east" } —
/// сигнал на единственном куске пути в клетке (docs/10а). Без "toward" — двусторонний.
/// </summary>
public sealed class SignalOperation : IMapOperation
{
    public string Name => "signal";

    public void Apply(JsonNode args, WorldMap map)
    {
        if (LineOperation.Network(args, map) is not TrackNetworkLayer rails)
            throw args.Get("network").Error("сигналы ставятся только на рельсовую сеть");
        var at = args.Pos("at");
        var pieces = rails.PiecesAt(at);
        var logic = args.String("logic") switch
        {
            "block" => SignalLogic.Block,
            "path" => SignalLogic.Path,
            var other => throw args.Get("logic").Error($"неизвестная логика «{other}»: нужно block или path"),
        };
        Direction? toward = args.Find("toward") is { } side
            ? side.AsString() switch
            {
                "north" => Direction.North,
                "east" => Direction.East,
                "south" => Direction.South,
                "west" => Direction.West,
                var other => throw side.Error($"неизвестная сторона «{other}»: north, east, south или west"),
            }
            : null;
        var piece = toward is { } t ? pieces.Enumerate().FirstOrDefault(p => p.Touches(t)) : pieces;
        if (piece.Count() != 1 || pieces.IsJunction())
            throw args.Get("at").Error($"в клетке {at} нужен один кусок пути без стрелки");
        var (a, b) = piece.Sides();
        rails.SetSignal(new Signal(at, piece, args.String("kind"), logic, toward is { } f ? f.ToMask() : a.ToMask() | b.ToMask()));
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
