using Mintrans.Core.Grid;
using Mintrans.Data.Json;
using Mintrans.Data.Maps;
using Mintrans.World;
using Mintrans.World.Networks;

namespace Mintrans.Data.Tests;

public class MapLoaderTests
{
    private static readonly GameContent Content = ContentLocator.Load();

    private static MapDescription Load(string json) =>
        new MapLoader(Content.Terrain, Content.Networks).Load(JsonNode.Parse(json, "test.json"));

    private const string Header = """ "format": 1, "id": "t", "name": "t", "size": [32, 32], """;

    [Fact]
    public void Every_map_in_content_loads()
    {
        var ids = Content.MapIds().ToList();
        Assert.Contains("prototype", ids);
        Assert.All(ids, id => Assert.Equal(id, Content.LoadMap(id).Id));
    }

    [Fact]
    public void Terrain_steps_apply_in_order()
    {
        var map = Load($$"""
            { {{Header}}
              "terrain": [
                { "op": "fill", "layer": "surface", "kind": "rock" },
                { "op": "rect", "layer": "surface", "kind": "grass", "rect": [0, 0, 4, 4] },
                { "op": "hill", "center": [16, 16], "radius": [6, 6], "height": 10 }
              ] }
            """).Map;

        Assert.True(map.Terrain["surface"].Is(new GridPos(1, 1), "grass"));
        Assert.True(map.Terrain["surface"].Is(new GridPos(10, 10), "rock"));
        Assert.Equal(10, map.Terrain.Height[16, 16]);
        Assert.Equal(0, map.Terrain.Height[0, 0]);
    }

    [Fact]
    public void Unknown_operation_reports_location_and_known_names()
    {
        var error = Assert.Throws<ContentException>(() => Load($$"""
            { {{Header}} "terrain": [ { "op": "volcano" } ] }
            """));
        Assert.Contains("terrain[0]", error.Message);
        Assert.Contains("hill", error.Message);
    }

    [Fact]
    public void Unknown_kind_is_reported()
    {
        var error = Assert.Throws<ContentException>(() => Load($$"""
            { {{Header}} "terrain": [ { "op": "fill", "layer": "surface", "kind": "lava" } ] }
            """));
        Assert.Contains("lava", error.Message);
    }

    [Fact]
    public void Diagonal_line_becomes_staircase_of_corner_pieces()
    {
        var map = Load($$"""
            { {{Header}} "networks": [ { "op": "line", "network": "rail", "points": [[0, 0], [4, 0], [8, 4]] } ] }
            """).Map;
        var rail = (TrackNetworkLayer)map.Networks["rail"];

        Assert.Equal(TrackPieces.EastWest, rail.PiecesAt(new GridPos(2, 0)));
        Assert.Equal(TrackPieces.SouthWest, rail.PiecesAt(new GridPos(5, 0)));
        Assert.Equal(TrackPieces.NorthEast, rail.PiecesAt(new GridPos(5, 1)));
        Assert.True(NetworkQueries.AreConnected(rail, [new GridPos(0, 0)], [new GridPos(8, 4)]));
    }

    [Fact]
    public void Line_not_at_45_degrees_is_rejected()
    {
        var error = Assert.Throws<ContentException>(() => Load($$"""
            { {{Header}} "networks": [ { "op": "line", "network": "rail", "points": [[0, 0], [5, 2]] } ] }
            """));
        Assert.Contains("networks[0]", error.Message);
    }

    [Fact]
    public void Overlapping_sites_are_reported()
    {
        var error = Assert.Throws<ContentException>(() => Load($$"""
            { {{Header}} "sites": [
                { "key": "a", "type": "city", "name": "A", "rect": [0, 0, 5, 5] },
                { "key": "b", "type": "city", "name": "B", "rect": [3, 3, 5, 5] }
            ] }
            """));
        Assert.Contains("sites[1]", error.Message);
    }

    [Fact]
    public void New_operation_can_be_registered_without_touching_loader()
    {
        var terrain = MapOperations.DefaultTerrain();
        terrain.Register(new RaiseEverything());
        var loader = new MapLoader(Content.Terrain, Content.Networks, terrain);

        var map = loader.Load(JsonNode.Parse($$"""
            { {{Header}} "terrain": [ { "op": "raise_everything" } ] }
            """, "test.json")).Map;
        Assert.Equal(1, map.Terrain.Height[5, 5]);
    }

    private sealed class RaiseEverything : IMapOperation
    {
        public string Name => "raise_everything";

        public void Apply(JsonNode args, WorldMap map)
        {
            foreach (var cell in map.Bounds.Cells())
                map.Terrain.Height[cell]++;
        }
    }
}
