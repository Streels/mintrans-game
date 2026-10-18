using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Data.Definitions;
using Mintrans.Economy;
using Mintrans.Economy.Runtime;
using Mintrans.Economy.Systems;
using Mintrans.Simulation;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.Transport.Systems;

namespace Mintrans.Data.Tests;

/// <summary>
/// Главная проверка первого прототипа (docs/01, docs/12 «Проверка в первом прототипе»):
/// на настоящей карте и с настоящими данными игрок строит станции, пути, депо и гараж,
/// покупает поезда и грузовики, назначает маршруты — и сталь доходит до города.
/// </summary>
public class PrototypeChainTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static GridPos P(int x, int y) => new(x, y);

    [Fact]
    public void Coal_and_ore_by_rail_steel_by_truck_reach_the_city()
    {
        var content = ContentLocator.Load();
        var description = content.LoadMap("prototype");
        var map = description.Map;
        var simulation = new GameSimulation(new SimulationState(map));
        var economy = EconomyModule.Install(simulation, content.Economy);
        EconomyLoader.SeedStocks(economy, description);
        var transport = TransportModule.Install(simulation, content.Transport);
        var builder = new Builder(map, content.Construction);

        // 1. Станции предприятий и городской пункт на улице.
        Built(builder.BuildStation("rail_station", P(200, 48), P(207, 48)));
        Built(builder.BuildStation("rail_station", P(62, 50), P(69, 50)));
        Built(builder.BuildStation("rail_station", P(150, 149), P(163, 149)));
        Built(builder.BuildStation("truck_station", P(149, 158), P(149, 161)));
        // 2. Железные дороги и дорога до города.
        Built(builder.BuildPath("rail", P(208, 48), P(208, 140)));
        Built(builder.BuildPath("rail", P(208, 140), P(200, 149)));
        Built(builder.BuildPath("rail", P(200, 149), P(164, 149)));
        Built(builder.BuildPath("rail", P(61, 50), P(61, 100)));
        Built(builder.BuildPath("rail", P(61, 100), P(110, 149)));
        Built(builder.BuildPath("rail", P(110, 149), P(149, 149)));
        Built(builder.BuildPath("road", P(148, 160), P(80, 160)));
        Built(builder.BuildPath("road", P(80, 160), P(54, 186)));
        Built(builder.BuildPath("road", P(54, 186), P(53, 186)));
        Built(builder.BuildFacility("city_truck_stop", P(50, 186), P(52, 186)));
        // 3. Депо со съездом на линию и автогараж у дороги.
        var depot = builder.BuildFacility("rail_depot", P(180, 152), P(180, 151));
        Built(depot);
        Built(builder.BuildPath("rail", P(180, 151), P(181, 149)));
        var garage = builder.BuildFacility("truck_garage", P(147, 161), P(147, 160));
        Built(garage);

        // 4. Техника и маршруты.
        StopRef Stop(string key, string network) =>
            transport.Stops.All.First(s => s.Network == network && s.Owner == map.Sites.GetByKey(key).Id).Ref;
        // Стартовая пара прототипа (docs/11b): паровоз Л с 12 полувагонами и ГАЗ-51 с бортовым кузовом.
        Vehicle Train()
        {
            var train = transport.Buy(depot.Built!.Id, "steam_l");
            for (var i = 0; i < 12; i++)
                transport.AddWagon(train.Id, "gondola");
            return train;
        }
        var coalTrain = Train();
        transport.AssignRoute(coalTrain.Id, Stop("coal_mine", "rail"), Stop("steel_plant", "rail"));
        var oreTrain = Train();
        transport.AssignRoute(oreTrain.Id, Stop("iron_mine", "rail"), Stop("steel_plant", "rail"));
        for (var i = 0; i < 3; i++)
        {
            var truck = transport.Buy(garage.Built!.Id, "gaz_51", "flatbed");
            transport.AssignRoute(truck.Id, Stop("steel_plant", "road"), Stop("city", "road"));
        }

        // 5. Два игровых месяца.
        var cityDays = new List<double>();
        for (var day = 0; day < 60; day++)
        {
            simulation.Run(24);
            cityDays.Add(Site(economy, map, "city").Yesterday!.Processes[0].Share);
        }

        Assert.All(transport.Vehicles, v => Assert.NotEqual(VehicleState.Stuck, v.State));
        var plant = Site(economy, map, "steel_plant");
        Assert.True(Total(plant, d => d.Received, "coal") > 0, "уголь не доехал до завода");
        Assert.True(Total(plant, d => d.Received, "iron_ore") > 0, "руда не доехала до завода");
        Assert.True(economy.Totals.Produced("steel") > Mass.FromTonnes(600), "завод не выпустил сталь сверх начальных запасов");
        Assert.True(economy.Totals.Consumed("steel") > Mass.FromTonnes(300), "город не получил сталь сверх своего запаса");
        Assert.Contains(cityDays.Skip(10), share => share > 0);

        foreach (var resource in economy.Catalog.Resources)
            Assert.Equal(economy.Totals.Expected(resource.Id), economy.InStock(resource.Id) + transport.InTransit(resource.Id));

        // Сводка для баланса: видно, где узкое место цепочки.
        output.WriteLine($"Сталь выпущено: {economy.Totals.Produced("steel")}, потреблено городом: {economy.Totals.Consumed("steel")}");
        output.WriteLine($"Спрос города по дням: {string.Join(" ", cityDays.Select(s => (int)(s * 100)))}");
        foreach (var site in economy.Sites)
            output.WriteLine($"{map.Sites.All.First(x => x.Id == site.Site).Name}: {string.Join(", ", site.Stock.Resources.Select(r => $"{r} {site.Stock.Amount(r)}"))}");
        foreach (var vehicle in transport.Vehicles)
            output.WriteLine($"{vehicle.Name}: {vehicle.State}, {vehicle.Reason}, груз {vehicle.Cargo}, износ {vehicle.Wear:P0}");
    }

    private static EconomicSite Site(EconomyState economy, World.WorldMap map, string key) =>
        economy.At(map.Sites.GetByKey(key).Id)!;

    /// <summary>Сумма за последние 30 закрытых суток и текущие.</summary>
    private static long Total(EconomicSite site, Func<DailyStats, IReadOnlyDictionary<Core.Ids.TypeId, Mass>> pick, string resource) =>
        site.History.Append(site.Today).Sum(d => pick(d).GetValueOrDefault(resource).Kg);

    private static void Built(BuildPlan plan) =>
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => $"{p.Cell}: {p.Message}")));

    private static void Built(StationPlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));

    private static void Built(FacilityPlan plan) =>
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => $"{p.Cell}: {p.Message}")));
}
