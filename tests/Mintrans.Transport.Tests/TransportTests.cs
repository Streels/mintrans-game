using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using static Mintrans.Transport.Tests.TestLine;

namespace Mintrans.Transport.Tests;

/// <summary>Требования docs/11 и docs/12 к технике и маршрутам.</summary>
public class TransportTests
{
    [Fact]
    public void Path_goes_from_depot_through_switch_and_reverses_at_dead_end()
    {
        var line = new TestLine();
        var plant = line.Transport.Stops.Find(line.Stop("steel_plant", "rail"))!;
        var path = Pathfinder.Find(line.Map.Networks["rail"], new RoutePoint(line.Depot.Area.Origin, null, null),
            plant.Area.Cells().ToHashSet(), new RouteOptions { RunThrough = true });

        Assert.NotNull(path);
        // Съезд из депо ведёт на запад, поэтому к заводу на востоке поезд приходит через разворот в тупике у шахты
        // и проезжает станцию завода до дальнего (восточного) конца.
        Assert.Equal(P(53, 6), path[^1].Cell);
        Assert.Contains(path, p => p.IsReversal && p.Cell.X <= 5);
    }

    [Fact]
    public void Train_is_bought_in_depot_and_assembled_from_locomotive_and_wagons()
    {
        var line = new TestLine();
        var train = line.Train(gondolas: 2);
        line.Transport.AddWagon(train.Id, "flatcar");

        Assert.Equal(VehicleState.InDepot, train.State);
        Assert.Equal(line.Depot.Area.Origin, train.Position);
        Assert.Equal(4, train.Units.Count);
        Assert.Equal(T(170), train.Capacity);
        Assert.Equal(2, train.LengthCells);
        Assert.Throws<FleetException>(() => line.Transport.Buy(line.Garage.Id, "loco"));
        Assert.Throws<FleetException>(() => line.Transport.AddWagon(train.Id, "truck"));
    }

    [Fact]
    public void Train_longer_than_platform_is_handled_slower()
    {
        // docs/10, docs/12: состав длиннее платформы обслуживается, но медленнее; перестановки нет.
        var line = new TestLine();
        line.Economy.Seed(line.Site("coal_mine").Site, "coal", T(1000));
        var train = line.Train(gondolas: 10); // платформа 4 клетки = 8 вагонов: у платформы 80 % состава
        var mine = line.Stop("coal_mine", "rail");
        Assert.Empty(line.Transport.CheckRoute(train.Id, mine, line.Stop("steel_plant", "rail")));
        line.Transport.AssignRoute(train.Id, mine, line.Stop("steel_plant", "rail"));

        var reasons = new List<string>();
        var loadingHours = 0;
        for (var hour = 0; hour < 24 * 10 && train.Leg != Leg.ToB; hour++)
        {
            line.RunHours(1);
            if (train.State == VehicleState.Loading)
            {
                loadingHours++;
                reasons.Add(train.Reason);
            }
        }
        Assert.Equal(T(600), train.Cargo);
        Assert.Contains(reasons, r => r.Contains("медленнее: у платформы 80"));
        // 600 т при 80 т/ч вместо 100 т/ч — восемь часов вместо шести.
        Assert.True(loadingHours >= 8, $"погрузка заняла {loadingHours} ч");
    }

    [Fact]
    public void Rail_station_has_one_place_per_track()
    {
        var line = new TestLine();
        var mine = line.Transport.Stops.Find(line.Stop("coal_mine", "rail"))!;
        var plant = line.Transport.Stops.Find(line.Stop("steel_plant", "rail"))!;
        Assert.Equal((2, 4), (mine.Slots, mine.Length));
        Assert.Equal((1, 4), (plant.Slots, plant.Length));
    }

    [Fact]
    public void Vehicles_beyond_station_slots_wait_for_a_free_place()
    {
        // docs/12: тип станции задаёт число мест; прибывшие сверх них ждут.
        var line = new TestLine();
        line.Economy.Seed(line.Site("steel_plant").Site, "steel", T(500));
        var trucks = Enumerable.Range(0, 3).Select(_ => line.Transport.Buy(line.Garage.Id, "truck")).ToList();
        foreach (var truck in trucks)
            line.Transport.AssignRoute(truck.Id, line.Stop("steel_plant", "road"), line.Stop("city", "road"));

        var sawWaiting = false;
        for (var hour = 0; hour < 48 && !sawWaiting; hour++)
        {
            line.RunHours(1);
            sawWaiting = trucks.Any(t => t.State == VehicleState.Loading && t.Reason.Contains("ждёт места у пункта"));
        }
        Assert.True(sawWaiting);
        line.AssertConserved();
    }

    [Fact]
    public void Train_carries_coal_from_mine_to_plant_and_cargo_is_never_lost()
    {
        var line = new TestLine();
        var train = line.Train();
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));

        line.RunHours(24 * 6);

        Assert.True(line.Site("steel_plant").History.Sum(d => d.Received.GetValueOrDefault("coal").Kg) > 0);
        Assert.True(line.Site("coal_mine").History.Sum(d => d.Shipped.GetValueOrDefault("coal").Kg) > 0);
        line.AssertConserved();
    }

    [Fact]
    public void Loading_is_limited_by_station_handling_rate()
    {
        var line = new TestLine();
        line.Economy.Seed(line.Site("coal_mine").Site, "coal", T(1000));
        var train = line.Train(gondolas: 3); // 180 т, а пункт грузит 100 т/ч
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));

        while (train.State != VehicleState.Loading)
            line.RunHours(1);
        line.RunHours(1);
        Assert.Equal(T(100), train.Cargo);
        line.RunHours(1);
        Assert.Equal(T(180), train.Cargo);
        line.RunHours(1);
        Assert.Equal(Leg.ToB, train.Leg);
    }

    [Fact]
    public void Station_serves_vehicles_in_arrival_order_and_others_wait_in_queue()
    {
        // docs/10, docs/12: больше техники, чем пункт успевает обработать, — очередь в порядке прибытия.
        // Второй поезд идёт за первым по блочным сигналам и встаёт на второй путь станции шахты.
        var line = new TestLine();
        line.Economy.Seed(line.Site("coal_mine").Site, "coal", T(1000));
        var early = line.Train(gondolas: 7); // 420 т — грузится пять часов при 100 т/ч
        var late = line.Train(gondolas: 7);
        var mine = line.Stop("coal_mine", "rail");
        var plant = line.Stop("steel_plant", "rail");
        // Второй купленный поезд выезжает на час раньше — значит, и обслуживаться должен первым.
        line.Transport.AssignRoute(late.Id, mine, plant);
        line.RunHours(1);
        line.Transport.AssignRoute(early.Id, mine, plant);

        RunUntil(line, () => early.State == VehicleState.Loading && early.Queued);
        Assert.Equal(VehicleState.Loading, late.State);
        Assert.True(late.Cargo > Economy.Mass.Zero);
        Assert.Equal(Economy.Mass.Zero, early.Cargo);
        Assert.Contains("в очереди на погрузку (впереди: 1)", early.Reason);
        Assert.Equal([late.Id, early.Id], line.Transport.AtStop(mine).Select(v => v.Id));

        // Поезда стоят на разных путях станции: у пункта два места (docs/10).
        Assert.NotEqual(late.Position.Y, early.Position.Y);
        RunUntil(line, () => late.State != VehicleState.Loading);
        Assert.Equal(T(420), late.Cargo);
        RunUntil(line, () => early.State != VehicleState.Loading);
        Assert.Equal(T(420), early.Cargo);
        line.AssertConserved();
    }

    /// <summary>Крутить часы, пока условие не выполнится; не дольше двух игровых недель.</summary>
    private static void RunUntil(TestLine line, Func<bool> done)
    {
        for (var hour = 0; hour < 24 * 14 && !done(); hour++)
            line.RunHours(1);
        Assert.True(done(), "условие не выполнилось за две игровые недели");
    }

    [Fact]
    public void Wagons_take_only_compatible_cargo_needed_by_receiver()
    {
        var line = new TestLine();
        line.Economy.Seed(line.Site("steel_plant").Site, "steel", T(300));
        line.Economy.Seed(line.Site("steel_plant").Site, "coal", T(300));
        var truck = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.AssignRoute(truck.Id, line.Stop("steel_plant", "road"), line.Stop("city", "road"));

        while (truck.State != VehicleState.Unloading)
            line.RunHours(1);
        // Городу нужна сталь, а не уголь, хотя грузовик может везти оба.
        Assert.Equal(T(20), truck.CargoOf("steel"));
        Assert.Equal(Economy.Mass.Zero, truck.CargoOf("coal"));
    }

    [Fact]
    public void Truck_repeats_a_b_a_without_returning_to_garage_and_delivers_to_city()
    {
        var line = new TestLine();
        line.Economy.Seed(line.Site("steel_plant").Site, "steel", T(500));
        var truck = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.AssignRoute(truck.Id, line.Stop("steel_plant", "road"), line.Stop("city", "road"));

        var visitedGarage = false;
        for (var hour = 0; hour < 24 * 3; hour++)
        {
            line.RunHours(1);
            visitedGarage |= truck.State == VehicleState.Servicing;
        }
        Assert.False(visitedGarage);
        var delivered = line.Site("city").History.Sum(d => d.Received.GetValueOrDefault("steel").Kg)
                        + line.Site("city").Today.Received.GetValueOrDefault("steel").Kg;
        Assert.True(delivered >= T(60).Kg, $"доставлено {delivered / 1000} т");
        line.AssertConserved();
    }

    [Fact]
    public void Worn_vehicle_goes_to_depot_for_service_and_returns_to_route()
    {
        var line = new TestLine(wearPerCell: 0.01, serviceAt: 0.5, serviceHours: 10);
        var train = line.Train();
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));

        var serviceHours = 0;
        var wasWorn = false;
        for (var hour = 0; hour < 24 * 10 && serviceHours < 10; hour++)
        {
            line.RunHours(1);
            wasWorn |= train.Wear >= 0.5;
            if (train.State == VehicleState.Servicing)
            {
                serviceHours++;
                Assert.Equal(line.Depot.Area.Origin, train.Position);
            }
        }
        Assert.True(wasWorn);
        Assert.Equal(10, serviceHours);
        line.RunHours(2);
        Assert.True(train.Wear < 0.1);
        Assert.Equal(Leg.ToA, train.Leg);
    }

    [Fact]
    public void Broken_track_stops_train_with_reason_and_rebuilding_restores_it()
    {
        var line = new TestLine();
        var train = line.Train();
        line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
        line.RunHours(2);

        Assert.True(line.Builder.Demolish(P(10, 6), P(10, 6)).CanBuild);
        line.RunHours(3);
        Assert.Equal(VehicleState.Stuck, train.State);
        Assert.Contains("нет пути", train.Reason);

        Assert.True(line.Builder.BuildPath("rail", P(9, 6), P(11, 6)).CanBuild);
        line.RunHours(1);
        Assert.Equal(VehicleState.Travelling, train.State);
    }

    [Fact]
    public void Depot_with_vehicles_cannot_be_demolished()
    {
        var line = new TestLine();
        line.Train();
        var plan = line.Builder.PlanDemolish(line.Depot.Area.Origin, line.Depot.Area.Origin);
        Assert.Contains(plan.Problems, p => p.Code == "guarded");
    }

    [Fact]
    public void Same_actions_give_same_result()
    {
        static string Play()
        {
            var line = new TestLine();
            var train = line.Train();
            line.Transport.AssignRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
            line.RunHours(24 * 5);
            return $"{train.Position} {train.Cargo} {train.State} {line.Site("steel_plant").Stock.Amount("coal")}";
        }
        Assert.Equal(Play(), Play());
    }
}
