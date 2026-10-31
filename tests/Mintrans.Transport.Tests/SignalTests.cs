using Mintrans.Transport.Runtime;
using Mintrans.World.Networks;
using static Mintrans.Transport.Tests.Corridor;

namespace Mintrans.Transport.Tests;

/// <summary>Требования docs/10а: сигналы, закрепление пути, разъезды, блокировки, крушения, переезды и мосты.</summary>
public class SignalTests
{
    [Fact]
    public void Wrong_one_way_block_signals_lead_to_crash_and_cargo_is_written_off()
    {
        // Блочные сигналы смотрят только в одну сторону: встречный поезд проходит сигнал сзади без проверки (docs/10а).
        var line = new Corridor();
        line.Signal("block_signal", P(23, 6), P(24, 6));
        line.Signal("block_signal", P(34, 6), P(33, 6));
        line.Signal("block_signal", P(41, 6), P(40, 6));
        line.Economy.Seed(line.Map.Sites.GetByKey("coal_mine").Id, "coal", T(1000));
        var loaded = line.Train(line.WestDepot, "coal_mine", "steel_plant");
        Assert.True(line.RunUntil(() => loaded.Leg == Leg.ToB && loaded.State == VehicleState.Travelling));
        Assert.True(loaded.Cargo > Economy.Mass.Zero);
        var oncoming = line.Train(line.EastDepot, "coal_mine", "steel_plant");

        Assert.True(line.RunUntil(() => loaded.State == VehicleState.Crashed, 24 * 3));
        Assert.Equal(VehicleState.Crashed, oncoming.State);
        Assert.Equal(Economy.Mass.Zero, loaded.Cargo);
        Assert.True(line.Economy.Totals.Lost("coal") > Economy.Mass.Zero);
        Assert.Contains(line.Simulation.State.Log.Messages, m => m.Text.Contains("Крушение"));
        AssertConserved(line);

        // Обломки занимают путь, затем исчезают; игра не чинит схему сигналов за игрока.
        line.Simulation.Run(line.Transport.Catalog.Rail.WreckHours + 1);
        Assert.Empty(line.Transport.Vehicles);
    }

    [Fact]
    public void Path_signals_let_opposite_trains_pass_on_a_loop()
    {
        var line = new Corridor();
        line.PassingLoop();
        line.Economy.Seed(line.Map.Sites.GetByKey("coal_mine").Id, "coal", T(1000));
        // Оба поезда идут навстречу: первый — с шахты на завод, второй — к шахте.
        var eastbound = line.Train(line.WestDepot, "coal_mine", "steel_plant");
        var westbound = line.Train(line.EastDepot, "coal_mine", "steel_plant");

        // Первый грузится на шахте, второй ждёт её у разъезда; гружёный первый уходит на завод вторым путём разъезда.
        var (usedLoop, delivered, reachedMine) = (false, false, false);
        Assert.True(line.RunUntil(() => delivered && reachedMine, 24 * 4, () =>
        {
            usedLoop |= new[] { eastbound, westbound }.Any(t => t.Position.Y == 7 && t.Position.X is >= 26 and <= 32);
            delivered |= eastbound.State == VehicleState.Unloading;
            reachedMine |= westbound.State == VehicleState.Loading;
        }));
        Assert.True(usedLoop, "поезда не разъехались на втором пути");
        Assert.DoesNotContain(line.Transport.Vehicles, v => v.State == VehicleState.Crashed);
        AssertConserved(line);
    }

    [Fact]
    public void Deadlock_is_reported_but_not_resolved()
    {
        // Двусторонние маршрутные сигналы на однопутной линии без разъезда: второй поезд доходит до сигнала (23,6)
        // и ждёт, пока шахту освободит первый, а тот ждёт, пока освободится путь, — блокировка
        // (docs/10а: она не устраняется, игрок получает предупреждение).
        var line = new Corridor();
        line.Signal("path_signal", P(23, 6), P(23, 6));
        line.Signal("path_signal", P(41, 6), P(41, 6));
        line.Economy.Seed(line.Map.Sites.GetByKey("coal_mine").Id, "coal", T(1000));
        var first = line.Train(line.WestDepot, "coal_mine", "steel_plant");
        var second = line.Train(line.EastDepot, "coal_mine", "steel_plant");

        var warning = line.Transport.Catalog.Rail.StuckWarningHours;
        Assert.True(line.RunUntil(() => first.BlockedHours >= warning && second.BlockedHours >= warning, 24 * 6));
        Assert.Equal(VehicleState.Travelling, first.State);
        Assert.Equal(VehicleState.Travelling, second.State);
        Assert.Contains("маршрутного сигнала", second.Reason);
        Assert.Contains(line.Simulation.State.Log.Messages, m => m.Text.Contains("возможна взаимная блокировка"));
        // Маршрутный сигнал, за которым никто не закрепил путь, красный.
        var rails = (TrackNetworkLayer)line.Map.Networks["rail"];
        var signal = rails.SignalOn(P(23, 6), TrackPieces.EastWest)!;
        Assert.True(signal.TwoWay);
        Assert.False(line.Transport.Rail("rail").IsGreen(signal, Core.Grid.Direction.West));
    }

    [Fact]
    public void Train_set_to_reverse_turns_back_at_red_signal()
    {
        var line = new Corridor();
        line.Signal("path_signal", P(23, 6), P(23, 6));
        line.Signal("path_signal", P(41, 6), P(41, 6));
        line.Economy.Seed(line.Map.Sites.GetByKey("coal_mine").Id, "coal", T(1000));
        var first = line.Train(line.WestDepot, "coal_mine", "steel_plant");
        var second = line.Train(line.EastDepot, "coal_mine", "steel_plant");
        second.RedSignal = RedSignalBehavior.Reverse;

        var reasons = new List<string>();
        line.RunUntil(() => reasons.Any(r => r.Contains("развернулся")), 24 * 6, () => reasons.Add(second.Reason));
        Assert.Contains(reasons, r => r.Contains("развернулся"));
        Assert.DoesNotContain(line.Transport.Vehicles, v => v.State == VehicleState.Crashed);
    }

    [Fact]
    public void Train_crosses_another_line_over_the_bridge()
    {
        var line = new Corridor();
        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        // За час поезд проходит несколько клеток, поэтому смотрим на маршрут: он ведёт по пролёту над поперечной линией.
        var onBridge = false;
        Assert.True(line.RunUntil(() => train.State == VehicleState.Loading, 24 * 3,
            () => onBridge |= train.Path.Any(p => p.Elevated && p.Cell == P(36, 6))));
        Assert.True(onBridge);
        Assert.True(NetworkQueries.AreConnected(line.Map.Networks["rail"], [P(36, 1)], [P(36, 12)]));
        Assert.False(NetworkQueries.AreConnected(line.Map.Networks["rail"], [P(36, 1)], [P(30, 6)]));
    }

    [Fact]
    public void Train_goes_straight_over_rail_cross_and_holds_it_against_the_other_line()
    {
        var line = new Corridor();
        // Поперечная линия пересекает коридор крестом в (28,6), без моста.
        Assert.True(line.Builder.BuildPath("rail", P(28, 2), P(28, 10)).CanBuild);
        var rails = (TrackNetworkLayer)line.Map.Networks["rail"];
        Assert.True(rails.PiecesAt(P(28, 6)).IsCrossing());

        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        var acrossOtherLine = new Rail.TrackSegment(P(28, 6), TrackPieces.NorthSouth, false);
        var (passedStraight, heldCross) = (false, false);
        Assert.True(line.RunUntil(() => train.State == VehicleState.Loading, 24 * 3, () =>
        {
            passedStraight |= train.Path.Any(p => p.Cell == P(28, 6) && p.Entry == Core.Grid.Direction.West && p.Exit == Core.Grid.Direction.East);
            heldCross |= line.Transport.Rail("rail").Reservations.ConflictWith(acrossOtherLine, new VehicleId(999)) == train.Id;
        }));
        Assert.True(passedStraight, "поезд не прошёл крест прямо");
        Assert.True(heldCross, "закреплённый путь через крест не мешает поперечной линии");
    }

    [Fact]
    public void Level_crossing_closes_for_trucks_while_train_passes()
    {
        var line = new Corridor();
        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        // Поезд идёт на восток и уже закрепил путь через переезд — машина подъедет к закрытому переезду.
        Assert.True(line.RunUntil(() => train.Position.X >= 38 && train.Position.Y == 6, 24 * 2));
        var truck = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.AssignRoute(truck.Id, line.CityStops[0], line.CityStops[1]);

        var waited = false;
        Assert.True(line.RunUntil(() => truck.State == VehicleState.Loading, 24, () =>
        {
            waited |= truck.Reason.Contains("ждёт у переезда");
            Assert.False(truck.Position == train.Position || train.Trail.Contains(truck.Position), "машина на переезде вместе с поездом");
        }));
        Assert.True(waited);
    }

    [Fact]
    public void Train_waits_while_truck_stands_on_crossing()
    {
        var line = new Corridor();
        var truck = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.AssignRoute(truck.Id, line.CityStops[0], line.CityStops[1]);
        Assert.True(line.RunUntil(() => truck.Position == P(48, 6), 4));
        // Дорогу за переездом снесли — машина застряла прямо на переезде.
        Assert.True(line.Builder.Demolish(P(48, 7), P(48, 7)).CanBuild);

        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        Assert.True(line.RunUntil(() => train.Reason.Contains("на переезде"), 24 * 2));
        Assert.Equal(P(47, 6), train.Position);

        Assert.True(line.Builder.BuildPath("road", P(48, 7), P(48, 10)).CanBuild);
        Assert.True(line.RunUntil(() => train.State == VehicleState.Loading, 24 * 2));
        Assert.DoesNotContain(line.Transport.Vehicles, v => v.State == VehicleState.Crashed);
    }

    private static Economy.Mass T(double tonnes) => Economy.Mass.FromTonnes(tonnes);

    private static void AssertConserved(Corridor line)
    {
        foreach (var resource in line.Economy.Catalog.Resources)
            Assert.Equal(line.Economy.Totals.Expected(resource.Id), line.Economy.InStock(resource.Id) + line.Transport.InTransit(resource.Id));
    }
}
