using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;
using Mintrans.World.Terrain;

namespace Mintrans.World.Spatial;

/// <summary>
/// Местность карты для пространственного мира: природа клетки, в которую попадает точка, и её высота.
/// Точки за краем карты берут ближайшую клетку края.
/// </summary>
public sealed class MapTerrainField(TerrainLayers terrain, int width, int height, double metresPerLevel = 1.0) : ITerrainField
{
    public TerrainSample Sample(WorldPoint p)
    {
        var cell = SpatialScale.ToCell(p);
        cell = new(Math.Clamp(cell.X, 0, width - 1), Math.Clamp(cell.Y, 0, height - 1));
        var kinds = new Dictionary<TypeId, TypeId>();
        foreach (var layer in terrain.Layers)
            kinds[layer.Category.Id] = layer.KindAt(cell).Id;
        return new TerrainSample(terrain.Height[cell] * metresPerLevel, kinds);
    }
}
