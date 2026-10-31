using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;

namespace Mintrans.Core.Tests;

/// <summary>Мировые координаты, клетки, чанки и метрическая геометрия (docs/02в, docs/10в).</summary>
public class SpatialTests
{
    private static WorldPoint W(double x, double y) => new(x, y);

    [Fact]
    public void ToCell_uses_floor_on_borders_and_negative_coordinates()
    {
        Assert.Equal(new GridPos(0, 0), SpatialScale.ToCell(W(0, 0)));
        Assert.Equal(new GridPos(0, 0), SpatialScale.ToCell(W(4.9999, 4.9999)));
        Assert.Equal(new GridPos(1, 1), SpatialScale.ToCell(W(5, 5)));
        Assert.Equal(new GridPos(-1, -1), SpatialScale.ToCell(W(-0.001, -5)));
        Assert.Equal(new GridPos(-2, 0), SpatialScale.ToCell(W(-5.001, 0.5)));
    }

    [Fact]
    public void Chunks_share_one_world_origin()
    {
        Assert.Equal(new ChunkPos(0, 0), SpatialScale.ToChunk(new GridPos(63, 0)));
        Assert.Equal(new ChunkPos(1, 0), SpatialScale.ToChunk(new GridPos(64, 0)));
        Assert.Equal(new ChunkPos(-1, -1), SpatialScale.ToChunk(new GridPos(-1, -64)));
        Assert.Equal(new ChunkPos(-2, 0), SpatialScale.ToChunk(new GridPos(-65, 0)));
        foreach (var cell in new[] { new GridPos(-65, 7), new GridPos(0, -1), new GridPos(130, 64) })
            Assert.Equal(cell, SpatialScale.FromLocal(SpatialScale.ToChunk(cell), SpatialScale.ToLocal(cell)));
        Assert.Equal(320, SpatialScale.ChunkBounds(new ChunkPos(0, 0)).Width);
    }

    [Fact]
    public void Arc_has_exact_length_tangents_and_bounds()
    {
        // Поворот направо на 90° радиусом 200 м: из (0, 0) на восток в (200, 200) на юг.
        var arc = new ArcPrimitive(W(0, 200), 200, -Math.PI / 2, Math.PI / 2);
        Assert.True(arc.Start.ApproxEquals(W(0, 0), 1e-9));
        Assert.True(arc.End.ApproxEquals(W(200, 200), 1e-9));
        Assert.Equal(0, arc.StartHeading, 9);
        Assert.Equal(Math.PI / 2, arc.EndHeading, 9);
        Assert.Equal(100 * Math.PI, arc.Length, 9);
        var bounds = arc.Bounds;
        Assert.Equal(0, bounds.MinX, 9);
        Assert.Equal(0, bounds.MinY, 9);
        Assert.Equal(200, bounds.MaxX, 9);
        Assert.Equal(200, bounds.MaxY, 9);

        var reversed = arc.Reversed();
        Assert.True(reversed.Start.ApproxEquals(arc.End, 1e-9));
        Assert.Equal(Angles.Reverse(arc.EndHeading), reversed.StartHeading, 9);
    }

    [Fact]
    public void Path_rejects_kinks_and_splits_without_losing_length()
    {
        Assert.Throws<ArgumentException>(() => new PathGeometry(
            new StraightPrimitive(W(0, 0), W(10, 0)), new StraightPrimitive(W(10, 0), W(10, 10))));

        var path = new PathGeometry(
            new StraightPrimitive(W(-100, 0), W(0, 0)),
            new ArcPrimitive(W(0, 200), 200, -Math.PI / 2, Math.PI / 4));
        var (before, after) = path.Split(150);
        Assert.Equal(path.Length, before.Length + after.Length, 9);
        Assert.True(before.End.ApproxEquals(after.Start, 1e-9));
        Assert.Equal(before.EndHeading, after.StartHeading, 9);
        Assert.True(path.PointAt(150).ApproxEquals(before.End, 1e-9));
    }

    [Fact]
    public void Curve_off_cell_centers_marks_every_touched_cell_across_chunk_border()
    {
        // Дуга проходит через границу чанков x = 320 м и не через центры клеток.
        var axis = new PathGeometry(new ArcPrimitive(W(300.7, 513.3), 200, -Math.PI / 2 - 0.3, 0.6));
        var corridor = new CorridorShape(axis, 2.0);
        var cells = corridor.TouchedCells();
        Assert.Contains(cells, c => SpatialScale.ToChunk(c).X == 0);
        Assert.Contains(cells, c => SpatialScale.ToChunk(c).X == 1);

        // Каждая точка габарита попадает в отмеченную клетку.
        for (var s = 0.0; s <= axis.Length; s += 0.5)
        {
            var p = axis.PointAt(s);
            var normal = WorldPoint.FromAngle(axis.HeadingAt(s)).Perpendicular(1);
            for (var offset = -1.99; offset <= 1.99; offset += 0.33)
                Assert.Contains(SpatialScale.ToCell(p + normal * offset), cells);
        }
        // И лишних клеток далеко от оси нет.
        Assert.All(cells, c => Assert.True(Distances.PointRect(axis.PointAt(axis.Length / 2), SpatialScale.CellBounds(c)) < 70));
    }

    [Fact]
    public void Shared_index_cell_is_not_a_conflict_until_shapes_overlap()
    {
        Assert.False(ShapeConflicts.Conflict(Line(4), Line(9)));
        Assert.Contains(Line(4).TouchedCells(), cell => Line(9).TouchedCells().Contains(cell));
        Assert.True(ShapeConflicts.Conflict(Line(4), Line(7)));

        var building = AreaShape.OfCells(new GridRect(0, 2, 4, 4));
        Assert.False(ShapeConflicts.Conflict(building, AreaShape.OfCells(new GridRect(4, 2, 2, 2))));
        Assert.True(ShapeConflicts.Conflict(building, Line(12)));
        Assert.False(ShapeConflicts.Conflict(building, Line(7)));
    }

    private static CorridorShape Line(double y) =>
        new(new PathGeometry(new StraightPrimitive(W(0, y), W(100, y))), 2.0);

    [Fact]
    public void Sprite_direction_is_nearest_of_sixteen()
    {
        Assert.Equal(0, SpriteDirections.Index(0));
        Assert.Equal(0, SpriteDirections.Index(Angles.ToRadians(11)));
        Assert.Equal(1, SpriteDirections.Index(Angles.ToRadians(-12)));
        Assert.Equal(4, SpriteDirections.Index(-Math.PI / 2));
        Assert.Equal(12, SpriteDirections.Index(Math.PI / 2));
        Assert.Equal(8, SpriteDirections.Index(Math.PI));
        for (var i = 0; i < SpriteDirections.Count; i++)
            Assert.Equal(i, SpriteDirections.Index(SpriteDirections.Heading(i)));
    }
}
