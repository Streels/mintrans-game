using Mintrans.Data.Definitions;
using Mintrans.Economy;
using Mintrans.Economy.Runtime;
using Mintrans.Economy.Systems;
using Mintrans.Simulation;

namespace Mintrans.Data.Tests;

/// <summary>Экономика прототипа из content/: данные читаются, начальные запасы ложатся на склады, сутки считаются.</summary>
public class PrototypeEconomyTests
{
    private static (GameSimulation Simulation, EconomyState Economy) Start()
    {
        var content = ContentLocator.Load();
        var description = content.LoadMap("prototype");
        var simulation = new GameSimulation(new SimulationState(description.Map));
        var economy = EconomyModule.Install(simulation, content.Economy);
        EconomyLoader.SeedStocks(economy, description);
        return (simulation, economy);
    }

    private static EconomicSite Site(GameSimulation simulation, EconomyState economy, string key) =>
        economy.At(simulation.State.Map.Sites.GetByKey(key).Id)!;

    [Fact]
    public void All_prototype_objects_have_economy_and_scenario_stock()
    {
        var (simulation, economy) = Start();
        Assert.Equal(4, economy.Sites.Count());
        Assert.Equal(Mass.FromTonnes(600), Site(simulation, economy, "steel_plant").Stock.Amount("coal"));
        Assert.Equal(Mass.FromTonnes(300), Site(simulation, economy, "city").Stock.Amount("steel"));
    }

    [Fact]
    public void Without_transport_chain_runs_on_initial_stock_and_then_stops_with_reasons()
    {
        var (simulation, economy) = Start();
        simulation.Run(24 * 4);

        var plant = Site(simulation, economy, "steel_plant").Yesterday!.Processes[0];
        Assert.Equal(ProcessLimit.MissingInput, plant.Limit);
        var city = Site(simulation, economy, "city").Yesterday!.Processes[0];
        Assert.Equal(0.0, city.Share);
        // Шахты добывают, но вывезти некому — склады копятся.
        Assert.True(Site(simulation, economy, "coal_mine").Stock.Amount("coal") > Mass.Zero);

        foreach (var resource in economy.Catalog.Resources)
            Assert.Equal(economy.Totals.Expected(resource.Id), economy.InStock(resource.Id));
    }
}
