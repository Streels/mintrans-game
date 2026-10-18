using Mintrans.Economy.Runtime;
using static Mintrans.Economy.Tests.TestEconomy;

namespace Mintrans.Economy.Tests;

/// <summary>Проверки из docs/01 «Что именно проверяем» и docs/04.</summary>
public class ChainTests
{
    [Fact]
    public void Mine_produces_daily_rate_until_storage_is_full()
    {
        var e = new TestEconomy();

        e.RunDays(1);
        Assert.Equal(T(300), e.CoalMine.Stock.Amount("coal"));
        Assert.Equal(ProcessLimit.None, e.CoalMine.Yesterday!.Processes[0].Limit);

        e.RunDays(3); // 1200 т не влезает в склад 1000 т
        Assert.Equal(T(1000), e.CoalMine.Stock.Amount("coal"));
        var last = e.CoalMine.Yesterday!.Processes[0];
        Assert.Equal(ProcessLimit.OutputFull, last.Limit);
        Assert.Equal("coal", last.Resource!.Value.Value);
        Assert.Equal(100, last.Cycles);
    }

    [Fact]
    public void Production_happens_once_per_day_not_every_hour()
    {
        var e = new TestEconomy();
        e.Simulation.Run(23);
        Assert.Equal(Mass.Zero, e.CoalMine.Stock.Amount("coal"));
        e.Simulation.Run(1);
        Assert.Equal(T(300), e.CoalMine.Stock.Amount("coal"));
    }

    [Fact]
    public void Plant_without_inputs_stays_idle_and_names_missing_resource()
    {
        var e = new TestEconomy();
        e.Economy.Seed(e.Plant.Site, "coal", T(500));
        e.RunDays(1);

        var result = e.Plant.Yesterday!.Processes[0];
        Assert.Equal(0, result.Cycles);
        Assert.Equal(ProcessLimit.MissingInput, result.Limit);
        Assert.Equal("iron_ore", result.Resource!.Value.Value);
        Assert.Equal(T(500), e.Plant.Stock.Amount("coal"));
    }

    [Fact]
    public void Plant_converts_inputs_by_recipe_within_capacity()
    {
        var e = new TestEconomy();
        e.Economy.Seed(e.Plant.Site, "coal", T(1000));
        e.Economy.Seed(e.Plant.Site, "iron_ore", T(1500));
        e.RunDays(1);

        Assert.Equal(T(200), e.Plant.Stock.Amount("steel"));
        Assert.Equal(T(800), e.Plant.Stock.Amount("coal"));
        Assert.Equal(T(1200), e.Plant.Stock.Amount("iron_ore"));
        Assert.Equal(T(200), e.Plant.Yesterday!.Produced["steel"]);
        Assert.Equal(T(300), e.Plant.Yesterday!.Consumed["iron_ore"]);
    }

    [Fact]
    public void Plant_stops_when_output_storage_is_full()
    {
        var e = new TestEconomy();
        e.Economy.Seed(e.Plant.Site, "coal", T(1000));
        e.Economy.Seed(e.Plant.Site, "iron_ore", T(1500));
        e.Economy.Seed(e.Plant.Site, "steel", T(450));
        e.RunDays(1);

        var result = e.Plant.Yesterday!.Processes[0];
        Assert.Equal(50, result.Cycles);
        Assert.Equal(ProcessLimit.OutputFull, result.Limit);
        Assert.Equal(T(500), e.Plant.Stock.Amount("steel"));
    }

    [Fact]
    public void City_consumes_only_what_it_has_and_reports_satisfaction()
    {
        var e = new TestEconomy();
        e.Economy.Seed(e.City.Site, "steel", T(225));

        e.RunDays(1);
        Assert.Equal(1.0, e.City.Yesterday!.Processes[0].Share);
        e.RunDays(1);
        Assert.Equal(0.5, e.City.Yesterday!.Processes[0].Share);
        Assert.Equal(ProcessLimit.MissingInput, e.City.Yesterday!.Processes[0].Limit);
        Assert.Equal(Mass.Zero, e.City.Stock.Amount("steel"));
    }

    [Fact]
    public void Loading_takes_from_sender_and_unloading_adds_to_receiver_within_space()
    {
        var e = new TestEconomy();
        e.Economy.Seed(e.CoalMine.Site, "coal", T(800));

        var shipped = e.CoalMine.Ship("coal", T(600));
        Assert.Equal(T(600), shipped);
        Assert.Equal(T(200), e.CoalMine.Stock.Amount("coal"));

        e.Economy.Seed(e.Plant.Site, "coal", T(700));
        var accepted = e.Plant.Receive("coal", shipped);
        Assert.Equal(T(300), accepted); // склад угля завода 1000 т
        Assert.Equal(T(300), e.Plant.Today.Received["coal"]);
        Assert.Equal(Mass.Zero, e.CoalMine.Ship("iron_ore", T(10))); // такого груза у шахты нет
    }

    [Fact]
    public void Resources_are_conserved_over_a_month_of_full_chain()
    {
        var e = new TestEconomy();
        e.Economy.Seed(e.City.Site, "steel", T(100));
        Shuttles(e);
        e.RunDays(30);
        e.AssertConserved();
        Assert.True(e.Economy.Totals.Consumed("steel") > Mass.Zero);
    }

    [Fact]
    public void Stopping_ore_supply_stops_steel_and_then_city_supply_and_restoring_it_recovers()
    {
        // Проверочный опыт из docs/01.
        var e = new TestEconomy();
        var (_, ore, _) = Shuttles(e);

        e.RunDays(10);
        Assert.Equal(1.0, e.City.Yesterday!.Processes[0].Share);

        ore.Enabled = false;
        e.RunDays(15);
        var plant = e.Plant.Yesterday!.Processes[0];
        Assert.Equal(0, plant.Cycles);
        Assert.Equal("iron_ore", plant.Resource!.Value.Value);
        Assert.Equal(0.0, e.City.Yesterday!.Processes[0].Share);
        Assert.Equal(ProcessLimit.OutputFull, e.IronMine.Yesterday!.Processes[0].Limit);

        ore.Enabled = true;
        e.RunDays(10);
        Assert.Equal(1.0, e.City.Yesterday!.Processes[0].Share);
        e.AssertConserved();
    }

    /// <summary>Подвоз угля и руды на завод и стали в город, с запасом по пропускной способности.</summary>
    private static (ShuttleSystem Coal, ShuttleSystem Ore, ShuttleSystem Steel) Shuttles(TestEconomy e)
    {
        var coal = new ShuttleSystem(e.CoalMine, e.Plant, "coal", T(20));
        var ore = new ShuttleSystem(e.IronMine, e.Plant, "iron_ore", T(30));
        var steel = new ShuttleSystem(e.Plant, e.City, "steel", T(20));
        e.Simulation.Register(coal);
        e.Simulation.Register(ore);
        e.Simulation.Register(steel);
        return (coal, ore, steel);
    }
}
