using Mintrans.Core.Ids;
using Mintrans.World.Networks;
using Mintrans.World.Terrain;

namespace Mintrans.World.Tests;

/// <summary>Маленький мир для тестов, без файлов данных.</summary>
internal static class TestWorld
{
    public static TerrainCatalog Catalog() => new(
    [
        new TerrainCategory("surface", [new TypeId("grass"), new TypeId("rock")], "grass"),
        new TerrainCategory("water", [new TypeId("none"), new TypeId("river")], "none"),
    ]);

    public static WorldMap Map(int width = 16, int height = 16) =>
        new(width, height, seed: 1, Catalog(),
        [
            new NetworkDefinition("road", EdgeNetworkLayer.LayoutName),
            new NetworkDefinition("rail", TrackNetworkLayer.LayoutName),
        ]);

    public static EdgeNetworkLayer Road(this WorldMap map) => (EdgeNetworkLayer)map.Networks["road"];

    public static TrackNetworkLayer Rail(this WorldMap map) => (TrackNetworkLayer)map.Networks["rail"];
}
