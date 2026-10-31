using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;
using Mintrans.World.Rail;
using Mintrans.World.Spatial;

namespace Mintrans.World.Tests;

/// <summary>Мосты, сигналы, платформы и переезды на рельсовом графе (docs/10в, разделы 3–5, 7).</summary>
public class RailFixtureTests
{
    private static WorldPoint W(double x, double y) => new(x, y);

    private static RailNetwork Network() =>
        new(new SpatialWorld(WorldBounds.Sized(10_000, 10_000), new FlatTerrain()), new RailNetworkSettings("rail"));

    private static TrackEdgeId Straight(RailNetwork network, WorldPoint a, WorldPoint b, int? level = null)
    {
        var edit = network.Begin();
        var edge = edit.AddEdge(edit.NewNode(a), edit.NewNode(b), new PathGeometry(new StraightPrimitive(a, b)), level);
        var result = network.Commit(edit);
        Assert.True(result.Committed, string.Join("; ", result.Issues));
        return edge;
    }

    private static RailEditPreview Commit(RailNetwork network, Action<RailEdit> change)
    {
        var edit = network.Begin();
        change(edit);
        return network.Commit(edit);
    }

    [Fact]
    public void Bridge_is_a_straight_edge_above_ground_nodes_and_passes_over_another_line()
    {
        var network = Network();
        Straight(network, W(1000, 2002.5), W(2000, 2002.5));
        // Поперёк по земле — пересечение без стрелки запрещено.
        var edit = network.Begin();
        edit.AddEdge(edit.NewNode(W(1502.5, 1800)), edit.NewNode(W(1502.5, 2200)),
            new PathGeometry(new StraightPrimitive(W(1502.5, 1800), W(1502.5, 2200))));
        Assert.Contains(network.Preview(edit).Issues, i => i.Contains("мост"));
        // Тот же путь мостом — можно: опоры на земле, пролёт над линией.
        var bridge = Straight(network, W(1502.5, 1800), W(1502.5, 2200), level: 1);
        Assert.True(network.IsBridge(network.Edge(bridge)));
        Assert.Equal(0, network.Node(network.Edge(bridge).From).Level);
        // Кривой мост не строится.
        var arc = new ArcPrimitive(W(3000, 3300), 300, -Math.PI / 2, 0.3);
        var curved = network.Begin();
        curved.AddEdge(curved.NewNode(arc.Start), curved.NewNode(arc.End), new PathGeometry(arc), level: 1);
        Assert.Contains(network.Preview(curved).Issues, i => i.Contains("по прямой"));
    }

    [Fact]
    public void Only_one_signal_per_cell_and_none_on_a_switch()
    {
        var network = Network();
        var edge = Straight(network, W(1002.5, 1002.5), W(2002.5, 1002.5));
        Assert.True(Commit(network, e => e.AddSignal(edge, 300, "block_signal", SignalLogic.Block, SignalFacing.Forward)).Committed);
        // Та же клетка (300…305 м от начала) — второй сигнал нельзя.
        var second = Commit(network, e => e.AddSignal(edge, 301, "path_signal", SignalLogic.Path, SignalFacing.Both));
        Assert.False(second.Committed);
        Assert.Contains(second.Issues, i => i.Contains("одного на клетку"));
        Assert.True(Commit(network, e => e.AddSignal(edge, 310, "path_signal", SignalLogic.Path, SignalFacing.Both)).Committed);

        // Стрелка у 600 м: на неё сигнал не встать.
        var split = Commit(network, e =>
        {
            var node = e.SplitEdge(edge, 600);
            var arc = new ArcPrimitive(W(1602.5, 1302.5), 300, -Math.PI / 2, 0.4);
            e.AddEdge(node, e.NewNode(arc.End), new PathGeometry(arc));
        });
        Assert.True(split.Committed, string.Join("; ", split.Issues));
        var junction = network.Junctions.Single();
        var zone = junction.ConflictZone.First(z => z.ToM - z.FromM > 1);
        var onSwitch = Commit(network, e => e.AddSignal(zone.Edge, (zone.FromM + zone.ToM) / 2, "block_signal", SignalLogic.Block, SignalFacing.Both));
        Assert.Contains(onSwitch.Issues, i => i.Contains("стрелке"));
    }

    [Fact]
    public void Splitting_a_track_moves_signals_and_platforms_to_the_pieces_at_the_same_place()
    {
        var network = Network();
        var edge = Straight(network, W(1000, 1002.5), W(3000, 1002.5));
        Commit(network, e =>
        {
            e.AddSignal(edge, 1500, "block_signal", SignalLogic.Block, SignalFacing.Forward);
            e.AddBerth(BerthKind.Platform, edge, 200, 300, new SiteId(7), null);
        });
        var signalAt = network.Edge(edge).Geometry.PointAt(1500);
        RailChange? change = null;
        network.Changed += c => change = c;

        var result = Commit(network, e => e.SplitEdge(edge, 800));
        Assert.True(result.Committed, string.Join("; ", result.Issues));
        Assert.False(network.ContainsEdge(edge));
        var signal = network.Signals.Single();
        Assert.True(network.Edge(signal.Edge).Geometry.PointAt(signal.DistanceM).ApproxEquals(signalAt, 1e-6));
        Assert.Equal(SignalFacing.Forward, signal.Facing);
        var platform = network.Berths.Single();
        Assert.Equal(100, platform.LengthM, 9);
        Assert.Equal(200, platform.FromM, 9);
        Assert.NotNull(change);
        Assert.Equal(2, change!.Replaced[edge].Count);
        Assert.Equal((signal.Edge, signal.DistanceM), change.Map(edge, 1500));
    }

    [Fact]
    public void Platform_stands_on_a_straight_and_cannot_be_cut_by_a_switch()
    {
        var network = Network();
        var arc = new ArcPrimitive(W(1000, 1300), 300, -Math.PI / 2, 0.5);
        var curved = Commit(network, e => e.AddEdge(e.NewNode(arc.Start), e.NewNode(arc.End), new PathGeometry(arc)));
        var onCurve = Commit(network, e => e.AddBerth(BerthKind.Platform, curved.Edges[0].Id, 10, 40, new SiteId(1), null));
        Assert.Contains(onCurve.Issues, i => i.Contains("прямой"));

        var edge = Straight(network, W(3000, 3002.5), W(4000, 3002.5));
        Assert.True(Commit(network, e => e.AddBerth(BerthKind.Platform, edge, 100, 200, new SiteId(1), null)).Committed);
        var cut = Commit(network, e => e.SplitEdge(edge, 150));
        Assert.Contains(cut.Issues, i => i.Contains("платформ"));
        Assert.True(network.ContainsEdge(edge));
    }

    [Fact]
    public void Removing_a_track_loses_its_signals_and_berths()
    {
        var network = Network();
        var edge = Straight(network, W(1000, 1002.5), W(2000, 1002.5));
        Commit(network, e =>
        {
            e.AddSignal(edge, 500, "block_signal", SignalLogic.Block, SignalFacing.Both);
            e.AddBerth(BerthKind.Depot, edge, 100, 120, SiteId.None, new Facilities.FacilityId(3));
        });
        var removal = Commit(network, e => e.RemoveEdge(edge));
        Assert.True(removal.Committed);
        Assert.Single(removal.LostSignals);
        Assert.Single(removal.LostBerths);
        Assert.Empty(network.Signals);
        Assert.Empty(network.Berths);
        Assert.Equal(0, network.World.Count);
    }

    [Fact]
    public void Road_cell_crossed_by_a_ground_track_is_a_level_crossing_but_not_under_a_bridge()
    {
        var map = TestWorld.Map(80, 40);
        map.Road().LayPath([new GridPos(20, 5), new GridPos(20, 6), new GridPos(20, 7), new GridPos(20, 8), new GridPos(20, 9)]);
        map.Road().LayPath([new GridPos(40, 5), new GridPos(40, 6), new GridPos(40, 7), new GridPos(40, 8), new GridPos(40, 9)]);
        var rails = map.Rail();
        Straight(rails, SpatialScale.CellCenter(new GridPos(5, 7)), SpatialScale.CellCenter(new GridPos(30, 7)));
        Straight(rails, SpatialScale.CellCenter(new GridPos(35, 7)), SpatialScale.CellCenter(new GridPos(45, 7)), level: 1);

        var crossing = Assert.Single(map.Crossings.All);
        Assert.Equal(new GridPos(20, 7), crossing.Cell);
        Assert.True(crossing.RoadAlongY);
        Assert.True(crossing.ToM - crossing.FromM >= SpatialScale.CellM);
        Assert.False(map.Crossings.IsCrossing(new GridPos(40, 7)));
    }

    [Fact]
    public void Nearest_finds_the_track_point_under_a_click()
    {
        var network = Network();
        var edge = Straight(network, W(1000, 1002.5), W(2000, 1002.5));
        var near = network.Nearest(W(1250, 1004), 4);
        Assert.NotNull(near);
        Assert.Equal(edge, near!.Value.Edge.Id);
        Assert.Equal(250, near.Value.DistanceM, 3);
        Assert.Null(network.Nearest(W(1250, 1020), 4));
        Assert.NotNull(network.NearestFreeEnd(W(1003, 1003), 6));
    }

    [Fact]
    public void Polyline_corners_are_rounded_with_tangent_arcs()
    {
        var path = Fillet.Polyline([W(0, 0), W(1000, 0), W(1000, 1000)], 300);
        Assert.Equal(3, path.Primitives.Count);
        var arc = Assert.IsType<ArcPrimitive>(path.Primitives[1]);
        Assert.Equal(300, arc.Radius, 9);
        Assert.Equal(Math.PI / 2, path.EndHeading, 9);
        Assert.Equal(700 + 300 * Math.PI / 2 + 700, path.Length, 6);
        Assert.Throws<ArgumentException>(() => Fillet.Polyline([W(0, 0), W(100, 0), W(100, 100)], 300));
    }
}
