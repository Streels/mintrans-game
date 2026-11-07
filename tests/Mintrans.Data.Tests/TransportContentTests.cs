using Mintrans.Transport.Definitions;

namespace Mintrans.Data.Tests;

/// <summary>Каталог техники из content/ (docs/11а, docs/14).</summary>
public class TransportContentTests
{
    private static readonly TransportCatalog Catalog = ContentLocator.Load().Transport;

    [Fact]
    public void In_1950_prototype_has_locomotive_wagons_and_trucks_for_every_cargo()
    {
        var available = Catalog.AvailableIn(1950).ToList();
        Assert.Contains(available, m => m.Role == UnitRole.Locomotive);
        Assert.Contains(available, m => m.Role == UnitRole.Wagon && m.Cargo.Contains("coal") && m.Cargo.Contains("iron_ore"));
        Assert.Contains(available, m => m.Role == UnitRole.Wagon && m.Cargo.Contains("steel"));
        var bodies = available.Where(m => m.Role == UnitRole.Truck).SelectMany(m => m.Bodies ?? []).Distinct()
            .Select(Catalog.Body).SelectMany(b => b.Cargo).ToHashSet();
        Assert.Superset(new HashSet<Core.Ids.TypeId> { "coal", "iron_ore", "steel" }, bodies);
    }

    [Fact]
    public void Every_model_appears_within_the_campaign_and_trucks_have_bodies()
    {
        Assert.All(Catalog.Models, m => Assert.InRange(m.AvailableFromYear, 1950, 2000));
        Assert.All(Catalog.Models.Where(m => m.Role == UnitRole.Truck), m => Assert.NotEmpty(m.Bodies ?? []));
        Assert.All(Catalog.Models.Where(m => m.Role == UnitRole.Locomotive), m => Assert.True(m.MaxWagons > 0));
    }

    [Fact]
    public void Rail_platform_length_is_free_so_the_longest_train_can_get_a_platform()
    {
        // Длину платформы игрок выбирает поклеточно, без верхнего предела (docs/10в, раздел 4): любой состав можно уместить.
        var content = ContentLocator.Load();
        var station = content.Construction.Stations.Single(s => s.Network == "rail");
        Assert.Equal(0, station.MaxLength);
        Assert.True(station.MaxDistanceM > 0);
        Assert.True(station.MaxTracks > 1);
    }
}
