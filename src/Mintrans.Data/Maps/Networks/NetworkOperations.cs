using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.Data.Json;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Rail;

namespace Mintrans.Data.Maps.Networks;

// Операции раздела "networks": готовые участки сетей на старте (например, улицы города).
// Клеточные сети (дороги) прокладываются по клеткам, рельсы — путями графа на метрической геометрии (docs/10в).

/// <summary>
/// { "op": "line", "network": "road", "points": [[x, y], ...] } — ломаная клеточной сети из горизонталей,
/// вертикалей и диагоналей под 45° (лесенкой). Изломы становятся поворотами. Рельсы — операцией "rail".
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
        if (map.Rails?.Settings.Kind == id)
            throw args.Get("network").Error($"«{id}» — рельсовый граф: рельсы кладутся операцией \"rail\"");
        return map.Networks.TryGet(id, out var layer)
            ? layer
            : throw args.Get("network").Error(
                $"нет сети «{id}». Доступны: {string.Join(", ", map.Networks.Layers.Select(l => l.Type))}");
    }
}

/// <summary>
/// { "op": "bridge", "network": "road", "kind": "road_bridge", "from": [x, y], "to": [x, y] } — мост клеточной сети между
/// двумя опорами на одной прямой (docs/10б). Путь к опорам подводится отдельными линиями с тыльной стороны.
/// </summary>
public sealed class BridgeOperation : IMapOperation
{
    public string Name => "bridge";

    public void Apply(JsonNode args, WorldMap map) =>
        LineOperation.Network(args, map).AddBridge(args.String("kind"), args.Pos("from"), args.Pos("to"));
}

/// <summary>Общее для рельсовых операций карты.</summary>
internal static class RailMapSteps
{
    public static RailNetwork Rails(JsonNode args, WorldMap map) =>
        map.Rails ?? throw args.Error("на карте нет рельсовой сети (в world/networks.json нет сети с раскладкой «graph»)");

    public static WorldPoint Center(GridPos cell) => SpatialScale.CellCenter(cell);

    /// <summary>
    /// Узел для конца нового пути в точке <paramref name="at"/>: продолжение свободного конца, стрелка на прямом пути
    /// или новый узел. <paramref name="outward"/> — направление нового пути наружу из этой точки.
    /// </summary>
    public static TrackNodeId NodeAt(RailNetwork rails, RailEdit edit, WorldPoint at, double outward, int level = 0)
    {
        // Новый путь продолжает свободный конец: выходит из узла туда же, куда смотрит конец.
        var end = rails.FreeEnds.FirstOrDefault(e => e.At.ApproxEquals(at, 0.01) &&
                                                     Angles.Difference(e.Heading, outward) <= 1e-6);
        if (end is not null)
            return end.Node;
        if (rails.Nearest(at, 0.01, includeBridges: false) is { } near &&
            near.DistanceM > SpatialScale.EpsilonM && near.DistanceM < near.Edge.LengthM - SpatialScale.EpsilonM)
            return edit.SplitEdge(near.Edge.Id, near.DistanceM);
        return edit.NewNode(at, level);
    }

    public static void Commit(JsonNode args, RailNetwork rails, RailEdit edit)
    {
        var result = rails.Commit(edit);
        if (!result.Committed)
            throw args.Error(string.Join("; ", result.Issues));
    }
}

/// <summary>
/// { "op": "rail", "points": [[x, y], ...], "radius": 300 } — путь через центры клеток со скруглёнными дугами углами
/// (docs/10в). Концы пристыковываются к свободным концам путей, а в середине прямого пути встают стрелкой.
/// Радиус по умолчанию — наименьший допустимый.
/// </summary>
public sealed class RailOperation : IMapOperation
{
    public string Name => "rail";

    public void Apply(JsonNode args, WorldMap map)
    {
        var rails = RailMapSteps.Rails(args, map);
        var points = args.Get("points").Items().Select(p => RailMapSteps.Center(p.AsPos())).ToList();
        if (points.Count < 2)
            throw args.Get("points").Error("нужно хотя бы две точки");
        var radius = args.Find("radius")?.AsDouble() ?? rails.Settings.MinRadiusM;
        var geometry = Fillet.Polyline(points, radius);
        var edit = rails.Begin();
        var from = RailMapSteps.NodeAt(rails, edit, geometry.Start, geometry.StartHeading);
        var to = RailMapSteps.NodeAt(rails, edit, geometry.End, Angles.Reverse(geometry.EndHeading));
        edit.AddEdge(from, to, geometry);
        RailMapSteps.Commit(args, rails, edit);
    }
}

/// <summary>
/// { "op": "rail_connect", "from": [x, y], "to": [x, y] } — соединение инструментом «А→Б» (docs/10в, раздел 2) с выбором
/// по умолчанию: клетка у свободного конца пути — этот конец, на прямом пути — стрелка, иначе новая точка в центре клетки.
/// Так в карте строятся ответвления и съезды.
/// </summary>
public sealed class RailConnectOperation : IMapOperation
{
    public string Name => "rail_connect";

    public void Apply(JsonNode args, WorldMap map)
    {
        var rails = RailMapSteps.Rails(args, map);
        var settings = Construction.Rail.RailEditorSettings.Default with { MinRadiusM = rails.Settings.MinRadiusM };
        var tool = new Construction.Rail.RailConnectTool(rails, settings);
        var connection = tool.Preview(Anchor(rails, args.Pos("from")), Anchor(rails, args.Pos("to")));
        if (!connection.CanConfirm)
            throw args.Error("соединить нельзя: " + string.Join("; ", connection.Rejections.Take(3)));
        var result = connection.Confirm();
        if (!result.Committed)
            throw args.Error(string.Join("; ", result.Issues));
    }

    private static Construction.Rail.RailAnchor Anchor(RailNetwork rails, GridPos cell)
    {
        var center = RailMapSteps.Center(cell);
        if (rails.NearestFreeEnd(center, SpatialScale.CellM) is { } end)
            return new Construction.Rail.FreeEndAnchor(end.Node);
        if (rails.Nearest(center, SpatialScale.CellM / 2, includeBridges: false) is { } near)
            return new Construction.Rail.EdgePointAnchor(near.Edge.Id, near.DistanceM);
        return new Construction.Rail.NewPointAnchor(center);
    }
}

/// <summary>
/// { "op": "rail_bridge", "from": [x, y], "to": [x, y] } — прямой мост рельсов от опоры до опоры (docs/10б):
/// путь уровня над землёй. Концы пристыковываются к свободным концам путей.
/// </summary>
public sealed class RailBridgeOperation : IMapOperation
{
    public string Name => "rail_bridge";

    public void Apply(JsonNode args, WorldMap map)
    {
        var rails = RailMapSteps.Rails(args, map);
        var (a, b) = (RailMapSteps.Center(args.Pos("from")), RailMapSteps.Center(args.Pos("to")));
        var geometry = new PathGeometry(new StraightPrimitive(a, b));
        var edit = rails.Begin();
        var from = RailMapSteps.NodeAt(rails, edit, a, geometry.StartHeading);
        var to = RailMapSteps.NodeAt(rails, edit, b, Angles.Reverse(geometry.EndHeading));
        edit.AddEdge(from, to, geometry, level: 1);
        RailMapSteps.Commit(args, rails, edit);
    }
}

/// <summary>
/// { "op": "rail_signal", "kind": "block_signal", "logic": "block", "at": [x, y], "toward": "east" } — сигнал на пути
/// напротив центра клетки (docs/10а). "toward" — односторонний: регулирует поезда, идущие в эту сторону; без него — двусторонний.
/// </summary>
public sealed class RailSignalOperation : IMapOperation
{
    public string Name => "rail_signal";

    public void Apply(JsonNode args, WorldMap map)
    {
        var rails = RailMapSteps.Rails(args, map);
        var center = RailMapSteps.Center(args.Pos("at"));
        if (rails.Nearest(center, SpatialScale.CellM / 2) is not { } near)
            throw args.Get("at").Error($"в клетке {args.Pos("at")} нет пути");
        var logic = args.String("logic") switch
        {
            "block" => SignalLogic.Block,
            "path" => SignalLogic.Path,
            var other => throw args.Get("logic").Error($"неизвестная логика «{other}»: нужно block или path"),
        };
        var facing = SignalFacing.Both;
        if (args.Find("toward") is { } side)
        {
            var direction = side.AsString() switch
            {
                "north" => new WorldPoint(0, -1),
                "east" => new WorldPoint(1, 0),
                "south" => new WorldPoint(0, 1),
                "west" => new WorldPoint(-1, 0),
                var other => throw side.Error($"неизвестная сторона «{other}»: north, east, south или west"),
            };
            facing = direction.Dot(WorldPoint.FromAngle(near.Edge.Geometry.HeadingAt(near.DistanceM))) >= 0
                ? SignalFacing.Forward
                : SignalFacing.Backward;
        }
        var edit = rails.Begin();
        edit.AddSignal(near.Edge.Id, near.DistanceM, args.String("kind"), logic, facing);
        RailMapSteps.Commit(args, rails, edit);
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
