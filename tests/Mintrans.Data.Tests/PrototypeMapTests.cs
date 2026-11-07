using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Networks;

namespace Mintrans.Data.Tests;

/// <summary>Проверяет тестовую карту против требований docs/01 и docs/02.</summary>
public class PrototypeMapTests
{
    private static readonly WorldMap Map = ContentLocator.Load().LoadMap("prototype").Map;

    [Fact]
    public void Has_city_two_mines_and_steel_plant()
    {
        var types = Map.Sites.All.Select(s => s.Type.Value).Order().ToList();
        Assert.Equal(["city", "coal_mine", "iron_mine", "steel_plant"], types);
    }

    [Fact]
    public void Map_is_forty_by_forty_kilometres_and_stored_sparsely()
    {
        // docs/01, docs/02в: 8 000×8 000 клеток по 5 м — логические адреса, а не плотный массив.
        Assert.Equal((8000, 8000), (Map.Width, Map.Height));
        Assert.Equal(40_000, Map.Spatial.Bounds.Width, 6);
        // Рельеф — на крупной сетке; клеточные слои заняты только там, где есть улицы и площадки.
        Assert.True(Map.Terrain.Step > 1);
        Assert.True(Map.Networks["road"].OccupiedCells().Count() < 20_000);
    }

    [Fact]
    public void Objects_are_far_apart()
    {
        // «Маленькая карта» — это мало объектов, а не короткие расстояния: не ближе полутора километров.
        const int minDistance = 300;
        var sites = Map.Sites.All;
        foreach (var a in sites)
        foreach (var b in sites.Where(b => b != a))
            Assert.True(a.Base.Center.ManhattanDistance(b.Base.Center) >= minDistance,
                $"{a.Key} и {b.Key} ближе {minDistance} клеток");
    }

    [Fact]
    public void City_has_connected_internal_streets()
    {
        var road = Map.Networks["road"];
        var city = Map.Sites.GetByKey("city");
        var streetCells = city.Cells().Where(road.HasAny).ToList();

        Assert.NotEmpty(streetCells);
        var reachable = NetworkQueries.ReachableFrom(road, streetCells[0]);
        Assert.All(streetCells, cell => Assert.Contains(cell, reachable));
        Assert.All(reachable, cell => Assert.True(city.Contains(cell), $"улица {cell} выходит за город"));
    }

    [Fact]
    public void Objects_start_isolated_without_external_networks()
    {
        foreach (var network in Map.Networks.Layers)
        foreach (var a in Map.Sites.All)
        foreach (var b in Map.Sites.All.Where(b => b != a))
            Assert.False(NetworkQueries.AreConnected(network, a.Cells(), b.Cells()),
                $"{a.Key} и {b.Key} уже связаны сетью {network.Type}");
        Assert.Empty(Map.RequireRails().Edges);
    }

    [Fact]
    public void Terrain_is_not_flat_and_sites_stand_on_level_dry_ground()
    {
        var coarse = Map.Terrain.Height.Coarse;
        var heights = coarse.NonDefault().Select(c => (int)c.Value).Append(coarse.Default).ToList();
        Assert.True(heights.Max() - heights.Min() >= 8, "рельеф слишком плоский");

        var water = Map.Terrain["water"];
        foreach (var site in Map.Sites.All)
        {
            var siteHeights = site.Cells().Select(c => Map.Terrain.Height[c]).Distinct().ToList();
            Assert.True(siteHeights.Count == 1, $"площадка {site.Key} не ровная");
            Assert.All(site.Cells(), c => Assert.True(water.Is(c, "none"), $"{site.Key} стоит на воде в {c}"));
        }
    }

    [Fact]
    public void Mines_stand_on_their_deposits()
    {
        var deposit = Map.Terrain["deposit"];
        Assert.True(deposit.Is(Map.Sites.GetByKey("coal_mine").Base.Center, "coal"));
        Assert.True(deposit.Is(Map.Sites.GetByKey("iron_mine").Base.Center, "iron_ore"));
    }

    [Fact]
    public void Water_does_not_split_the_map()
    {
        // Все объекты достижимы по суше. Обход — по ячейкам рельефа: вода хранится на крупной сетке.
        var water = Map.Terrain["water"].Cells;
        var none = Map.Terrain["water"].Category.Get("none").Index;
        var coarse = water.Coarse;
        var start = water.CoarseOf(Map.Sites.GetByKey("city").Base.Center);
        var visited = new HashSet<GridPos> { start };
        var queue = new Queue<GridPos>([start]);
        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            foreach (var direction in Directions.All)
            {
                var next = pos.Step(direction);
                if (coarse.Contains(next) && coarse[next] == none && visited.Add(next))
                    queue.Enqueue(next);
            }
        }
        Assert.All(Map.Sites.All, site => Assert.Contains(water.CoarseOf(site.Base.Center), visited));
    }

    [Fact]
    public void Same_file_builds_identical_map()
    {
        var again = ContentLocator.Load().LoadMap("prototype").Map;
        Assert.True(Map.Terrain.Height.Coarse.SameAs(again.Terrain.Height.Coarse));
        foreach (var layer in Map.Terrain.Layers)
            Assert.True(layer.Cells.Coarse.SameAs(again.Terrain[layer.Category.Id].Cells.Coarse));
        Assert.Equal(Map.Networks["road"].Strokes(), again.Networks["road"].Strokes());
        Assert.Equal(Map.Sites.All.Select(s => (s.Key, s.Type, s.Cells().Count())), again.Sites.All.Select(s => (s.Key, s.Type, s.Cells().Count())));
    }
}
