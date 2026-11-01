using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Rail;

namespace Mintrans.Game.UI;

/// <summary>Текстовое описание клетки: всё, что карта о ней знает. Не зависит от того, как его показывают.</summary>
public static class CellDescriber
{
    public static IEnumerable<string> Describe(WorldMap map, GridPos cell, DisplayNames names)
    {
        yield return $"Клетка {cell.X}, {cell.Y}";
        yield return $"{names["height"]}: {map.Terrain.Height[cell]}";

        foreach (var layer in map.Terrain.Layers)
        {
            var kind = layer.KindAt(cell);
            if (kind != layer.Category.Default || layer.Category.Id.Value == "surface")
                yield return $"{names[layer.Category.Id.Value]}: {names[kind.Id.Value]}";
        }

        foreach (var network in map.Networks.Layers)
        {
            if (network.BridgeOver(cell) is { } over)
                yield return $"Над клеткой: {names[over.Kind.Value]}";
            if (!network.HasAny(cell))
                continue;
            // Дорога: «С, В» — куда выходит.
            var strokes = network.StrokesAt(cell).Select(s => s.From is { } from
                ? $"{names[from.ToString()]}–{names[s.To.ToString()]}"
                : names[s.To.ToString()]);
            yield return $"{names[network.Type.Value]}: {string.Join(", ", strokes)}";
            if (network.BridgeAt(cell) is { } bridge)
                yield return $"Опора моста: {names[bridge.Kind.Value]}, {bridge.Length} кл.";
        }

        if (map.Rails is { } rails)
        {
            foreach (var level in new[] { 0, 1 })
            foreach (var edge in RailCellQuery.EdgesIn(map, cell, level))
            {
                var kind = rails.IsBridge(edge) ? "мост" : edge.Geometry.IsStraight ? "прямая" : "кривая";
                var radius = edge.Geometry.Primitives.OfType<Core.Geometry.ArcPrimitive>().Select(a => a.Radius).DefaultIfEmpty(0).Min();
                var curve = radius > 0 ? $", R {radius:0} м" : "";
                var junction = rails.JunctionAt(edge.From) is not null || rails.JunctionAt(edge.To) is not null ? ", у стрелки" : "";
                yield return $"{names[rails.Settings.Kind.Value]}: {kind}{curve}, {edge.LengthM:0} м{junction}";
            }
            foreach (var signal in rails.Signals.Where(s => rails.CellOf(s) == cell))
                yield return $"{names[signal.Kind.Value]}: {(signal.TwoWay ? "двусторонний" : "односторонний")}";
        }
        if (map.Crossings.IsCrossing(cell))
            yield return "Переезд";

        if (map.Facilities.At(cell) is { } facility)
            yield return $"Сооружение: {facility.Name}";

        if (map.Sites.At(cell) is { } site)
        {
            yield return $"Объект: {site.Name} ({names[site.Type.Value]})";
            if (site.PartAt(cell) is { } part && part.Kind != Mintrans.World.Sites.Site.BasePart)
                yield return $"Часть площадки: {names[part.Kind.Value]}";
        }
    }
}
