using Mintrans.Economy.Runtime;
using Mintrans.Transport.Reports;
using Mintrans.Transport.Runtime;
using static Mintrans.Transport.Tests.TestLine;

namespace Mintrans.Transport.Tests;

/// <summary>Управление техникой на маршруте и сведения для игрока (docs/01, docs/11, docs/12).</summary>
public class RouteControlTests
{
    [Fact]
    public void Stopped_train_stands_still_and_resumes_its_leg()
    {
        var line = new TestLine();
        var train = line.Train();
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
        RunUntil(line, () => train.State == VehicleState.Travelling && train.Position.Y == 6);

        line.Transport.Stop(train.Id);
        var position = train.Position;
        line.RunHours(10);
        Assert.Equal(VehicleState.Stopped, train.State);
        Assert.Equal(position, train.Position);
        Assert.Throws<FleetException>(() => line.Transport.Stop(train.Id));

        line.Transport.Resume(train.Id);
        RunUntil(line, () => train.State == VehicleState.Loading);
        Assert.Equal(Leg.ToA, train.Leg);
    }

    [Fact]
    public void Stopping_supply_starves_the_plant_and_resuming_restores_it()
    {
        // Проверочный опыт docs/01: остановить подвоз — у завода кончается сырьё и встаёт выпуск; возобновить — работает снова.
        var line = new TestLine();
        var plant = line.Site("steel_plant");
        line.Economy.Seed(plant.Site, "iron_ore", T(1500));
        var train = line.Train(gondolas: 3);
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
        RunUntil(line, () => line.Economy.Totals.Produced("steel") > Economy.Mass.Zero, 24 * 10);

        line.Transport.Stop(train.Id);
        RunUntil(line, () => plant.Yesterday is { } day && day.Processes[0].Limit == ProcessLimit.MissingInput
                             && day.Processes[0].Resource == "coal" && day.Processes[0].Cycles == 0, 24 * 10);

        var produced = line.Economy.Totals.Produced("steel");
        line.Transport.Resume(train.Id);
        RunUntil(line, () => line.Economy.Totals.Produced("steel") > produced, 24 * 10);
        line.AssertConserved();
    }

    [Fact]
    public void Sent_to_depot_train_is_serviced_and_waits_there_with_its_route()
    {
        var line = new TestLine(wearPerCell: 0.001);
        var train = line.Train();
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
        line.RunHours(3);
        Assert.True(train.Wear > 0);

        line.Transport.SendToDepot(train.Id, service: false);
        RunUntil(line, () => train.State == VehicleState.InDepot);
        Assert.Equal(0, train.Wear);
        Assert.NotNull(train.A);
        Assert.Throws<FleetException>(() => line.Transport.SendToDepot(train.Id, service: false));

        line.Transport.Resume(train.Id);
        RunUntil(line, () => train.State == VehicleState.Loading);
    }

    [Fact]
    public void Train_sent_for_service_returns_to_its_route_and_keeps_its_cargo_leg()
    {
        var line = new TestLine(wearPerCell: 0.001);
        line.Economy.Seed(line.Site("coal_mine").Site, "coal", T(1000));
        var train = line.Train();
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
        RunUntil(line, () => train.Leg == Leg.ToB && train.State == VehicleState.Travelling);
        var cargo = train.Cargo;

        line.Transport.SendToDepot(train.Id, service: true);
        RunUntil(line, () => train.State == VehicleState.Servicing);
        RunUntil(line, () => train.State != VehicleState.Servicing);
        Assert.Equal(Leg.ToB, train.Leg);
        Assert.Equal(cargo, train.Cargo);
        RunUntil(line, () => train.State == VehicleState.Unloading);
        line.AssertConserved();
    }

    [Fact]
    public void Only_empty_vehicle_standing_in_depot_can_be_sold()
    {
        var line = new TestLine();
        line.Economy.Seed(line.Site("coal_mine").Site, "coal", T(1000));
        var idle = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.Sell(idle.Id);
        Assert.DoesNotContain(idle, line.Transport.Vehicles);

        var train = line.Train();
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
        line.RunHours(1);
        Assert.Throws<FleetException>(() => line.Transport.Sell(train.Id));

        // Гружёный поезд в депо продать нельзя: груз сначала довозят, и «возобновить» везёт его к Б.
        RunUntil(line, () => train.Leg == Leg.ToB && train.State == VehicleState.Travelling);
        line.Transport.SendToDepot(train.Id, service: false);
        RunUntil(line, () => train.State == VehicleState.InDepot);
        Assert.Contains("груз", Assert.Throws<FleetException>(() => line.Transport.Sell(train.Id)).Message);
        line.Transport.Resume(train.Id);
        Assert.Equal(Leg.ToB, train.Leg);
        line.AssertConserved();
    }

    [Fact]
    public void Arrival_estimate_matches_the_actual_trip()
    {
        var line = new TestLine();
        line.Economy.Seed(line.Site("steel_plant").Site, "steel", T(500));
        var truck = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.AssignRoute(truck.Id, line.Stop("steel_plant", "road"), line.Stop("city", "road"));
        RunUntil(line, () => truck.Leg == Leg.ToB && truck.State == VehicleState.Travelling && truck.HoursToArrival > 1);

        var estimate = truck.HoursToArrival!.Value;
        var hours = 0;
        while (truck.State == VehicleState.Travelling && hours < 100)
        {
            line.RunHours(1);
            hours++;
        }
        Assert.Equal(VehicleState.Unloading, truck.State);
        Assert.InRange(hours, Math.Floor(estimate), Math.Ceiling(estimate) + 1);
    }

    [Fact]
    public void Direction_summary_and_cargo_in_transit_are_reported()
    {
        var line = new TestLine();
        line.Economy.Seed(line.Site("steel_plant").Site, "steel", T(500));
        var trucks = Enumerable.Range(0, 2).Select(_ => line.Transport.Buy(line.Garage.Id, "truck")).ToList();
        var (plant, city) = (line.Stop("steel_plant", "road"), line.Stop("city", "road"));
        foreach (var truck in trucks)
            line.Transport.AssignRoute(truck.Id, plant, city);

        RunUntil(line, () => trucks.Any(t => t.Leg == Leg.ToB && t.State == VehicleState.Travelling));
        var cityId = line.Map.Sites.GetByKey("city").Id;
        var inTransit = RouteReport.InTransitTo(line.Transport, cityId);
        Assert.True(inTransit["steel"].Amount > Economy.Mass.Zero);
        Assert.Empty(RouteReport.InTransitTo(line.Transport, line.Map.Sites.GetByKey("coal_mine").Id));

        line.RunHours(24 * 2);
        var summary = RouteReport.Direction(line.Transport, line.Economy, plant, city, line.Simulation.State.Time.Day);
        Assert.Equal(2, summary.Vehicles);
        Assert.Equal(T(40), summary.Capacity);
        Assert.True(summary.DistanceCells > 0);
        Assert.True(summary.PerDay > Economy.Mass.Zero);
        Assert.True(summary.Delivered > Economy.Mass.Zero);
        // Доставленное по направлению совпадает с тем, что город принял за эти сутки.
        var received = line.Site("city").History.Append(line.Site("city").Today).Sum(d => d.Received.GetValueOrDefault("steel").Kg);
        Assert.Equal(received, summary.Delivered.Kg);

        line.Transport.Stop(trucks[0].Id);
        Assert.Equal(1, RouteReport.Direction(line.Transport, line.Economy, plant, city, line.Simulation.State.Time.Day).Working);
    }

    private static void RunUntil(TestLine line, Func<bool> done, int limit = 24 * 14)
    {
        for (var hour = 0; hour < limit && !done(); hour++)
            line.RunHours(1);
        Assert.True(done(), $"условие не выполнилось за {limit} ч");
    }
}
