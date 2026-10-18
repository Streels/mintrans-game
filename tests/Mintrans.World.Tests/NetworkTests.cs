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
    public void Crossing_rails_do_not_allow_turning()
    {
        var rail = TestWorld.Map().Rail();
        rail.LayPath([P(0, 2), P(1, 2), P(2, 2)]);
        rail.LayPath([P(1, 1), P(1, 2), P(1, 3)]);

        Assert.Equal(TrackPieces.EastWest | TrackPieces.NorthSouth, rail.PiecesAt(P(1, 2)));
        Assert.False(rail.Connects(P(1, 2), Direction.West, Direction.South));
        Assert.False(NetworkQueries.AreConnected(rail, [P(0, 2)], [P(1, 3)]));
        Assert.True(NetworkQueries.AreConnected(rail, [P(0, 2)], [P(2, 2)]));
    }

    [Fact]
    public void Rail_bend_lays_a_corner_piece()
    {
        var rail = TestWorld.Map().Rail();
        rail.LayPath([P(0, 0), P(1, 0), P(1, 1)]);

        Assert.Equal(TrackPieces.EastWest, rail.PiecesAt(P(0, 0)));
        Assert.Equal(TrackPieces.SouthWest, rail.PiecesAt(P(1, 0)));
        Assert.Equal(TrackPieces.NorthSouth, rail.PiecesAt(P(1, 1)));
    }

    [Fact]
    public void Diagonal_is_a_staircase_of_corner_pieces()
    {
        // Диагональ, как в OpenTTD: каждая клетка лесенки получает угловой кусок.
        var rail = TestWorld.Map().Rail();
        rail.LayPath([P(0, 0), P(1, 0), P(1, 1), P(2, 1), P(2, 2), P(3, 2)]);

        Assert.Equal(TrackPieces.SouthWest, rail.PiecesAt(P(1, 0)));
        Assert.Equal(TrackPieces.NorthEast, rail.PiecesAt(P(1, 1)));
        Assert.Equal(TrackPieces.SouthWest, rail.PiecesAt(P(2, 1)));
        Assert.Equal(TrackPieces.NorthEast, rail.PiecesAt(P(2, 2)));
        Assert.True(NetworkQueries.AreConnected(rail, [P(0, 0)], [P(3, 2)]));
    }

    [Fact]
    public void New_rail_from_existing_dead_end_turns_into_it()
    {
        // Первая протяжка заканчивается в (5, 2), вторая начинается там же и уходит на юг:
        // в (5, 2) должен получиться поворот, а не крест.
        var rail = TestWorld.Map().Rail();
        rail.LayPath([P(3, 2), P(4, 2), P(5, 2)]);
        rail.LayPath([P(5, 2), P(5, 3), P(5, 4)]);

        Assert.Equal(TrackPieces.EastWest | TrackPieces.SouthWest, rail.PiecesAt(P(5, 2)));
        Assert.True(NetworkQueries.AreConnected(rail, [P(3, 2)], [P(5, 4)]));
    }

    [Fact]
    public void New_rail_next_to_existing_end_joins_it()
    {
        var rail = TestWorld.Map().Rail();
        rail.LayPath([P(1, 1), P(2, 1), P(3, 1)]);
        // Начинаем в клетке сразу за концом (на неё он смотрит) и уходим на юг — путь поворачивает к концу.
        rail.LayPath([P(4, 1), P(4, 2), P(4, 3)]);

        Assert.Equal(TrackPieces.SouthWest, rail.PiecesAt(P(4, 1)));
        Assert.True(NetworkQueries.AreConnected(rail, [P(1, 1)], [P(4, 3)]));
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
    public void Path_in_open_field_does_not_snap()
    {
        var rail = TestWorld.Map().Rail();
        rail.LayPath([P(1, 1), P(1, 2)]);
        rail.LayPath([P(5, 5), P(6, 5)]);
        Assert.Equal(TrackPieces.EastWest, rail.PiecesAt(P(5, 5)));
    }

    [Fact]
    public void Cells_touching_only_by_corner_are_not_linked()
    {
        var rail = TestWorld.Map().Rail();
        Assert.Throws<ArgumentException>(() => rail.LayPath([P(0, 0), P(1, 1)]));
    }

    [Fact]
    public void Path_outside_map_or_turning_back_is_rejected()
    {
        var road = TestWorld.Map(4, 4).Road();
        Assert.Throws<ArgumentOutOfRangeException>(() => road.LayPath([P(3, 0), P(4, 0)]));
        Assert.Throws<ArgumentException>(() => road.LayPath([P(0, 0), P(1, 0), P(0, 0)]));
    }

    [Fact]
    public void Networks_are_independent()
    {
        var map = TestWorld.Map();
        map.Road().LayPath([P(1, 1), P(1, 2)]);
        Assert.False(map.Rail().HasAny(P(1, 1)));
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
        map.Rail().LayPath([P(5, 5), P(6, 5), P(6, 6)]);

        Assert.Equal(2, map.Road().Strokes().Count());
        Assert.Contains(new NetworkStroke(P(6, 5), Direction.South, Direction.West), map.Rail().Strokes());
    }
}
