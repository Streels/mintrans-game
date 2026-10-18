using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Terrain;

namespace Mintrans.Game.View;

/// <summary>
/// Цвет клетки по её природным слоям и высоте. Порядок и способ смешивания задаются стилем,
/// поэтому новая категория рельефа требует только записи в map_style.json.
/// </summary>
public sealed class TerrainColorizer
{
    private readonly WorldMap _map;
    private readonly MapStyle _style;
    private readonly List<(TerrainLayer Layer, TerrainLayerStyle Style, Color?[] ByIndex)> _layers = new();

    public TerrainColorizer(WorldMap map, MapStyle style)
    {
        _map = map;
        _style = style;
        foreach (var layerStyle in style.TerrainLayers)
        {
            if (!map.Terrain.TryGet(layerStyle.Category, out var layer))
            {
                GD.PushWarning($"Стиль описывает категорию «{layerStyle.Category}», которой нет на карте.");
                continue;
            }
            // Заранее раскладываем цвета по индексам видов, чтобы не искать по словарю в каждой клетке.
            var byIndex = layer.Category.Kinds
                .Select(kind => layerStyle.Colors.TryGetValue(kind.Id, out var c) ? c : (Color?)null)
                .ToArray();
            if (layerStyle.Mode == "base")
            {
                foreach (var kind in layer.Category.Kinds.Where(k => byIndex[k.Index] is null))
                {
                    GD.PushWarning($"Нет цвета для «{layer.Category.Id}/{kind.Id}».");
                    byIndex[kind.Index] = MapStyle.Missing;
                }
            }
            _layers.Add((layer, layerStyle, byIndex));
        }
    }

    public Color TopColor(GridPos cell)
    {
        var color = MapStyle.Missing;
        var isWater = false;
        foreach (var (layer, style, byIndex) in _layers)
        {
            if (byIndex[layer.Cells[cell]] is not { } kindColor)
                continue;
            switch (style.Mode)
            {
                case "base":
                    color = kindColor;
                    break;
                case "blend":
                    color = color.Lerp(kindColor, style.Amount);
                    break;
                case "replace":
                    color = kindColor;
                    isWater = true; // заменяющие слои (вода) не затеняем рельефом
                    break;
            }
        }
        if (isWater)
            return color;

        var height = _map.Terrain.Height[cell];
        var shade = 1f + height * _style.ShadePerLevel;
        // Свет с северо-запада: склон, обращённый к свету, светлее.
        var slope = HeightAt(cell.X - 1, cell.Y, height) + HeightAt(cell.X, cell.Y - 1, height) - 2 * height;
        shade += -slope * _style.SlopeShade;
        if (((cell.X + cell.Y) & 1) == 0)
            shade += _style.Checker;
        return new Color(color.R * shade, color.G * shade, color.B * shade, 1f);
    }

    private int HeightAt(int x, int y, int fallback)
    {
        var pos = new GridPos(x, y);
        return _map.Contains(pos) ? _map.Terrain.Height[pos] : fallback;
    }
}
