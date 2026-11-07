using Mintrans.Construction.Rail;
using Mintrans.Transport.Rail;
using Mintrans.Transport.Runtime;
using Mintrans.World.Rail;
using static Mintrans.Transport.Tests.Corridor;
using static Mintrans.Transport.Tests.TestLine;

namespace Mintrans.Transport.Tests;

/// <summary>Требования docs/10а и docs/10в: сигналы, закрепление пути, разъезды, блокировки, крушения, переезды и мосты.</summary>
public class SignalTests
{
    [Fact]
    public void Wrong_one_way_block_signals_lead_to_crash_and_cargo_is_written_off()
    {
        // Блочные сигналы смотрят только в одну сторону: встречный поезд проходит сигнал сзади без проверки (docs/10а).
        var line = new Corridor();
        line.Signal("block_signal", 26, 27);
        line.Signal("block_signal", 74, 73);
        line.Signal("block_signal", 110, 109);
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
        Assert.True(line.RunUntil(() => delivered && reachedMine, 24 * 6, () =>
        {
            usedLoop |= new[] { eastbound, westbound }.Any(t => t.Position.Y == 21 && t.Position.X is >= 40 and <= 60);
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
        // Двусторонние маршрутные сигналы на однопутной линии без разъезда: второй поезд доходит до сигнала x = 26
        // и ждёт, пока шахту освободит первый, а тот ждёт, пока освободится путь, — блокировка
        // (docs/10а: она не устраняется, игрок получает предупреждение).
        var line = new Corridor();
        line.Signal("path_signal", 26, 26);
        line.Signal("path_signal", 110, 110);
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
        var signal = line.Rails.Signals.MinBy(s => line.Rails.CellOf(s).X)!;
        Assert.True(signal.TwoWay);
        Assert.False(line.Transport.Rail("rail").IsGreen(signal, forward: false));
        Assert.DoesNotContain(line.Transport.Vehicles, v => v.State == VehicleState.Crashed);
    }

    [Fact]
    public void Train_set_to_reverse_turns_back_at_red_signal()
    {
        var line = new Corridor();
        line.Signal("path_signal", 26, 26);
        line.Signal("path_signal", 110, 110);
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
        var rails = line.Rails;
        var onBridge = false;
        Assert.True(line.RunUntil(() => train.State == VehicleState.Loading, 24 * 4,
            () => onBridge |= train.RailRoute?.Edges.Any(e => rails.IsBridge(rails.Edge(e.Edge))) == true));
        Assert.True(onBridge);
        // Поперечная линия проходит под мостом и с главным путём не соединена.
        var cross = rails.Edges.Single(e => Math.Abs(e.Geometry.Start.X - C(80, 4).X) < 0.01);
        Assert.Null(RailRouter.Find(rails, new TrackPosition(new DirectedEdge(cross.Id, true), 0),
            new TrackPosition(new DirectedEdge(rails.Edges.First().Id, true), 10)));
    }

    [Fact]
    public void Independent_line_cannot_cross_at_grade()
    {
        var line = new Corridor();
        // Поперечная линия через главный путь без моста — нет допустимого соединения (docs/10в, раздел 7).
        var connection = line.Builder.PlanRail(C(30, 4), C(30, 36));
        Assert.False(connection.CanConfirm);
        Assert.Contains(connection.Rejections, r => r.Contains("мост"));
    }

    [Fact]
    public void Level_crossing_closes_for_trucks_while_train_passes()
    {
        var line = new Corridor();
        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        var rail = line.Transport.Rail("rail");
        var crossing = line.Map.Crossings.At(Crossing).Single();
        var span = new TrackSpan(crossing.Edge, crossing.FromM, crossing.ToM);
        // Поезд идёт на восток и подходит к переезду — машина подъедет к закрытому переезду.
        Assert.True(line.RunUntil(() => train.Position.X >= 116 && train.Position.Y == 20, 24 * 3));
        var truck = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.AssignRoute(truck.Id, line.CityStops[0], line.CityStops[1]);

        var waited = false;
        Assert.True(line.RunUntil(() => truck.State == VehicleState.Loading, 48, () =>
        {
            waited |= truck.Reason.Contains("ждёт у переезда");
            Assert.False(truck.Position == Crossing && rail.Occupancy.Any(span), "машина на переезде вместе с поездом");
        }));
        Assert.True(waited);
    }

    [Fact]
    public void Train_waits_while_truck_stands_on_crossing()
    {
        var line = new Corridor();
        var truck = line.Transport.Buy(line.Garage.Id, "truck");
        line.Transport.AssignRoute(truck.Id, line.CityStops[0], line.CityStops[1]);
        Assert.True(line.RunUntil(() => truck.Position == Crossing, 8));
        // Дорогу за переездом снесли — машина застряла прямо на переезде.
        Assert.True(line.Builder.Demolish(P(140, 21), P(140, 21)).CanBuild);

        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        Assert.True(line.RunUntil(() => train.Reason.Contains("на переезде"), 24 * 3));
        Assert.Equal(20, train.Position.Y);
        // Поезд подъехал вплотную к переезду и стоит перед ним.
        Assert.Equal(139, train.Position.X);

        Assert.True(line.Builder.BuildPath("road", P(140, 21), P(140, 26)).CanBuild);
        Assert.True(line.RunUntil(() => train.State == VehicleState.Loading, 24 * 3));
        Assert.DoesNotContain(line.Transport.Vehicles, v => v.State == VehicleState.Crashed);
    }

    [Fact]
    public void Building_a_switch_under_a_running_train_keeps_its_place()
    {
        var line = new Corridor();
        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        Assert.True(line.RunUntil(() => train.Position.X >= 110 && train.Position.Y == 20, 24 * 4));
        var before = line.Rails.Locate(train.Body!.Head).At;
        // Стрелка позади поезда делит путь, на котором он стоит: положение переезжает на часть пути.
        Built(line.Builder.BuildRail(C(104, 20), C(150, 4)));
        Assert.True(line.Rails.Locate(train.Body!.Head).At.ApproxEquals(before, 1e-6));
        Assert.True(line.RunUntil(() => train.State == VehicleState.Loading, 24 * 4));
        Assert.DoesNotContain(line.Transport.Vehicles, v => v.State == VehicleState.Crashed);
    }

    [Fact]
    public void Track_under_a_train_cannot_be_demolished()
    {
        var line = new Corridor();
        var train = line.Train(line.WestDepot, "steel_plant", "coal_mine");
        Assert.True(line.RunUntil(() => train.Position.X >= 110 && train.Position.Y == 20, 24 * 4));
        var cell = train.Position;
        var plan = line.Builder.PlanDemolish(cell, cell);
        Assert.Contains(plan.Problems, p => p.Message.Contains("поезд"));
    }

    private static void AssertConserved(Corridor line)
    {
        foreach (var resource in line.Economy.Catalog.Resources)
            Assert.Equal(line.Economy.Totals.Expected(resource.Id), line.Economy.InStock(resource.Id) + line.Transport.InTransit(resource.Id));
    }
}
