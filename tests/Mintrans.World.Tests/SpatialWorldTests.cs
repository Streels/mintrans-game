using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;
using Mintrans.World.Spatial;

namespace Mintrans.World.Tests;

/// <summary>Пространственный мир: разреженный индекс, транзакции, местность (критерии готовности docs/02в, раздел 9).</summary>
public class SpatialWorldTests
{
    private static WorldPoint W(double x, double y) => new(x, y);

    private static SpatialWorld Prototype(ITerrainField? terrain = null, IEnumerable<ITerrainRule>? rules = null) =>
        new(WorldBounds.Sized(40_000, 40_000), terrain ?? new FlatTerrain(), terrainRules: rules);

    private static CorridorShape Track(WorldPoint a, WorldPoint b) => new(new PathGeometry(new StraightPrimitive(a, b)), 2.0);

    [Fact]
    public void Empty_world_has_no_cells_and_a_kilometre_of_track_only_fills_its_corridor()
    {
        var world = Prototype();
        Assert.Equal(0, world.Index.ChunkCount);
        Assert.Equal(0, world.Index.CellCount);

        var change = world.Begin();
        change.Add("rail", 0, Track(W(20_002.5, 20_002.5), W(21_002.5, 20_002.5)));
        Assert.True(world.Commit(change).Committed);
        // Коридор шириной 4 м с осью посреди ряда клеток — один ряд из 200 клеток (плюс крайние).
        Assert.InRange(world.Index.CellCount, 200, 202);
        Assert.InRange(world.Index.ChunkCount, 4, 5);
    }

    [Fact]
    public void Preview_does_not_touch_the_world_and_replace_moves_index_entries()
    {
        var world = Prototype();
        var change = world.Begin();
        var id = change.Add("rail", 0, Track(W(100, 102.5), W(200, 102.5)));
        var preview = world.Preview(change);
        Assert.True(preview.IsValid);
        Assert.NotEmpty(preview.Cells[id]);
        Assert.Equal(0, world.Count);
        Assert.Equal(0, world.Index.CellCount);
        Assert.Equal(0, world.Revision);

        world.Commit(change);
        var oldCells = world.Index.CellsOf(id).ToList();

        var replace = world.Begin();
        replace.Replace(id, Track(W(1000, 1002.5), W(1100, 1002.5)));
        var result = world.Commit(replace);
        Assert.True(result.Committed);
        Assert.Equal(1, world.Count);
        Assert.All(oldCells, c => Assert.Empty(world.Index.At(c)));
        Assert.Contains(id, world.Index.At(SpatialScale.ToCell(W(1050, 1002.5))));
        Assert.Contains(SpatialScale.ToChunk(oldCells[0]), result.TouchedChunks);
    }

    [Fact]
    public void Removing_an_object_clears_empty_cells_and_chunks_but_keeps_geometry_of_others()
    {
        var world = Prototype();
        var change = world.Begin();
        var a = change.Add("rail", 0, Track(W(10, 12.5), W(900, 12.5)));
        var b = change.Add("rail", 0, Track(W(10, 17.5), W(30, 17.5)));
        world.Commit(change);
        var geometry = world.Get(b).Shape;

        var remove = world.Begin();
        remove.Remove(a);
        world.Commit(remove);
        Assert.False(world.Contains(a));
        Assert.Equal(1, world.Index.ChunkCount);
        Assert.Same(geometry, world.Get(b).Shape);
        Assert.Equal(world.Get(b).Shape.TouchedCells().Count, world.Index.CellCount);
    }

    [Fact]
    public void Parallel_tracks_five_metres_apart_share_cells_without_conflict()
    {
        var world = Prototype();
        var first = world.Begin();
        first.Add("rail", 0, Track(W(10, 4), W(100, 4)));
        world.Commit(first);

        var parallel = world.Begin();
        parallel.Add("rail", 0, Track(W(10, 9), W(100, 9)));
        Assert.True(world.Preview(parallel).IsValid);

        var tooClose = world.Begin();
        tooClose.Add("rail", 0, Track(W(10, 7), W(100, 7)));
        var preview = world.Preview(tooClose);
        Assert.False(preview.IsValid);
        Assert.Equal(new ObjectId(1), preview.Issues[0].Other);
    }

    [Fact]
    public void Crossing_on_another_level_is_allowed_on_the_same_level_is_not()
    {
        var world = Prototype();
        var ground = world.Begin();
        ground.Add("rail", 0, Track(W(10, 50), W(100, 50)));
        world.Commit(ground);

        var cross = world.Begin();
        cross.Add("rail", 0, Track(W(50, 10), W(50, 100)));
        Assert.False(world.Preview(cross).IsValid);

        var bridge = world.Begin();
        bridge.Add("rail", 1, Track(W(50, 10), W(50, 100)));
        Assert.True(world.Commit(bridge).Committed);
        Assert.Equal(2, world.At(SpatialScale.ToCell(W(50, 50))).Count());
        Assert.Single(world.At(SpatialScale.ToCell(W(50, 50)), level: 1));
    }

    [Fact]
    public void Terrain_is_read_where_no_cells_ever_existed()
    {
        var bounds = WorldBounds.Sized(40_000, 40_000);
        var terrain = new GridTerrain(bounds, 100);
        var water = new TypeId("water");
        var (x, y) = terrain.CellOf(W(30_050, 30_050));
        terrain.SetKind(x, y, water, "river");
        terrain.SetNodeHeight(x, y, 12);
        var world = Prototype(terrain, [new ForbiddenTerrainRule(water, [new TypeId("river")])]);

        Assert.Equal(0, world.Index.CellCount);
        Assert.Equal(new TypeId("river"), world.Terrain.Sample(W(30_010, 30_090)).KindOf(water));
        Assert.Equal(12, world.Terrain.Sample(W(30_000, 30_000)).HeightM, 9);

        var change = world.Begin();
        change.Add("rail", 0, Track(W(29_950, 30_050), W(30_150, 30_050)));
        Assert.Contains(world.Preview(change).Issues, i => i.Reason.Contains("river"));

        var bridge = world.Begin();
        bridge.Add("rail", 1, Track(W(29_950, 30_050), W(30_150, 30_050)));
        Assert.True(world.Preview(bridge).IsValid);
    }

    [Fact]
    public void Commit_is_refused_after_world_changed_and_outside_bounds()
    {
        var world = Prototype();
        var late = world.Begin();
        late.Add("rail", 0, Track(W(10, 50), W(100, 50)));
        var other = world.Begin();
        other.Add("rail", 0, Track(W(10, 500), W(100, 500)));
        world.Commit(other);
        Assert.False(world.Commit(late).Committed);
        Assert.Equal(1, world.Count);

        var outside = world.Begin();
        outside.Add("rail", 0, Track(W(39_990, 50), W(40_100, 50)));
        Assert.Contains(world.Preview(outside).Issues, i => i.Reason.Contains("границу"));
    }

    [Fact]
    public void Index_rebuilds_from_geometry()
    {
        var world = Prototype();
        var change = world.Begin();
        change.Add("rail", 0, new CorridorShape(new PathGeometry(new ArcPrimitive(W(500, 700), 200, -Math.PI / 2, 1.2)), 2));
        change.Add("building", 0, AreaShape.OfCells(new GridRect(10, 10, 4, 3)));
        world.Commit(change);
        var before = world.Objects.ToDictionary(o => o.Id, o => world.Index.CellsOf(o.Id).OrderBy(c => c, GridPosOrder.Instance).ToList());

        world.RebuildIndex();
        foreach (var (id, cells) in before)
            Assert.Equal(cells, world.Index.CellsOf(id).OrderBy(c => c, GridPosOrder.Instance));
        Assert.Equal(12, world.Index.CellsOf(new ObjectId(2)).Count);
    }

    [Fact]
    public void World_grows_to_hundred_kilometres_without_moving_existing_objects()
    {
        var world = Prototype();
        var change = world.Begin();
        var id = change.Add("rail", 0, Track(W(39_000, 20_002.5), W(39_900, 20_002.5)));
        world.Commit(change);
        var cells = world.Index.CellsOf(id).ToList();

        var beyond = world.Begin();
        beyond.Add("rail", 0, Track(W(39_900, 30_002.5), W(99_000, 30_002.5)));
        Assert.False(world.Preview(beyond).IsValid);

        world.ExpandBounds(WorldBounds.Sized(100_000, 100_000));
        Assert.Equal(cells, world.Index.CellsOf(id));
        var grown = world.Begin();
        grown.Add("rail", 0, Track(W(39_900, 30_002.5), W(99_000, 30_002.5)));
        Assert.True(world.Commit(grown).Committed);
        // 59 км пути — это записи только вдоль коридора, а не 400 млн клеток карты.
        Assert.InRange(world.Index.CellCount, 180 + 11_820, 180 + 11_830);
        Assert.Throws<ArgumentException>(() => world.ExpandBounds(WorldBounds.Sized(50_000, 50_000)));
    }
}
