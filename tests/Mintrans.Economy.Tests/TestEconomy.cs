using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy.Definitions;
using Mintrans.Economy.Runtime;
using Mintrans.Economy.Systems;
using Mintrans.Simulation;
using Mintrans.Simulation.Systems;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.Economy.Tests;

/// <summary>Цепочка прототипа в коде: шахты, завод, город с понятными круглыми числами.</summary>
internal sealed class TestEconomy
{
    public static Mass T(double tonnes) => Mass.FromTonnes(tonnes);

    private static ResourceAmount A(string resource, double tonnes) => new(resource, T(tonnes));

    public static EconomyCatalog Catalog() => new(
        [new ResourceDefinition("coal"), new ResourceDefinition("iron_ore"), new ResourceDefinition("steel")],
        [
            new EconomicSiteDefinition("coal_mine", new Dictionary<TypeId, Mass> { ["coal"] = T(1000) }, T(100),
                [new ProcessDefinition("mining", ProcessPhase.Production, [], [A("coal", 1)], 300)]),
            new EconomicSiteDefinition("iron_mine", new Dictionary<TypeId, Mass> { ["iron_ore"] = T(1000) }, T(100),
                [new ProcessDefinition("mining", ProcessPhase.Production, [], [A("iron_ore", 1)], 400)]),
            new EconomicSiteDefinition("steel_plant",
                new Dictionary<TypeId, Mass> { ["coal"] = T(1000), ["iron_ore"] = T(1500), ["steel"] = T(500) }, T(100),
                [new ProcessDefinition("smelting", ProcessPhase.Production, [A("coal", 1), A("iron_ore", 1.5)], [A("steel", 1)], 200)]),
            new EconomicSiteDefinition("city", new Dictionary<TypeId, Mass> { ["steel"] = T(1000) }, T(100),
                [new ProcessDefinition("demand", ProcessPhase.Consumption, [A("steel", 1)], [], 150)]),
        ]);

    public GameSimulation Simulation { get; }
    public EconomyState Economy { get; }
    public EconomicSite CoalMine => Site("coal_mine");
    public EconomicSite IronMine => Site("iron_mine");
    public EconomicSite Plant => Site("steel_plant");
    public EconomicSite City => Site("city");

    public TestEconomy()
    {
        var terrain = new TerrainCatalog([new TerrainCategory("surface", [new TypeId("grass")], "grass")]);
        var map = new WorldMap(40, 10, 0, terrain, [new NetworkDefinition("road", EdgeNetworkLayer.LayoutName)]);
        var placement = SitePlacement.Basic();
        var x = 0;
        foreach (var type in new[] { "coal_mine", "iron_mine", "steel_plant", "city" })
        {
            placement.Place(map, new NewSiteRequest(type, type, type, new GridRect(x, 0, 4, 4)));
            x += 10;
        }
        Simulation = new GameSimulation(new SimulationState(map));
        Economy = EconomyModule.Install(Simulation, Catalog());
    }

    public EconomicSite Site(string key) => Economy.At(Simulation.State.Map.Sites.GetByKey(key).Id)!;

    public void RunDays(int days) => Simulation.Run(24L * days);

    /// <summary>Сохранение груза: для каждого ресурса запасы = начальные + произведено − потреблено (челноки ничего не держат в пути).</summary>
    public void AssertConserved()
    {
        foreach (var resource in Economy.Catalog.Resources)
            Assert.Equal(Economy.Totals.Expected(resource.Id), Economy.InStock(resource.Id));
    }
}

/// <summary>
/// Заменитель транспорта для тестов: каждый час перевозит до <paramref name="perHour"/> ресурса
/// со склада на склад через те же операции, что будет использовать настоящий транспорт (Ship / Receive).
/// Всё, что не поместилось у получателя, возвращается отправителю — груз не теряется.
/// </summary>
internal sealed class ShuttleSystem(EconomicSite from, EconomicSite to, TypeId resource, Mass perHour) : ISimulationSystem
{
    public bool Enabled { get; set; } = true;
    public string Name => $"shuttle.{resource}";
    public Schedule Schedule => Schedule.Hourly;
    public int Phase => Phases.Transport;

    public void Run(SimulationState state)
    {
        if (!Enabled)
            return;
        var loaded = from.Ship(resource, Mass.Min(perHour, to.Stock.Free(resource)));
        var left = loaded - to.Receive(resource, loaded);
        if (!left.IsZero)
            from.Receive(resource, left);
    }
}
