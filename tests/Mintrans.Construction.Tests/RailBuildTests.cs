using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.Construction.Tests;

/// <summary>Строительство по docs/10, 10а, 10б: пересечения, переезды, мосты, сигналы, многопутные станции.</summary>
public class RailBuildTests
{
    private static GridPos P(int x, int y) => new(x, y);

    /// <summary>Карта 32×24: завод 2×2 в (4, 4), река по столбцу x = 20.</summary>
    private static (WorldMap Map, Builder Builder) Setup()
    {
        var terrain = new TerrainCatalog(
        [
            new TerrainCategory("surface", [new TypeId("grass")], "grass"),
            new TerrainCategory("water", [new TypeId("none"), new TypeId("river")], "none"),
        ]);
        var map = new WorldMap(32, 24, 0, terrain,
        [
            new NetworkDefinition("road", EdgeNetworkLayer.LayoutName),
            new NetworkDefinition("rail", TrackNetworkLayer.LayoutName),
        ]);
        for (var y = 0; y < 24; y++)
            map.Terrain["water"].Set(P(20, y), "river");
        SitePlacement.Basic().Place(map, new NewSiteRequest("plant", "steel_plant", "Завод", new GridRect(4, 4, 2, 2)));

        var catalog = new ConstructionCatalog(
            [new NetworkBuildDefinition("rail", [], [new TypeId("rail_station")]), new NetworkBuildDefinition("road", [], [])],
            [new StationDefinition("rail_station", "rail", [new TypeId("steel_plant")], 6, 3)],
            [new ForbiddenTerrain("water", [new TypeId("river")])],
            [],
            [new BridgeDefinition("rail_bridge", "rail", 6), new BridgeDefinition("road_bridge", "road", 6)],
            [new SignalDefinition("block_signal", "rail", SignalLogic.Block), new SignalDefinition("path_signal", "rail", SignalLogic.Path)],
            [new LevelCrossing("road", "rail")]);
        return (map, new Builder(map, catalog));
    }

    private static TrackNetworkLayer Rail(WorldMap map) => (TrackNetworkLayer)map.Networks["rail"];

    [Fact]
    public void Rail_lines_cross_at_grade_but_cannot_turn_on_the_cross()
    {
        var (map, builder) = Setup();
        Assert.True(builder.BuildPath("rail", P(8, 10), P(16, 10)).CanBuild);
        var plan = builder.BuildPath("rail", P(12, 6), P(12, 14));
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.Equal(TrackPieces.NorthSouth | TrackPieces.EastWest, Rail(map).PiecesAt(P(12, 10)));

        // Стрелка в клетке креста не строится, на кресте нет места сигналу.
        Assert.Contains(builder.PlanPath("rail", P(11, 10), P(13, 12)).Problems, p => p.Code == "cross_with_turn" && p.Cell == P(12, 10));
        Assert.Contains(builder.PlanSignal("block_signal", P(12, 10), P(12, 10)).Problems, p => p.Code == "junction");
        // Через стрелку крест не построить тоже.
        Assert.True(builder.BuildPath("rail", P(13, 10), P(15, 12)).CanBuild);
        Assert.Contains(builder.PlanPath("rail", P(14, 6), P(14, 14)).Problems, p => p.Code == "cross_with_turn" && p.Cell == P(14, 10));
    }

    [Fact]
    public void Switch_is_built_from_a_straight_and_a_curve()
    {
        var (map, builder) = Setup();
        Assert.True(builder.BuildPath("rail", P(8, 10), P(16, 10)).CanBuild);
        // Протяжка от линии в сторону: в (12,10) — стрелка, прямая плюс поворот.
        Assert.True(builder.BuildPath("rail", P(11, 10), P(13, 12)).CanBuild);
        Assert.Equal(TrackPieces.EastWest | TrackPieces.SouthWest, Rail(map).PiecesAt(P(12, 10)));
    }

    [Fact]
    public void Road_crosses_rail_only_straight_and_across()
    {
        var (map, builder) = Setup();
        Assert.True(builder.BuildPath("rail", P(8, 10), P(16, 10)).CanBuild);
        Assert.True(builder.BuildPath("road", P(10, 6), P(10, 14)).CanBuild);
        Assert.True(NetworkShapes.IsLevelCrossing(map, P(10, 10)));
        // Дорога вдоль пути, поворот на переезде или тупик на нём — нельзя.
        Assert.Contains(builder.PlanPath("road", P(12, 10), P(14, 10)).Problems, p => p.Code == "crossing_shape");
        Assert.Contains(builder.PlanPath("road", P(14, 6), P(14, 10)).Problems, p => p.Code == "crossing_shape");
        // Рельсы поверх переезда не поворачивают.
        Assert.Contains(builder.PlanPath("rail", P(10, 6), P(10, 9)).Problems, p => p.Code == "crossing_shape");
    }

    [Fact]
    public void Bridge_spans_river_and_another_line_and_joins_track_from_behind()
    {
        var (map, builder) = Setup();
        Assert.True(builder.BuildPath("rail", P(22, 2), P(22, 20)).CanBuild);
        var plan = builder.BuildBridge("rail_bridge", P(19, 8), P(23, 8));
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.True(builder.BuildPath("rail", P(14, 8), P(18, 8)).CanBuild);
        Assert.True(builder.BuildPath("rail", P(24, 8), P(28, 8)).CanBuild);

        var rail = Rail(map);
        Assert.True(NetworkQueries.AreConnected(rail, [P(14, 8)], [P(28, 8)]));
        Assert.True(NetworkQueries.AreConnected(rail, [P(22, 2)], [P(22, 20)]));
        Assert.False(NetworkQueries.AreConnected(rail, [P(14, 8)], [P(22, 2)]));
    }

    [Fact]
    public void Bridge_needs_free_heads_and_a_span_without_sites_or_other_bridges()
    {
        var (_, builder) = Setup();
        Assert.Contains(builder.PlanBridge("rail_bridge", P(18, 8), P(19, 8)).Problems, p => p.Code == "too_short");
        Assert.Contains(builder.PlanBridge("rail_bridge", P(15, 8), P(25, 8)).Problems, p => p.Code == "too_long");
        Assert.Contains(builder.PlanBridge("rail_bridge", P(18, 8), P(20, 8)).Problems, p => p.Code == "forbidden_terrain");
        Assert.Contains(builder.PlanBridge("rail_bridge", P(3, 5), P(7, 5)).Problems, p => p.Code == "site_occupied");
        Assert.True(builder.BuildBridge("rail_bridge", P(19, 12), P(21, 12)).CanBuild);
        Assert.Contains(builder.PlanBridge("road_bridge", P(17, 12), P(22, 12)).Problems, p => p.Code == "bridge_crossing");
        // Через опору путь не прокладывается.
        Assert.Contains(builder.PlanPath("rail", P(19, 10), P(19, 14)).Problems, p => p.Code == "bridge_head");
    }

    [Fact]
    public void Signals_go_on_plain_track_one_way_by_drag_two_way_by_click()
    {
        var (map, builder) = Setup();
        builder.BuildPath("rail", P(8, 10), P(16, 10));
        builder.BuildPath("rail", P(11, 10), P(13, 12));

        var oneWay = builder.BuildSignal("block_signal", P(9, 10), P(8, 10));
        Assert.True(oneWay.CanBuild);
        Assert.Equal(DirectionMask.West, oneWay.Signal!.Faces);
        var twoWay = builder.BuildSignal("path_signal", P(14, 10), P(14, 10));
        Assert.True(twoWay.CanBuild);
        Assert.True(twoWay.Signal!.TwoWay);

        Assert.Contains(builder.PlanSignal("block_signal", P(12, 10), P(12, 10)).Problems, p => p.Code == "junction");
        Assert.Contains(builder.PlanSignal("block_signal", P(3, 3), P(3, 3)).Problems, p => p.Code == "no_track");
        Assert.Contains(builder.PlanSignal("block_signal", P(10, 10), P(10, 11)).Problems, p => p.Code == "wrong_direction");
        // Стрелку на клетке с сигналом не построить; снятие сигналов оставляет путь.
        Assert.Contains(builder.PlanPath("rail", P(13, 10), P(15, 12)).Problems, p => p.Code == "signal_on_junction");
        Assert.True(builder.RemoveSignals(P(8, 10), P(16, 10)).CanBuild);
        Assert.Empty(Rail(map).Signals);
        Assert.Equal(TrackPieces.EastWest, Rail(map).PiecesAt(P(14, 10)));
    }

    [Fact]
    public void Station_rectangle_gives_parallel_tracks_and_respects_track_limit()
    {
        var (map, builder) = Setup();
        var plan = builder.BuildStation("rail_station", P(3, 6), P(7, 7));
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.Equal((5, 2), (plan.Length, plan.Tracks));
        Assert.All(plan.Cells, cell => Assert.Equal(TrackPieces.EastWest, Rail(map).PiecesAt(cell)));

        Assert.Contains(builder.PlanStation("rail_station", P(6, 0), P(9, 5)).Problems, p => p.Code == "too_wide");
    }
}
