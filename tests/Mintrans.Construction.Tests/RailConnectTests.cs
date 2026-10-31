using Mintrans.Construction.Rail;
using Mintrans.Core.Geometry;
using Mintrans.Core.Spatial;
using Mintrans.World.Rail;
using Mintrans.World.Spatial;

namespace Mintrans.Construction.Tests;

/// <summary>Инструмент «Соединить А→Б» и стрелки (обязательные тесты docs/10в, раздел 9).</summary>
public class RailConnectTests
{
    private static WorldPoint W(double x, double y) => new(x, y);

    private static RailConnectTool Tool()
    {
        var settings = RailEditorSettings.Default;
        var world = new SpatialWorld(WorldBounds.Sized(40_000, 40_000), new FlatTerrain());
        return new RailConnectTool(new RailNetwork(world, settings.NetworkSettings("rail")), settings);
    }

    private static TrackEdgeId Lay(RailConnectTool tool, Pose a, Pose b, int level = 0)
    {
        var connection = tool.Preview(new NewEndAnchor(a, level), new NewEndAnchor(b, level));
        Assert.True(connection.CanConfirm, string.Join("; ", connection.Rejections));
        var result = connection.Confirm();
        Assert.True(result.Committed, string.Join("; ", result.Issues));
        return result.Edges[0].Id;
    }

    private static void AssertSmooth(PathGeometry geometry, Pose start, Pose end)
    {
        Assert.True(geometry.Start.ApproxEquals(start.At, 1e-6));
        Assert.True(geometry.End.ApproxEquals(end.At, 1e-6));
        Assert.True(Angles.Difference(geometry.StartHeading, start.Heading) < 1e-6);
        Assert.True(Angles.Difference(geometry.EndHeading, end.Heading) < 1e-6);
        for (var i = 1; i < geometry.Primitives.Count; i++)
            Assert.True(Angles.Difference(geometry.Primitives[i - 1].EndHeading, geometry.Primitives[i].StartHeading) < 1e-6);
    }

    [Fact]
    public void Non_parallel_ends_connect_smoothly_and_deterministically()
    {
        var tool = Tool();
        var east = tool.Network.Edge(Lay(tool, new Pose(W(500, 1000), 0), new Pose(W(1000, 1000), 0)));
        var south = tool.Network.Edge(Lay(tool, new Pose(W(1400, 1400), Math.PI / 2), new Pose(W(1400, 1900), Math.PI / 2)));
        var a = new FreeEndAnchor(east.To);
        var b = new FreeEndAnchor(south.From);

        var first = tool.Preview(a, b);
        var second = tool.Preview(a, b);
        Assert.True(first.CanConfirm);
        // Наибольший радиус среди вариантов без лишнего объезда: ровно четверть окружности R = 400 м.
        Assert.Equal(400, first.Current!.RadiusM);
        Assert.Single(first.Current.Candidate.Geometry.Primitives);
        Assert.Equal(first.Current.Candidate.Family, second.Current!.Candidate.Family);
        Assert.Equal(first.Current.LengthM, second.Current.LengthM, 12);
        AssertSmooth(first.Current.Candidate.Geometry, new Pose(W(1000, 1000), 0), new Pose(W(1400, 1400), Math.PI / 2));

        var result = first.Confirm();
        Assert.True(result.Committed);
        // Три пути одной линией: от начала до конца — без разворота.
        var route = RailRouter.Find(tool.Network,
            new TrackPosition(new DirectedEdge(east.Id, true), 0), new TrackPosition(new DirectedEdge(south.Id, true), 500));
        Assert.NotNull(route);
        Assert.Equal(500 + 200 * Math.PI + 500, route.LengthM, 6);
    }

    [Fact]
    public void Wheel_switches_radius_without_touching_the_world_and_cancel_leaves_nothing()
    {
        var tool = Tool();
        var east = tool.Network.Edge(Lay(tool, new Pose(W(500, 1000), 0), new Pose(W(1000, 1000), 0)));
        var south = tool.Network.Edge(Lay(tool, new Pose(W(1400, 1400), Math.PI / 2), new Pose(W(1400, 1900), Math.PI / 2)));
        var revision = (tool.Network.Revision, tool.Network.World.Revision, tool.Network.World.Index.CellCount);

        var connection = tool.Preview(new FreeEndAnchor(east.To), new FreeEndAnchor(south.From));
        Assert.True(connection.Radii.Count > 1);
        var radii = new List<double>();
        connection.ScrollRadius(-10);
        for (var i = 0; i < connection.Radii.Count; i++, connection.ScrollRadius(+1))
            radii.Add(connection.Current!.RadiusM);
        Assert.Equal(radii.Order(), radii);
        Assert.All(radii, r => Assert.True(r >= 200));
        connection.NextVariant();
        Assert.Equal(revision, (tool.Network.Revision, tool.Network.World.Revision, tool.Network.World.Index.CellCount));

        // Отмена — просто не подтверждать: в мире ничего не осталось.
        Assert.Equal(4, tool.Network.FreeEnds.Count());
    }

    [Fact]
    public void Impossible_connection_is_refused_without_partial_build()
    {
        var tool = Tool();
        var east = tool.Network.Edge(Lay(tool, new Pose(W(500, 1000), 0), new Pose(W(1000, 1000), 0)));
        var edges = tool.Network.Edges.Count();
        // Разворот на 50 м: с радиусом не меньше 200 м и дугами до 90° не построить.
        var connection = tool.Preview(new FreeEndAnchor(east.To), new NewEndAnchor(new Pose(W(1000, 1050), Math.PI)));
        Assert.False(connection.CanConfirm);
        Assert.NotEmpty(connection.Rejections);
        Assert.Throws<InvalidOperationException>(() => connection.Confirm());
        Assert.Equal(edges, tool.Network.Edges.Count());
        Assert.Equal(1, tool.Network.World.Revision);
    }

    [Fact]
    public void Switch_on_a_straight_keeps_the_straight_run_and_both_directions()
    {
        var tool = Tool();
        var main = Lay(tool, new Pose(W(1000, 1000), 0), new Pose(W(2500, 1000), 0));
        // Ответвление на параллельный путь в 100 м сбоку.
        var connection = tool.Preview(new EdgePointAnchor(main, 300, AlongEdge: true), new NewEndAnchor(new Pose(W(2000, 1100), 0)));
        Assert.True(connection.CanConfirm, string.Join("; ", connection.Rejections));
        var result = connection.Confirm();
        Assert.True(result.Committed, string.Join("; ", result.Issues));

        var network = tool.Network;
        var junction = Assert.Single(network.Junctions);
        Assert.Equal(W(1300, 1000), network.Node(junction.Node).At);
        Assert.Equal(3, junction.ConflictZone.Count);
        Assert.All(junction.ConflictZone, z => Assert.True(z.ToM - z.FromM > 0));

        var parts = network.EdgesAt(junction.Node);
        var before = parts.Single(e => e.To == junction.Node);
        var straightOn = parts.Single(e => e.From == junction.Node && e.Geometry.IsStraight);
        var branch = parts.Single(e => e.From == junction.Node && !e.Geometry.IsStraight);
        var choices = network.Next(new DirectedEdge(before.Id, true)).ToList();
        Assert.Equal(2, choices.Count);
        Assert.Contains(new DirectedEdge(straightOn.Id, true), choices);
        Assert.Contains(new DirectedEdge(branch.Id, true), choices);
        // Обратно с обеих веток — на общий путь перед стрелкой.
        Assert.Equal([new DirectedEdge(before.Id, false)], network.Next(new DirectedEdge(straightOn.Id, false)));
        Assert.Equal([new DirectedEdge(before.Id, false)], network.Next(new DirectedEdge(branch.Id, false)));
        Assert.Equal(1500, before.LengthM + straightOn.LengthM, 9);
    }

    [Fact]
    public void Switch_into_a_curve_or_next_to_another_switch_is_refused()
    {
        var tool = Tool();
        var east = tool.Network.Edge(Lay(tool, new Pose(W(500, 1000), 0), new Pose(W(1000, 1000), 0)));
        var south = tool.Network.Edge(Lay(tool, new Pose(W(1400, 1400), Math.PI / 2), new Pose(W(1400, 1900), Math.PI / 2)));
        var curve = tool.Preview(new FreeEndAnchor(east.To), new FreeEndAnchor(south.From)).Confirm().Edges[0];

        var intoCurve = tool.Preview(new EdgePointAnchor(curve.Id, curve.LengthM / 2, true), new NewEndAnchor(new Pose(W(2000, 2000), 0)));
        Assert.False(intoCurve.CanConfirm);
        Assert.Contains(intoCurve.Rejections, r => r.Contains("кривую"));

        var main = Lay(tool, new Pose(W(5000, 5000), 0), new Pose(W(7000, 5000), 0));
        Assert.True(tool.Preview(new EdgePointAnchor(main, 300, true), new NewEndAnchor(new Pose(W(6000, 5100), 0))).Confirm().Committed);
        var node = Assert.Single(tool.Network.Junctions).Node;
        var trunk = tool.Network.EdgesAt(node).Single(e => e.From == node && e.Geometry.IsStraight);
        var crowded = tool.Preview(new EdgePointAnchor(trunk.Id, 5, true), new NewEndAnchor(new Pose(W(6500, 4900), 0)));
        Assert.False(crowded.CanConfirm);
    }

    [Fact]
    public void Crossing_an_independent_line_is_refused_on_one_level_and_allowed_on_another()
    {
        var tool = Tool();
        Lay(tool, new Pose(W(1000, 1000), 0), new Pose(W(2000, 1000), 0));
        var crossing = tool.Preview(new NewEndAnchor(new Pose(W(1500, 500), Math.PI / 2)), new NewEndAnchor(new Pose(W(1500, 1500), Math.PI / 2)));
        Assert.False(crossing.CanConfirm);
        Lay(tool, new Pose(W(1500, 500), Math.PI / 2), new Pose(W(1500, 1500), Math.PI / 2), level: 1);
        Assert.Equal(2, tool.Network.Edges.Count());
    }

    [Fact]
    public void Parallel_line_five_metres_away_is_allowed_closer_is_not()
    {
        var tool = Tool();
        Lay(tool, new Pose(W(1000, 1002.5), 0), new Pose(W(2000, 1002.5), 0));
        Lay(tool, new Pose(W(1000, 1007.5), 0), new Pose(W(2000, 1007.5), 0));
        var close = tool.Preview(new NewEndAnchor(new Pose(W(1000, 1011), 0)), new NewEndAnchor(new Pose(W(2000, 1011), 0)));
        Assert.False(close.CanConfirm);
    }
}
