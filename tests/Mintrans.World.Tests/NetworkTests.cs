using Mintrans.Core.Grid;
using Mintrans.World.Networks;

namespace Mintrans.World.Tests;

public class NetworkTests
{
    private static GridPos P(int x, int y) => new(x, y);

    [Fact]
    public void Road_path_ends_are_dead_ends_and_middle_passes_through()
    {
        var road = TestWorld.Map().Road();
        road.LayPath([P(1, 1), P(2, 1), P(3, 1)]);

        Assert.Equal(DirectionMask.East, road.EdgesAt(P(1, 1)));
        Assert.Equal(DirectionMask.East | DirectionMask.West, road.EdgesAt(P(2, 1)));
        Assert.Equal(DirectionMask.West, road.EdgesAt(P(3, 1)));
        Assert.True(road.Connects(P(2, 1), Direction.West, Direction.East));
    }

    [Fact]
    public void Crossing_roads_form_a_junction()
    {
        var road = TestWorld.Map().Road();
        road.LayPath([P(0, 2), P(1, 2), P(2, 2)]);
        road.LayPath([P(1, 1), P(1, 2), P(1, 3)]);

        // На дорожном перекрёстке можно свернуть.
        Assert.True(road.Connects(P(1, 2), Direction.West, Direction.South));
        Assert.True(NetworkQueries.AreConnected(road, [P(0, 2)], [P(1, 3)]));
    }

    [Fact]
    public void Road_ending_next_to_road_connects()
    {
        var road = TestWorld.Map().Road();
        road.LayPath([P(1, 5), P(2, 5), P(3, 5)]);
        road.LayPath([P(4, 8), P(4, 7), P(4, 6), P(4, 5)]);
        Assert.True(NetworkQueries.AreConnected(road, [P(1, 5)], [P(4, 8)]));
    }

    [Fact]
    public void Cells_touching_only_by_corner_are_not_linked()
    {
        var road = TestWorld.Map().Road();
        Assert.Throws<ArgumentException>(() => road.LayPath([P(0, 0), P(1, 1)]));
    }

    [Fact]
    public void Path_outside_map_or_turning_back_is_rejected()
    {
        var road = TestWorld.Map(4, 4).Road();
        Assert.Throws<ArgumentOutOfRangeException>(() => road.LayPath([P(3, 0), P(4, 0)]));
        Assert.Throws<ArgumentException>(() => road.LayPath([P(0, 0), P(1, 0), P(0, 0)]));
    }

    [Fact]
    public void Half_built_link_does_not_connect()
    {
        // Дорога подходит к стороне, но у соседа дороги нет — проезда нет.
        var road = TestWorld.Map().Road();
        road.AddEdge(P(1, 1), Direction.East);
        road.AddEdge(P(1, 1), Direction.West);
        Assert.Equal([P(1, 1)], NetworkQueries.ReachableFrom(road, P(1, 1)));
    }

    [Fact]
    public void Strokes_describe_each_cell()
    {
        var map = TestWorld.Map();
        map.Road().LayPath([P(1, 1), P(2, 1)]);
        map.Road().LayPath([P(5, 5), P(6, 5), P(6, 6)]);

        Assert.Equal(6, map.Road().Strokes().Count());
        Assert.Contains(new NetworkStroke(P(6, 5), null, Direction.South), map.Road().Strokes());
    }
}
