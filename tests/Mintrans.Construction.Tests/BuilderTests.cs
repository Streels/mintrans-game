using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.Construction.Tests;

public class BuilderTests
{
    private static GridPos P(int x, int y) => new(x, y);

    /// <summary>
    /// Карта 24×24: завод 2×2 в (4, 4), город 4×4 в (14, 14), река по столбцу x = 10 на строках 0–5.
    /// </summary>
    private static (WorldMap Map, Builder Builder) Setup()
    {
        var terrain = new TerrainCatalog(
        [
            new TerrainCategory("surface", [new TypeId("grass")], "grass"),
            new TerrainCategory("water", [new TypeId("none"), new TypeId("river")], "none"),
        ]);
        var map = new WorldMap(24, 24, 0, terrain,
        [
            new NetworkDefinition("road", EdgeNetworkLayer.LayoutName),
            new NetworkDefinition("rail", TrackNetworkLayer.LayoutName),
        ]);
        for (var y = 0; y < 6; y++)
            map.Terrain["water"].Set(P(10, y), "river");
        var placement = SitePlacement.Basic();
        placement.Place(map, new NewSiteRequest("plant", "steel_plant", "Завод", new GridRect(4, 4, 2, 2)));
        placement.Place(map, new NewSiteRequest("city", "city", "Город", new GridRect(14, 14, 4, 4)));

        var catalog = new ConstructionCatalog(
        [
            new NetworkBuildDefinition("rail", [], [new TypeId("rail_station")]),
            new NetworkBuildDefinition("road", [new TypeId("city")], [new TypeId("truck_station")]),
        ],
        [
            new StationDefinition("rail_station", "rail", [new TypeId("steel_plant")], 6),
            new StationDefinition("truck_station", "road", [new TypeId("steel_plant")], 2),
        ],
        [new ForbiddenTerrain("water", [new TypeId("river")])]);
        return (map, new Builder(map, catalog));
    }

    [Fact]
    public void Rail_drag_lays_straight_then_diagonal()
    {
        var (map, builder) = Setup();
        var plan = builder.BuildPath("rail", P(0, 10), P(8, 13));

        Assert.True(plan.CanBuild);
        var rail = (TrackNetworkLayer)map.Networks["rail"];
        Assert.Equal(TrackPieces.EastWest, rail.PiecesAt(P(3, 10)));
        Assert.True(NetworkQueries.AreConnected(rail, [P(0, 10)], [P(8, 13)]));
    }

    [Fact]
    public void Path_over_water_is_refused_and_nothing_is_built()
    {
        var (map, builder) = Setup();
        var plan = builder.BuildPath("rail", P(8, 2), P(12, 2));

        Assert.False(plan.CanBuild);
        Assert.Contains(plan.Problems, p => p.Code == "forbidden_terrain" && p.Cell == P(10, 2));
        Assert.Empty(map.Networks["rail"].Strokes());
    }

    [Fact]
    public void Rail_cannot_cross_enterprise_but_road_may_enter_city()
    {
        var (_, builder) = Setup();
        Assert.Contains(builder.PlanPath("rail", P(2, 4), P(8, 4)).Problems, p => p.Code == "site_occupied");
        Assert.True(builder.PlanPath("road", P(12, 15), P(16, 15)).CanBuild);
        Assert.False(builder.PlanPath("rail", P(12, 15), P(16, 15)).CanBuild);
    }

    [Fact]
    public void Station_strip_attaches_to_enterprise_and_carries_track()
    {
        var (map, builder) = Setup();
        var plan = builder.BuildStation("rail_station", P(3, 6), P(7, 6));

        Assert.True(plan.CanBuild);
        var plant = map.Sites.GetByKey("plant");
        Assert.Equal(new GridRect(3, 6, 5, 1), plan.Area);
        Assert.Equal(plant, map.Sites.At(P(7, 6)));
        Assert.Equal("rail_station", plant.PartAt(P(7, 6))!.Kind.Value);
        var rail = (TrackNetworkLayer)map.Networks["rail"];
        Assert.All(plan.Cells, cell => Assert.Equal(TrackPieces.EastWest, rail.PiecesAt(cell)));

        // Путь, подведённый к торцу станции, соединяется с ней.
        builder.BuildPath("rail", P(8, 6), P(12, 6));
        Assert.True(NetworkQueries.AreConnected(rail, [P(12, 6)], [P(3, 6)]));
    }

    [Fact]
    public void Station_must_touch_exactly_one_suitable_enterprise()
    {
        var (_, builder) = Setup();
        Assert.Contains(builder.PlanStation("rail_station", P(1, 1), P(3, 1)).Problems, p => p.Code == "no_enterprise");
        // К городу грузовая станция предприятия не примыкает: у городских станций свои правила (docs/10).
        Assert.Contains(builder.PlanStation("rail_station", P(14, 13), P(17, 13)).Problems, p => p.Code == "no_enterprise");
        Assert.Contains(builder.PlanStation("truck_station", P(3, 6), P(7, 6)).Problems, p => p.Code == "too_long");
    }

    [Fact]
    public void Single_cell_station_runs_track_along_enterprise_side()
    {
        var (map, builder) = Setup();
        Assert.True(builder.BuildStation("rail_station", P(6, 4), P(6, 4)).CanBuild);
        // Станция справа от завода — путь вдоль его стороны, то есть с севера на юг.
        Assert.Equal(TrackPieces.NorthSouth, ((TrackNetworkLayer)map.Networks["rail"]).PiecesAt(P(6, 4)));
    }

    [Fact]
    public void Station_cannot_be_built_over_another_network()
    {
        var (_, builder) = Setup();
        builder.BuildPath("road", P(2, 6), P(8, 6));
        Assert.Contains(builder.PlanStation("rail_station", P(3, 6), P(6, 6)).Problems, p => p.Code == "other_network");
    }

    [Fact]
    public void Demolish_removes_networks_and_stations_but_not_enterprise()
    {
        var (map, builder) = Setup();
        builder.BuildStation("rail_station", P(3, 6), P(7, 6));
        builder.BuildPath("rail", P(8, 6), P(12, 6));
        var plant = map.Sites.GetByKey("plant");

        Assert.True(builder.Demolish(P(5, 5), P(9, 6)).CanBuild);

        Assert.Single(plant.Parts);
        Assert.Null(map.Sites.At(P(3, 6)));
        Assert.False(map.Networks["rail"].HasAny(P(3, 6)));
        Assert.False(map.Networks["rail"].HasAny(P(9, 6)));
        Assert.True(map.Networks["rail"].HasAny(P(10, 6)));
        Assert.Equal(plant, map.Sites.At(P(5, 5)));
    }

    [Fact]
    public void Removing_road_cell_cuts_neighbor_links()
    {
        var (map, builder) = Setup();
        builder.BuildPath("road", P(0, 20), P(4, 20));
        builder.Demolish(P(2, 20), P(2, 20));

        var road = map.Networks["road"];
        Assert.False(road.EdgesAt(P(1, 20)).Has(Direction.East));
        Assert.False(road.EdgesAt(P(3, 20)).Has(Direction.West));
        Assert.False(NetworkQueries.AreConnected(road, [P(0, 20)], [P(4, 20)]));
    }

    [Fact]
    public void Station_holding_another_part_cannot_be_demolished()
    {
        var (map, builder) = Setup();
        builder.BuildStation("rail_station", P(4, 6), P(5, 6));
        // Вторая станция примыкает только к первой: без первой она «повиснет».
        SitePlacement.Basic().Attach(map, new AttachPartRequest("plant", "truck_station", new GridRect(4, 7, 1, 1)));

        var plan = builder.PlanDemolish(P(5, 6), P(5, 6));
        Assert.Contains(plan.Problems, p => p.Code == "part_holds_others");
    }
}
