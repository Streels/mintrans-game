using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;
using Mintrans.World.Terrain;

namespace Mintrans.World.Tests;

public class TerrainTests
{
    [Fact]
    public void New_map_uses_default_kinds_and_zero_height()
    {
        var map = TestWorld.Map();
        var pos = new GridPos(3, 4);
        Assert.Equal(new TypeId("grass"), map.Terrain["surface"].KindAt(pos).Id);
        Assert.Equal(new TypeId("none"), map.Terrain["water"].KindAt(pos).Id);
        Assert.Equal(0, map.Terrain.Height[pos]);
    }

    [Fact]
    public void Categories_are_independent_layers()
    {
        var map = TestWorld.Map();
        var pos = new GridPos(2, 2);
        map.Terrain["surface"].Set(pos, "rock");
        map.Terrain["water"].Set(pos, "river");
        Assert.True(map.Terrain["surface"].Is(pos, "rock"));
        Assert.True(map.Terrain["water"].Is(pos, "river"));
    }

    [Fact]
    public void Unknown_kind_or_category_is_rejected()
    {
        var map = TestWorld.Map();
        Assert.Throws<KeyNotFoundException>(() => map.Terrain["surface"].Set(GridPos.Zero, "lava"));
        Assert.Throws<KeyNotFoundException>(() => map.Terrain["climate"]);
    }

    [Fact]
    public void Duplicate_kind_in_category_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new TerrainCategory("surface", [new TypeId("grass"), new TypeId("grass")], "grass"));
    }

    [Fact]
    public void Layer_rejects_cells_outside_map_and_counts_revisions()
    {
        var layer = new CellLayer<short>(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => layer[4, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => layer[-1, 0]);
        var before = layer.Revision;
        layer[1, 1] = 5;
        Assert.True(layer.Revision > before);
    }
}
