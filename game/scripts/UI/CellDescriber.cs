using Mintrans.Core.Grid;
using Mintrans.World;

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
            if (!network.HasAny(cell))
                continue;
            // Дорога: «С, В» — куда выходит; рельсы: «С–Ю, З–С» — уложенные куски.
            var strokes = network.StrokesAt(cell).Select(s => s.From is { } from
                ? $"{names[from.ToString()]}–{names[s.To.ToString()]}"
                : names[s.To.ToString()]);
            yield return $"{names[network.Type.Value]}: {string.Join(", ", strokes)}";
        }

        if (map.Sites.At(cell) is { } site)
        {
            yield return $"Объект: {site.Name} ({names[site.Type.Value]})";
            if (site.PartAt(cell) is { } part && part.Kind != Mintrans.World.Sites.Site.BasePart)
                yield return $"Часть площадки: {names[part.Kind.Value]}";
        }
    }
}
