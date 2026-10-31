using Mintrans.Core.Geometry;
using Mintrans.Core.Spatial;
using Mintrans.World.Rail;
using Mintrans.World.Spatial;

namespace Mintrans.World.Tests;

/// <summary>Рельсовый граф на метрической геометрии (docs/02в, раздел 7; docs/10в).</summary>
public class RailNetworkTests
{
    private static WorldPoint W(double x, double y) => new(x, y);

    private static RailNetwork Network() =>
        new(new SpatialWorld(WorldBounds.Sized(40_000, 40_000), new FlatTerrain()), new RailNetworkSettings("rail"));

    private static TrackEdgeId Straight(RailNetwork network, WorldPoint a, WorldPoint b, int level = 0)
    {
        var edit = network.Begin();
        var edge = edit.AddEdge(edit.NewNode(a, level), edit.NewNode(b, level), new PathGeometry(new StraightPrimitive(a, b)));
        var result = network.Commit(edit);
        Assert.True(result.Committed, string.Join("; ", result.Issues));
        return edge;
    }

    [Fact]
    public void Straight_edge_has_metric_length_free_ends_and_a_corridor_in_the_index()
    {
        var network = Network();
        var edge = Straight(network, W(1000, 1000), W(1750, 1000));
        Assert.Equal(750, network.Edge(edge).LengthM, 9);
        var ends = network.FreeEnds.ToList();
        Assert.Equal(2, ends.Count);
        Assert.Equal(Math.PI, ends[0].Heading, 9);
        Assert.Equal(0, ends[1].Heading, 9);
        Assert.Contains(network.Edge(edge).Spatial, network.World.Index.At(SpatialScale.ToCell(W(1300, 1000))));
    }

    [Fact]
    public void Kink_at_a_node_is_rejected_and_nothing_is_built()
    {
        var network = Network();
        var edge = Straight(network, W(1000, 1000), W(1500, 1000));
        var end = network.Edge(edge).To;
        var edit = network.Begin();
        edit.AddEdge(end, edit.NewNode(W(1500, 1500)), new PathGeometry(new StraightPrimitive(W(1500, 1000), W(1500, 1500))));
        var result = network.Commit(edit);
        Assert.False(result.Committed);
        Assert.Contains(result.Issues, i => i.Contains("излом"));
        Assert.Single(network.Edges);
        Assert.Equal(1, network.World.Count);
    }

    [Fact]
    public void Too_sharp_curve_is_rejected()
    {
        var network = Network();
        var edit = network.Begin();
        var arc = new ArcPrimitive(W(1000, 1100), 100, -Math.PI / 2, 0.5);
        edit.AddEdge(edit.NewNode(arc.Start), edit.NewNode(arc.End), new PathGeometry(arc));
        Assert.Contains(network.Preview(edit).Issues, i => i.Contains("радиус"));
    }

    [Fact]
    public void Independent_lines_do_not_cross_on_one_level_but_may_on_a_bridge()
    {
        var network = Network();
        Straight(network, W(1000, 1000), W(2000, 1000));
        var edit = network.Begin();
        edit.AddEdge(edit.NewNode(W(1500, 500)), edit.NewNode(W(1500, 1500)),
            new PathGeometry(new StraightPrimitive(W(1500, 500), W(1500, 1500))));
        Assert.False(network.Commit(edit).Committed);

        Straight(network, W(1500, 500), W(1500, 1500), level: 1);
        Assert.Equal(2, network.Edges.Count());
        // Пересечение в плане без общего узла не соединяет линии.
        Assert.All(network.Nodes, n => Assert.Single(network.EdgesAt(n.Id)));
    }

    [Fact]
    public void Extending_a_free_end_joins_tracks_through_a_node_without_conflict()
    {
        var network = Network();
        var first = Straight(network, W(1000, 1000), W(1500, 1000));
        var end = network.Edge(first).To;
        var edit = network.Begin();
        var second = edit.AddEdge(end, edit.NewNode(W(1800, 1000)), new PathGeometry(new StraightPrimitive(W(1500, 1000), W(1800, 1000))));
        Assert.True(network.Commit(edit).Committed);

        Assert.Equal([new DirectedEdge(second, true)], network.Next(new DirectedEdge(first, true)));
        Assert.Equal([new DirectedEdge(first, false)], network.Next(new DirectedEdge(second, false)));
        Assert.Equal(2, network.FreeEnds.Count());
    }

    [Fact]
    public void Split_keeps_straight_run_and_router_measures_metres()
    {
        var network = Network();
        var main = Straight(network, W(1000, 1000), W(2000, 1000));
        var edit = network.Begin();
        var node = edit.SplitEdge(main, 400);
        Assert.True(network.Commit(edit).Committed);

        Assert.Equal(W(1400, 1000), network.Node(node).At);
        var parts = network.EdgesAt(node);
        Assert.Equal(2, parts.Count);
        Assert.False(network.ContainsEdge(main));
        var from = new TrackPosition(new DirectedEdge(parts[0].Id, true), 100);
        var to = new TrackPosition(new DirectedEdge(parts[1].Id, true), 250);
        var route = RailRouter.Find(network, from, to);
        Assert.NotNull(route);
        Assert.Equal(300 + 250, route.LengthM, 9);
        // В обратную сторону без разворота не доехать.
        Assert.Null(RailRouter.Find(network, to, from));
        Assert.Null(RailRouter.Find(network, new TrackPosition(to.Edge.Reverse(), 0), to));
    }

    [Fact]
    public void Removing_edges_drops_orphan_nodes_and_spatial_records()
    {
        var network = Network();
        var edge = Straight(network, W(1000, 1000), W(2000, 1000));
        var edit = network.Begin();
        edit.RemoveEdge(edge);
        Assert.True(network.Commit(edit).Committed);
        Assert.Empty(network.Nodes);
        Assert.Equal(0, network.World.Count);
        Assert.Equal(0, network.World.Index.ChunkCount);
    }
}
