using Mintrans.Core.Grid;
using Mintrans.World.Sites;

namespace Mintrans.World.Tests;

public class SiteTests
{
    private static NewSiteRequest New(string key, GridRect area) => new(key, "steel_plant", key, area);

    private static AttachPartRequest Part(string key, GridRect area) => new(key, "rail_station", area);

    [Fact]
    public void Placed_site_occupies_its_base_area()
    {
        var map = TestWorld.Map();
        var site = SitePlacement.Basic().Place(map, New("plant", new GridRect(2, 2, 3, 2)));

        Assert.False(site.Id.IsNone);
        Assert.Equal(site, map.Sites.At(new GridPos(4, 3)));
        Assert.Null(map.Sites.At(new GridPos(5, 3)));
        Assert.Equal(site, map.Sites.GetByKey("plant"));
    }

    [Fact]
    public void Attached_part_extends_site_into_non_rectangular_shape()
    {
        // Завод 2×2 + полоса станции сбоку = площадка 2×3 (пример из docs/10), затем ещё модуль углом.
        var map = TestWorld.Map();
        var placement = SitePlacement.Basic();
        var site = placement.Place(map, New("plant", new GridRect(4, 4, 2, 2)));
        placement.Attach(map, Part("plant", new GridRect(4, 6, 2, 1)));
        placement.Attach(map, Part("plant", new GridRect(6, 6, 1, 1)));

        Assert.Equal(7, site.Cells().Count());
        Assert.Equal(new GridRect(4, 4, 3, 3), site.Bounds);
        Assert.False(site.Contains(new GridPos(6, 4)));
        Assert.Equal(site, map.Sites.At(new GridPos(6, 6)));
        Assert.Equal("rail_station", site.PartAt(new GridPos(5, 6))!.Kind.Value);
        Assert.Equal(Site.BasePart, site.PartAt(new GridPos(4, 4))!.Kind);
    }

    [Fact]
    public void Part_touching_only_by_corner_or_far_away_is_rejected()
    {
        var map = TestWorld.Map();
        var placement = SitePlacement.Basic();
        placement.Place(map, New("plant", new GridRect(4, 4, 2, 2)));

        Assert.Contains(placement.Check(map, Part("plant", new GridRect(6, 6, 1, 1))), p => p.Code == "not_attached");
        Assert.Contains(placement.Check(map, Part("plant", new GridRect(10, 10, 2, 1))), p => p.Code == "not_attached");
        Assert.Throws<SitePlacementException>(() => placement.Attach(map, Part("plant", new GridRect(10, 10, 2, 1))));
    }

    [Fact]
    public void Part_cannot_take_cells_of_another_site()
    {
        var map = TestWorld.Map();
        var placement = SitePlacement.Basic();
        placement.Place(map, New("a", new GridRect(2, 2, 2, 2)));
        placement.Place(map, New("b", new GridRect(2, 5, 2, 2)));

        Assert.Contains(placement.Check(map, Part("a", new GridRect(2, 4, 2, 2))), p => p.Code == "cells_occupied");
    }

    [Fact]
    public void Overlapping_site_is_rejected_with_reason()
    {
        var map = TestWorld.Map();
        var placement = SitePlacement.Basic();
        placement.Place(map, New("a", new GridRect(2, 2, 3, 3)));

        var problems = placement.Check(map, New("b", new GridRect(4, 4, 3, 3)));
        Assert.Contains(problems, p => p.Code == "cells_occupied");
        Assert.Throws<SitePlacementException>(() => placement.Place(map, New("b", new GridRect(4, 4, 3, 3))));
        Assert.Single(map.Sites.All);
    }

    [Fact]
    public void Site_outside_map_is_rejected()
    {
        var map = TestWorld.Map(8, 8);
        var problems = SitePlacement.Basic().Check(map, New("a", new GridRect(6, 6, 3, 3)));
        Assert.Contains(problems, p => p.Code == "outside_map");
    }

    [Fact]
    public void Extra_rules_plug_in_without_changing_placement()
    {
        var map = TestWorld.Map();
        map.Terrain["water"].Set(new GridPos(3, 3), "river");
        var placement = new SitePlacement([new InsideMapRule(), new FreeCellsRule(), new NoWaterRule()]);

        var problems = placement.Check(map, New("a", new GridRect(2, 2, 3, 3)));
        Assert.Contains(problems, p => p.Code == "on_water");
    }

    /// <summary>Пример будущего правила: оно пишется отдельно и просто добавляется в список.</summary>
    private sealed class NoWaterRule : IPlacementRule
    {
        public PlacementProblem? Check(WorldMap map, IPlacementRequest request) =>
            request.Area.Cells().Any(c => !map.Terrain["water"].Is(c, "none"))
                ? new PlacementProblem("on_water", "Нельзя строить на воде.")
                : null;
    }
}
