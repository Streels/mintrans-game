using Mintrans.Core.Grid;
using Mintrans.World.Networks;

namespace Mintrans.World.Tests;

/// <summary>Геометрия железнодорожной сети по модели OpenTTD (docs/10б): конфигурации клетки, мосты, сигналы.</summary>
public class RailGeometryTests
{
    private static GridPos P(int x, int y) => new(x, y);

    [Fact]
    public void Two_straights_crossing_is_not_a_valid_cell_but_switches_and_parallel_curves_are()
    {
        Assert.False((TrackPieces.NorthSouth | TrackPieces.EastWest).IsValid());
        Assert.True((TrackPieces.EastWest | TrackPieces.SouthWest).IsValid());
        Assert.True((TrackPieces.EastWest | TrackPieces.SouthWest).IsJunction());
        // Параллельные угловые куски — два независимых пути в одной клетке, не стрелка.
        var parallel = TrackPieces.NorthEast | TrackPieces.SouthWest;
        Assert.True(parallel.IsValid());
        Assert.False(parallel.IsJunction());
        Assert.False(Tracks.Overlap(TrackPieces.NorthEast, TrackPieces.SouthWest));
        Assert.True(Tracks.Overlap(TrackPieces.NorthEast, TrackPieces.NorthWest));
        Assert.True(Tracks.Overlap(TrackPieces.EastWest, TrackPieces.EastWest));
    }

    [Fact]
    public void Bridge_connects_only_its_heads_and_leaves_the_ground_below_independent()
    {
        var map = TestWorld.Map();
        var rail = map.Rail();
        var bridge = rail.AddBridge("rail_bridge", P(3, 5), P(7, 5));
        rail.LayPath([P(1, 5), P(2, 5)]);
        rail.LayPath([P(8, 5), P(9, 5)]);
        // Поперечная линия под пролётом.
        rail.LayPath([P(5, 2), P(5, 3), P(5, 4), P(5, 5), P(5, 6), P(5, 7)]);

        Assert.Equal([P(4, 5), P(5, 5), P(6, 5)], bridge.Span());
        Assert.True(NetworkQueries.AreConnected(rail, [P(1, 5)], [P(9, 5)]));
        Assert.True(NetworkQueries.AreConnected(rail, [P(5, 2)], [P(5, 7)]));
        Assert.False(NetworkQueries.AreConnected(rail, [P(1, 5)], [P(5, 2)]));
        // С опоры к пролёту — на мост; с земли на опору со стороны пролёта не попасть.
        Assert.Equal(new NetworkLink(P(4, 5), Direction.West, true), rail.Follow(P(3, 5), Direction.East));
        Assert.Equal(new NetworkLink(P(7, 5), Direction.West, false), rail.Follow(P(6, 5), Direction.East, elevated: true));
        Assert.Null(rail.Follow(P(4, 5), Direction.West));
    }

    [Fact]
    public void Path_snaps_to_bridge_head_only_from_behind()
    {
        var map = TestWorld.Map();
        var road = map.Road();
        road.AddBridge("road_bridge", P(3, 5), P(7, 5));
        // Дорога, подведённая к опоре сбоку, не стыкуется с ней — у опоры только тыльная сторона.
        road.LayPath([P(3, 8), P(3, 7), P(3, 6)]);
        Assert.Equal(DirectionMask.East | DirectionMask.West, road.EdgesAt(P(3, 5)));
        Assert.False(road.EdgesAt(P(3, 6)).Has(Direction.North));
    }

    [Fact]
    public void Removing_a_head_removes_the_whole_bridge()
    {
        var map = TestWorld.Map();
        var rail = map.Rail();
        rail.AddBridge("rail_bridge", P(3, 5), P(7, 5));
        rail.ClearCell(P(7, 5));
        Assert.Empty(rail.Bridges);
        Assert.False(rail.HasAny(P(3, 5)));
    }

    [Fact]
    public void Signal_sits_on_one_piece_and_goes_away_with_the_track()
    {
        var map = TestWorld.Map();
        var rail = map.Rail();
        rail.LayPath([P(1, 1), P(2, 1), P(3, 1)]);
        var signal = new Signal(P(2, 1), TrackPieces.EastWest, "block_signal", SignalLogic.Block, DirectionMask.East);
        rail.SetSignal(signal);

        Assert.True(signal.Controls(Direction.East));
        Assert.False(signal.Controls(Direction.West));
        Assert.Same(signal, rail.SignalOn(P(2, 1), TrackPieces.EastWest));
        Assert.Throws<ArgumentException>(() =>
            rail.SetSignal(signal with { Faces = DirectionMask.North }));

        var revision = rail.Revision;
        rail.ClearCell(P(2, 1));
        Assert.Empty(rail.Signals);
        Assert.True(rail.Revision > revision);
    }

    [Fact]
    public void Train_piece_is_chosen_from_entry_and_exit()
    {
        var map = TestWorld.Map();
        var rail = map.Rail();
        rail.AddPiece(P(4, 4), TrackPieces.EastWest | TrackPieces.SouthWest);
        Assert.Equal(TrackPieces.SouthWest, rail.PieceFor(P(4, 4), Direction.South, Direction.West));
        Assert.Equal(TrackPieces.EastWest, rail.PieceFor(P(4, 4), Direction.East, null));
        Assert.Equal(TrackPieces.SouthWest, rail.PieceFor(P(4, 4), null, Direction.South));
    }
}
