using Mintrans.Core.Grid;

namespace Mintrans.Core.Tests;

public class GridTests
{
    [Fact]
    public void Step_and_opposite_return_to_start()
    {
        var start = new GridPos(5, 5);
        foreach (var direction in Directions.All)
            Assert.Equal(start, start.Step(direction).Step(direction.Opposite()));
    }

    [Fact]
    public void Only_four_sides_exist()
    {
        Assert.Equal(4, Directions.All.Count);
        Assert.True(Direction.North.IsOpposite(Direction.South));
        Assert.False(Direction.North.IsOpposite(Direction.East));
    }

    [Fact]
    public void Between_finds_side_only_for_cells_sharing_it()
    {
        Assert.Equal(Direction.East, Directions.Between(new GridPos(1, 1), new GridPos(2, 1)));
        Assert.Equal(Direction.North, Directions.Between(new GridPos(1, 1), new GridPos(1, 0)));
        // Клетки, касающиеся только углом, не соседи.
        Assert.Null(Directions.Between(new GridPos(1, 1), new GridPos(2, 2)));
        Assert.Null(Directions.Between(new GridPos(1, 1), new GridPos(3, 1)));
        Assert.Null(Directions.Between(new GridPos(1, 1), new GridPos(1, 1)));
    }

    [Fact]
    public void Mask_enumerates_exactly_its_sides()
    {
        var mask = Direction.North.ToMask() | Direction.West.ToMask();
        Assert.Equal([Direction.North, Direction.West], mask.Enumerate());
        Assert.True(mask.Has(Direction.West));
        Assert.False(mask.Has(Direction.East));
        Assert.Equal(4, DirectionMask.All.Enumerate().Count());
    }

    [Fact]
    public void Rect_contains_and_intersects()
    {
        var rect = new GridRect(2, 3, 4, 5);
        Assert.True(rect.Contains(new GridPos(2, 3)));
        Assert.True(rect.Contains(new GridPos(5, 7)));
        Assert.False(rect.Contains(new GridPos(6, 3)));
        Assert.True(rect.Intersects(new GridRect(5, 7, 2, 2)));
        Assert.False(rect.Intersects(new GridRect(6, 3, 2, 2)));
        Assert.Equal(rect.Area, rect.Cells().Count());
    }

    [Fact]
    public void Rect_rejects_empty_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridRect(0, 0, 0, 3));
    }
}
