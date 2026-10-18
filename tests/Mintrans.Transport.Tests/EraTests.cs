using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Runtime;
using static Mintrans.Transport.Tests.TestLine;

namespace Mintrans.Transport.Tests;

/// <summary>Открытие техники по годам (docs/14), кузова грузовиков и предел вагонов (docs/11а).</summary>
public class EraTests
{
    [Fact]
    public void Model_is_sold_only_from_its_year_and_news_is_posted()
    {
        var line = new TestLine();
        Assert.Equal(1950, line.Transport.Year);
        var error = Assert.Throws<FleetException>(() => line.Transport.Buy(line.Depot.Id, "new_loco"));
        Assert.Contains("1956", error.Message);
        Assert.DoesNotContain(line.Transport.ForSale("rail"), m => m.Id == "new_loco");

        line.Simulation.Run(line.Simulation.State.Calendar.StartOfYear(1956).Hours);

        Assert.Equal(1956, line.Transport.Year);
        Assert.Contains(line.Transport.ForSale("rail"), m => m.Id == "new_loco");
        Assert.NotNull(line.Transport.Buy(line.Depot.Id, "new_loco"));
        Assert.Contains(line.Simulation.State.Log.Messages, m => m.Text.Contains("Новый локомотив"));
    }

    [Fact]
    public void Bought_vehicles_keep_working_when_newer_models_appear()
    {
        var line = new TestLine();
        var old = line.Train();
        line.Simulation.Run(line.Simulation.State.Calendar.StartOfYear(1956).Hours);
        Assert.Contains(line.Transport.Vehicles, v => v.Id == old.Id);
    }

    [Fact]
    public void Truck_body_decides_cargo()
    {
        var line = new TestLine();
        Assert.Throws<FleetException>(() => line.Transport.Buy(line.Garage.Id, "body_truck"));
        var dump = line.Transport.Buy(line.Garage.Id, "body_truck", "dump");
        var flatbed = line.Transport.Buy(line.Garage.Id, "body_truck", "flatbed");

        Assert.True(dump.Units[0].CanCarry("coal"));
        Assert.False(dump.Units[0].CanCarry("steel"));
        Assert.True(flatbed.Units[0].CanCarry("steel"));
        Assert.Contains("самосвал", dump.Name);
    }

    [Fact]
    public void Locomotive_pulls_limited_number_of_wagons()
    {
        var line = new TestLine();
        var train = line.Transport.Buy(line.Depot.Id, "weak_loco");
        line.Transport.AddWagon(train.Id, "gondola");
        line.Transport.AddWagon(train.Id, "gondola");
        var error = Assert.Throws<FleetException>(() => line.Transport.AddWagon(train.Id, "gondola"));
        Assert.Contains("не больше 2", error.Message);
    }

    /// <summary>Модели, которых нет в основном наборе <see cref="TestLine"/>: добавляются отдельным каталогом.</summary>
    internal static IEnumerable<VehicleModel> ExtraModels() =>
    [
        new("new_loco", "Новый локомотив", UnitRole.Locomotive, "rail", 4, Mass.Zero, [], 1, AvailableFromYear: 1956),
        new("weak_loco", "Слабый локомотив", UnitRole.Locomotive, "rail", 4, Mass.Zero, [], 1, MaxWagons: 2),
        new("body_truck", "Грузовик с кузовом", UnitRole.Truck, "road", 4, T(5), [], 1, Bodies: [new TypeId("dump"), new TypeId("flatbed")]),
    ];

    internal static IEnumerable<TruckBody> Bodies() =>
    [
        new("dump", "самосвал", [new TypeId("coal"), new TypeId("iron_ore")]),
        new("flatbed", "бортовой", [new TypeId("steel")]),
    ];
}
