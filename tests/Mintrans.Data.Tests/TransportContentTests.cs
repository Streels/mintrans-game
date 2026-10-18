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
    public void Longest_train_fits_the_longest_station()
    {
        var content = ContentLocator.Load();
        var maxStation = content.Construction.Stations.Where(s => s.Network == "rail").Max(s => s.MaxLength);
        var rail = Catalog.Network("rail");
        var wagon = Catalog.Models.Where(m => m.Role == UnitRole.Wagon).Max(m => m.Length);
        foreach (var loco in Catalog.Models.Where(m => m.Role == UnitRole.Locomotive))
        {
            var cells = Math.Ceiling((loco.Length + loco.MaxWagons * wagon) / (double)rail.UnitsPerCell);
            Assert.True(cells <= maxStation, $"{loco.Title}: состав из {loco.MaxWagons} вагонов ({cells} кл.) не влезает в станцию {maxStation} кл.");
        }
    }
}
