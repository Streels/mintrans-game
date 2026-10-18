using Mintrans.Core.Grid;
using Mintrans.Data.Json;
using Mintrans.World;
using Mintrans.World.Terrain;

namespace Mintrans.Data.Maps.Terrain;

// Операции раздела "terrain". Природные категории (surface, vegetation, water, deposit...)
// берутся из content/world/terrain.json, поэтому новая категория не требует нового кода.

/// <summary>{ "op": "fill", "layer": "surface", "kind": "grass" } — вся карта.</summary>
public sealed class FillOperation : IMapOperation
{
    public string Name => "fill";

    public void Apply(JsonNode args, WorldMap map) =>
        Paint(args, map, map.Bounds.Cells());

    internal static void Paint(JsonNode args, WorldMap map, IEnumerable<GridPos> cells)
    {
        var layer = Layer(args, map);
        var kind = args.String("kind");
        var index = layer.Category.TryGet(kind, out var found)
            ? found.Index
            : throw args.Get("kind").Error($"в категории «{layer.Category.Id}» нет вида «{kind}»");
        foreach (var cell in cells)
            layer.Cells[cell] = index;
    }

    internal static TerrainLayer Layer(JsonNode args, WorldMap map)
    {
        var id = args.String("layer");
        return map.Terrain.TryGet(id, out var layer)
            ? layer
            : throw args.Get("layer").Error(
                $"нет слоя «{id}». Доступны: {string.Join(", ", map.Terrain.Layers.Select(l => l.Category.Id))}");
    }
}

/// <summary>{ "op": "rect", "layer": ..., "kind": ..., "rect": [x, y, w, h] }</summary>
public sealed class RectOperation : IMapOperation
{
    public string Name => "rect";

    public void Apply(JsonNode args, WorldMap map) =>
        FillOperation.Paint(args, map, Shapes.Clip(map.Bounds, args.Rect("rect").Cells()));
}

/// <summary>{ "op": "ellipse", "layer": ..., "kind": ..., "center": [x, y], "radius": [rx, ry] }</summary>
public sealed class EllipseOperation : IMapOperation
{
    public string Name => "ellipse";

    public void Apply(JsonNode args, WorldMap map)
    {
        var (rx, ry) = args.Get("radius").AsPair();
        FillOperation.Paint(args, map, Shapes.Ellipse(map.Bounds, args.Pos("center"), rx, ry).Select(c => c.Cell));
    }
}

/// <summary>{ "op": "path", "layer": ..., "kind": ..., "points": [[x, y], ...], "width": 3 } — реки, полосы.</summary>
public sealed class PathOperation : IMapOperation
{
    public string Name => "path";

    public void Apply(JsonNode args, WorldMap map)
    {
        var points = args.Get("points").Items().Select(p => p.AsPos()).ToList();
        FillOperation.Paint(args, map, Shapes.ThickPolyline(map.Bounds, points, args.Double("width")));
    }
}

/// <summary>
/// { "op": "hill", "center": [x, y], "radius": [rx, ry], "height": 12 } — плавно поднимает
/// (или при отрицательной высоте опускает) рельеф. Холмы складываются.
/// </summary>
public sealed class HillOperation : IMapOperation
{
    public string Name => "hill";

    public void Apply(JsonNode args, WorldMap map)
    {
        var (rx, ry) = args.Get("radius").AsPair();
        var peak = args.Int("height");
        foreach (var (cell, distance) in Shapes.Ellipse(map.Bounds, args.Pos("center"), rx, ry))
        {
            // Косинусный профиль: вершина в центре, нулевой уклон у края.
            var lift = (int)Math.Round(peak * (Math.Cos(Math.PI * distance) + 1) / 2);
            map.Terrain.Height[cell] = (short)Math.Clamp(map.Terrain.Height[cell] + lift, short.MinValue, short.MaxValue);
        }
    }
}

/// <summary>{ "op": "level", "rect": [x, y, w, h], "height": 0 } — выровнять площадку.</summary>
public sealed class LevelOperation : IMapOperation
{
    public string Name => "level";

    public void Apply(JsonNode args, WorldMap map)
    {
        var height = (short)args.Int("height");
        foreach (var cell in Shapes.Clip(map.Bounds, args.Rect("rect").Cells()))
            map.Terrain.Height[cell] = height;
    }
}
