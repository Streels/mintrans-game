using Mintrans.Construction.Rail;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Rail;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.Construction.Tests;

/// <summary>Стройка рельсов игроком на метрическом графе (docs/10в): соединения, мосты, станции, депо, сигналы, переезды, снос.</summary>
public class RailBuildTests
{
    private static GridPos P(int x, int y) => new(x, y);

    /// <summary>Центр клетки в метрах.</summary>
    private static WorldPoint C(int x, int y) => SpatialScale.CellCenter(P(x, y));

    /// <summary>
    /// Карта 400×240 клеток (2 × 1,2 км): завод 8×8 в (40, 40), город 20×20 в (300, 180), ручей по столбцу x = 200 на строках 0–60.
    /// </summary>
    private static (WorldMap Map, Builder Builder) Setup()
    {
        var terrain = new TerrainCatalog(
        [
            new TerrainCategory("surface", [new TypeId("grass")], "grass"),
            new TerrainCategory("water", [new TypeId("none"), new TypeId("river")], "none"),
        ]);
        var map = new WorldMap(400, 240, 0, terrain,
        [
            new NetworkDefinition("road", EdgeNetworkLayer.LayoutName),
            new NetworkDefinition("rail", NetworkDefinition.GraphLayout),
        ], RailEditorSettings.Default.NetworkSettings("rail"));
        for (var y = 0; y <= 60; y++)
            map.Terrain["water"].Set(P(200, y), "river");
        var placement = SitePlacement.Basic();
        placement.Place(map, new NewSiteRequest("plant", "steel_plant", "Завод", new GridRect(40, 40, 8, 8)));
        placement.Place(map, new NewSiteRequest("city", "city", "Город", new GridRect(300, 180, 20, 20)));

        var catalog = new ConstructionCatalog(
        [
            new NetworkBuildDefinition("rail", [], []),
            new NetworkBuildDefinition("road", [new TypeId("city")], [new TypeId("truck_station")]),
        ],
        [
            new StationDefinition("rail_station", "rail", [new TypeId("steel_plant")], 0, MaxTracks: 2, MaxDistanceM: 20),
            new StationDefinition("truck_station", "road", [new TypeId("steel_plant")], 2),
        ],
        [new ForbiddenTerrain("water", [new TypeId("river")])],
        facilities:
        [
            new FacilityDefinition("rail_depot", "Ж/д депо", "rail", FacilityPlacement.RailDepot, [], 4),
            new FacilityDefinition("truck_garage", "Автогараж", "road", FacilityPlacement.Depot, [], 1),
        ],
        bridges: [new BridgeDefinition("rail_bridge", "rail", 30)],
        signals:
        [
            new SignalDefinition("block_signal", "rail", SignalLogic.Block),
            new SignalDefinition("path_signal", "rail", SignalLogic.Path),
        ],
        levelCrossings: [new LevelCrossing("road", "rail")]);
        return (map, new Builder(map, catalog));
    }

    private static void Built(RailEditPreview? result) => Assert.True(result is { Committed: true }, result is null ? "нет соединения" : string.Join("; ", result.Issues));

    private static void Built(RailPlan plan) => Assert.True(plan.Result is { Committed: true }, string.Join("; ", plan.Problems.Select(p => p.Message)));

    [Fact]
    public void Straight_between_free_points_then_smooth_curve_from_its_end()
    {
        var (map, builder) = Setup();
        Built(builder.BuildRail(C(10, 100), C(100, 100)));
        var rails = map.RequireRails();
        Assert.Equal(450, rails.Edges.Single().LengthM, 6);

        // От конца — дуга и прямая к точке; по умолчанию — наибольший радиус без большого объезда.
        var connection = builder.PlanRail(C(100, 100), C(180, 150));
        Assert.True(connection.CanConfirm, string.Join("; ", connection.Rejections));
        Assert.True(connection.Current!.RadiusM >= RailEditorSettings.Default.MinRadiusM);
        Assert.True(connection.Radii.Count > 1);
        // Колесо меняет радиус, мир не меняется.
        connection.ScrollRadius(-1);
        Assert.Single(rails.Edges);
        Built(connection.Confirm());
        Assert.Equal(2, rails.Edges.Count());
        Assert.Equal(2, rails.FreeEnds.Count());
        var route = RailRouter.Find(rails,
            new TrackPosition(new DirectedEdge(rails.Edges.First().Id, true), 0),
            new TrackPosition(new DirectedEdge(rails.Edges.Last().Id, true), 1));
        Assert.NotNull(route);
    }

    [Fact]
    public void Track_from_the_middle_of_a_straight_becomes_a_switch()
    {
        var (map, builder) = Setup();
        Built(builder.BuildRail(C(10, 100), C(150, 100)));
        Built(builder.BuildRail(C(40, 100), C(120, 140)));
        var rails = map.RequireRails();
        Assert.Single(rails.Junctions);
        Assert.Equal(3, rails.Edges.Count());
    }

    [Fact]
    public void Water_is_crossed_only_by_a_bridge_with_heads_on_land()
    {
        var (map, builder) = Setup();
        Assert.Null(builder.BuildRail(C(150, 30), C(250, 30)));
        Assert.Contains(builder.PlanRail(C(150, 30), C(250, 30)).Rejections, r => r.Contains("запрещённой природе"));

        Built(builder.BuildRail(C(150, 30), C(196, 30)));
        var bridge = builder.BuildRailBridge("rail_bridge", C(196, 30), C(204, 30));
        Built(bridge);
        Built(builder.BuildRail(C(204, 30), C(250, 30)));
        var rails = map.RequireRails();
        Assert.Single(rails.Edges, rails.IsBridge);
        var first = rails.Edges.First();
        var last = rails.Edges.Last();
        Assert.NotNull(RailRouter.Find(rails, new TrackPosition(new DirectedEdge(first.Id, true), 0),
            new TrackPosition(new DirectedEdge(last.Id, true), last.LengthM)));

        // Опора в воде — нельзя.
        Built(builder.BuildRail(C(150, 50), C(198, 50)));
        Assert.False(builder.PlanRailBridge("rail_bridge", C(198, 50), C(200, 50)).CanBuild);
    }

    [Fact]
    public void Track_cannot_pass_through_an_enterprise()
    {
        var (_, builder) = Setup();
        Assert.Contains(builder.PlanRail(C(30, 44), C(60, 44)).Rejections, r => r.Contains("площадку «Завод»"));
    }

    [Fact]
    public void Platform_stands_next_to_one_enterprise_and_their_number_is_limited()
    {
        var (map, builder) = Setup();
        // Пути вдоль южной стороны завода (его площадка до y = 240 м): оси через 7,5, 12,5 и 17,5 м.
        foreach (var y in new[] { 49, 50, 51 })
            Built(builder.BuildRail(C(20, y), C(80, y)));
        Built(builder.BuildRail(C(20, 60), C(80, 60)));

        var far = builder.PlanPlatform("rail_station", C(40, 60), C(47, 60));
        Assert.Contains(far.Problems, p => p.Code == "no_enterprise");

        var plan = builder.BuildPlatform("rail_station", C(40, 50), C(47, 50));
        Built(plan);
        Assert.Equal("Завод", plan.Target!.Name);
        var platform = map.RequireRails().Berths.Single();
        Assert.Equal(40, platform.LengthM, 6);
        Built(builder.BuildPlatform("rail_station", C(40, 51), C(45, 51)));
        Assert.Contains(builder.PlanPlatform("rail_station", C(40, 49), C(45, 49)).Problems, p => p.Code == "too_many_tracks");
    }

    [Fact]
    public void Depot_stands_on_a_straight_and_registers_a_facility_with_its_berth()
    {
        var (map, builder) = Setup();
        Built(builder.BuildRail(C(10, 100), C(150, 100)));
        var plan = builder.BuildRailDepot("rail_depot", C(120, 100), C(125, 100));
        Built(plan);
        var depot = plan.Built!;
        Assert.All(depot.Cells, cell => Assert.Equal(100, cell.Y));
        Assert.Equal(depot, map.Facilities.At(P(121, 100)));
        var berth = builder.DepotBerth(depot.Id);
        Assert.NotNull(berth);
        Assert.Equal(20, berth!.LengthM, 6);
        // Сигнал в депо не ставится: у него встроенные.
        Assert.False(builder.PlanRailSignal("block_signal", C(121, 100), C(121, 100)).CanBuild);
    }

    [Fact]
    public void Signal_is_two_way_by_click_one_way_by_drag_and_one_per_cell()
    {
        var (map, builder) = Setup();
        Built(builder.BuildRail(C(10, 100), C(150, 100)));
        Assert.Equal(SignalFacing.Both, ((RailPlan)builder.PlanRailSignal("block_signal", C(60, 100), C(60, 100))).Edit!.NewSignals[0].Facing);
        Assert.Equal(SignalFacing.Forward, builder.PlanRailSignal("block_signal", C(60, 100), C(62, 100)).Edit!.NewSignals[0].Facing);
        Assert.Equal(SignalFacing.Backward, builder.PlanRailSignal("block_signal", C(60, 100), C(58, 100)).Edit!.NewSignals[0].Facing);
        Built(builder.BuildRailSignal("path_signal", C(60, 100), C(60, 100)));
        Assert.False(builder.PlanRailSignal("block_signal", C(60, 100), C(61, 100)).CanBuild);
        Assert.Single(map.RequireRails().Signals);

        Assert.True(builder.RemoveSignals(P(55, 95), P(65, 105)).CanBuild);
        Assert.Empty(map.RequireRails().Signals);
    }

    [Fact]
    public void Road_and_rail_cross_only_straight_across()
    {
        var (map, builder) = Setup();
        Built(builder.BuildRail(C(10, 100), C(150, 100)));
        Assert.True(builder.BuildPath("road", P(50, 95), P(50, 105)).CanBuild);
        Assert.True(map.Crossings.IsCrossing(P(50, 100)));
        Assert.Contains(builder.PlanPath("road", P(60, 100), P(70, 100)).Problems, p => p.Code == "crossing_shape");

        // Рельсы через готовую дорогу: поперёк — переезд, у перекрёстка — нельзя.
        builder.BuildPath("road", P(250, 90), P(250, 110));
        Built(builder.BuildRail(C(240, 100), C(260, 100)));
        Assert.True(map.Crossings.IsCrossing(P(250, 100)));
        builder.BuildPath("road", P(300, 80), P(300, 120));
        builder.BuildPath("road", P(295, 130), P(305, 130));
        builder.BuildPath("road", P(300, 120), P(300, 130));
        Assert.Contains(builder.PlanRail(C(290, 130), C(310, 130)).Rejections, r => r.Contains("переезд"));
    }

    [Fact]
    public void Demolish_cuts_the_track_at_the_rectangle_and_takes_its_signals()
    {
        var (map, builder) = Setup();
        Built(builder.BuildRail(C(10, 100), C(150, 100)));
        Built(builder.BuildRailSignal("block_signal", C(50, 100), C(50, 100)));
        var rails = map.RequireRails();

        Assert.True(builder.Demolish(P(45, 95), P(55, 105)).CanBuild);
        Assert.Equal(2, rails.Edges.Count());
        Assert.Empty(rails.Signals);
        Assert.Equal(4, rails.FreeEnds.Count());
        Assert.Empty(rails.World.Index.At(P(50, 100)));
        Assert.NotEmpty(rails.World.Index.At(P(40, 100)));
        Assert.NotEmpty(rails.World.Index.At(P(60, 100)));
    }

    [Fact]
    public void Demolishing_the_track_under_a_depot_removes_the_depot()
    {
        var (map, builder) = Setup();
        Built(builder.BuildRail(C(10, 100), C(150, 100)));
        var depot = builder.BuildRailDepot("rail_depot", C(120, 100), C(125, 100)).Built!;
        Assert.True(builder.Demolish(P(118, 98), P(130, 102)).CanBuild);
        Assert.False(map.Facilities.TryGet(depot.Id, out _));
        Assert.Empty(map.RequireRails().Berths);
    }
}
