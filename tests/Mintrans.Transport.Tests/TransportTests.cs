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
        var mineStation = line.Transport.Stops.Find(line.Stop("coal_mine", "rail"))!;
        var path = Pathfinder.Find(line.Map.Networks["rail"], line.Depot.Area.Origin,
            mineStation.Area.Cells().ToHashSet(), farthest: true);

        Assert.NotNull(path);
        // Съезд из депо ведёт на восток, поэтому к шахте на западе поезд приходит через разворот в тупике у завода
        // и проезжает станцию шахты до дальнего (западного) конца.
        Assert.Equal(P(2, 6), path[^1]);
        Assert.Contains(P(53, 6), path);
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
    public void Train_longer_than_station_is_refused()
    {
        var line = new TestLine();
        var train = line.Train(gondolas: 16); // 17 единиц = 9 клеток, станция — 4
        var problems = line.Transport.CheckRoute(train.Id, line.Stop("coal_mine", "rail"), line.Stop("steel_plant", "rail"));
        Assert.Contains(problems, p => p.Contains("длиннее станции"));
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
