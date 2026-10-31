using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Economy.Runtime;
using Mintrans.Simulation;
using Mintrans.Simulation.Systems;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Rail;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Networks;

namespace Mintrans.Transport.Systems;

/// <summary>
/// Почасовой расчёт транспорта (docs/03, docs/10а, docs/11, docs/12): движение по маршруту с минимальным
/// ожидаемым временем, сигналы и закрепление пути, переезды, крушения, погрузка в А, выгрузка в Б,
/// заезд на обслуживание. Обходит только технику, а не всю карту.
/// Груз всегда ровно в одном месте: склад отправителя → техника → склад получателя (или списан при крушении).
/// </summary>
public sealed partial class TransportSystem : ISimulationSystem
{
    public string Name => "transport";
    public Schedule Schedule => Schedule.Hourly;
    public int Phase => Phases.Transport;

    public void Run(SimulationState state)
    {
        var transport = state.Get<TransportState>();
        var economy = state.Get<EconomyState>();
        foreach (var rail in transport.RailNetworks)
            rail.RebuildOccupancy();
        // Скорость погрузки и разгрузки — общий предел пункта на час, делится между всей техникой в нём.
        var handling = new Dictionary<SiteId, Mass>();

        // Очередь (docs/10, docs/12): техника у пунктов обслуживается в порядке прибытия.
        // Пункт одновременно принимает столько машин, сколько у него мест; кому не хватило места
        // или скорости обработки в этот час, ждёт.
        var working = transport.Vehicles
            .Where(v => v.State is VehicleState.Loading or VehicleState.Unloading)
            .OrderBy(v => v.QueueTicket)
            .ToList();
        var atStops = working
            .GroupBy(v => v.State == VehicleState.Loading ? v.A : v.B)
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => g.ToList());
        var context = new Context(state, transport, economy, handling, new Dictionary<SiteId, int>(), atStops, new List<Vehicle>());
        foreach (var vehicle in working)
            Update(vehicle, context);
        foreach (var vehicle in transport.Vehicles.Except(working).ToList())
            Update(vehicle, context);
        foreach (var wreck in context.Wrecks)
            transport.Remove(wreck);
        foreach (var vehicle in transport.Vehicles)
            vehicle.HoursInState++;
    }

    private sealed record Context(
        SimulationState State,
        TransportState Transport,
        EconomyState Economy,
        Dictionary<SiteId, Mass> Handling,
        Dictionary<SiteId, int> Served,
        Dictionary<StopRef, List<Vehicle>> AtStops,
        List<Vehicle> Wrecks)
    {
        public WorldMap Map => State.Map;
        public MaintenanceRules Maintenance => Transport.Catalog.Maintenance;
        public RailTrafficRules RailRules => Transport.Catalog.Rail;

        public Mass Budget(EconomicSite site) =>
            Handling.TryGetValue(site.Site, out var left) ? left : site.Definition.HandlingPerHour;

        public void Spend(EconomicSite site, Mass used) => Handling[site.Site] = Budget(site) - used;

        /// <summary>Место техники в очереди пункта по порядку прибытия (с нуля).</summary>
        public int Position(Stop stop, Vehicle vehicle) =>
            AtStops.TryGetValue(stop.Ref, out var list) ? list.IndexOf(vehicle) : 0;

        /// <summary>Сколько техники у пункта уже обслужено в этот час до текущей.</summary>
        public int Ahead(EconomicSite site)
        {
            var ahead = Served.GetValueOrDefault(site.Site);
            Served[site.Site] = ahead + 1;
            return ahead;
        }

        public void Post(string text) => State.Log.Post(State.Time, text);
    }

    private static void Update(Vehicle vehicle, Context context)
    {
        switch (vehicle.State)
        {
            case VehicleState.InDepot:
            case VehicleState.Stopped:
                return;
            case VehicleState.Crashed:
                if (vehicle.HoursInState >= context.RailRules.WreckHours)
                    context.Wrecks.Add(vehicle);
                return;
            case VehicleState.Servicing:
                if (vehicle.HoursInState >= context.Maintenance.ServiceHours)
                {
                    vehicle.Wear = 0;
                    // После ТО — дальше по маршруту; после команды «в депо» — остаться в депо (docs/11).
                    if (vehicle.AfterDepot is { } next && vehicle.A is not null)
                        Depart(vehicle, next);
                    else
                        vehicle.SetState(VehicleState.InDepot);
                }
                return;
            case VehicleState.Travelling:
            case VehicleState.Stuck:
                if (vehicle.Network.Trains)
                    TravelTrain(vehicle, context);
                else
                    TravelTruck(vehicle, context);
                return;
            case VehicleState.Loading:
                Load(vehicle, context);
                return;
            case VehicleState.Unloading:
                Unload(vehicle, context);
                return;
        }
    }

    // ---------- Движение машин ----------

    private static void TravelTruck(Vehicle vehicle, Context context)
    {
        var (targets, targetName) = Targets(vehicle, context);
        if (targets is null)
        {
            vehicle.SetState(VehicleState.Stuck, targetName);
            return;
        }

        var network = context.Map.Networks[vehicle.Network.Network];
        var revision = Revision(network, context);
        if (vehicle.Path.Count == 0 || vehicle.PathRevision != revision || !targets.Contains(vehicle.Path[^1].Cell))
        {
            // Путь пересчитывается только при смене цели или изменении сети (docs/03), а не каждый час.
            var options = new RouteOptions { Passable = cell => Passable(vehicle, context, cell) };
            var path = Pathfinder.Find(network, vehicle.Head with { Entry = null, Exit = null }, targets, options);
            if (path is null)
            {
                vehicle.SetState(VehicleState.Stuck, $"нет пути до «{targetName}»");
                return;
            }
            vehicle.SetPath(path, revision);
        }
        if (vehicle.State == VehicleState.Stuck)
            vehicle.SetState(VehicleState.Travelling);
        vehicle.Reason = $"едет: {targetName}";

        vehicle.MoveBudget += vehicle.Speed;
        while (vehicle.MoveBudget >= 1 && vehicle.PathIndex < vehicle.Path.Count - 1)
        {
            // Переезд (docs/10а): при подходе поезда машины ждут, уже въехавшие — проезжают.
            var next = vehicle.Path[vehicle.PathIndex + 1];
            if (!next.Elevated && context.Transport.IsCrossingClosed(next.Cell))
            {
                vehicle.MoveBudget = 0;
                vehicle.BlockedHours++;
                vehicle.Reason = "ждёт у переезда: идёт поезд";
                return;
            }
            vehicle.StepForward();
            vehicle.Wear += context.Maintenance.WearPerCell;
            vehicle.MoveBudget -= 1;
            vehicle.BlockedHours = 0;
        }
        if (vehicle.PathIndex >= vehicle.Path.Count - 1)
            Arrive(vehicle, context);
    }

    /// <summary>Машина на переезде в клетке — поезд её ждёт (docs/10а).</summary>
    private static Vehicle? TruckOn(GridPos cell, Context context)
    {
        if (!context.Map.Networks.Layers.Any(l => l.HasAny(cell)))
            return null;
        return context.Transport.Vehicles.FirstOrDefault(v => !v.Network.Trains && v.Position == cell
            && v.State is VehicleState.Travelling or VehicleState.Stuck or VehicleState.Loading or VehicleState.Unloading);
    }

    // ---------- Общее для движения ----------

    private static int Revision(NetworkLayer network, Context context) => network.Revision * 31 + context.Map.Facilities.Revision;

    private static (IReadOnlySet<GridPos>? Targets, string Name) Targets(Vehicle vehicle, Context context)
    {
        if (vehicle.Leg == Leg.ToDepot)
        {
            return context.Map.Facilities.TryGet(vehicle.Depot, out var depot)
                ? (depot.Area.Cells().ToHashSet(), depot.Name)
                : (null, "депо приписки снесено");
        }
        var reference = vehicle.Leg == Leg.ToA ? vehicle.A : vehicle.B;
        if (reference is not { } r || context.Transport.Stops.Find(r) is not { } stop)
            return (null, "пункт маршрута снесён");
        return (stop.Cells.ToHashSet(), stop.Name);
    }

    /// <summary>Сквозь депо и гаражи не ездят: в них заезжает только своя техника на обслуживание.</summary>
    private static bool Passable(Vehicle vehicle, Context context, GridPos cell)
    {
        if (context.Map.Facilities.At(cell) is not { } facility)
            return true;
        if (context.Transport.Catalog.NetworkByDepot(facility.Kind) is null)
            return true;
        return vehicle.Leg == Leg.ToDepot && facility.Id == vehicle.Depot;
    }

    private static void Arrive(Vehicle vehicle, Context context)
    {
        vehicle.ClearPath();
        vehicle.MoveBudget = 0;
        vehicle.CanTurn = true;
        switch (vehicle.Leg)
        {
            case Leg.ToA:
                vehicle.SetState(VehicleState.Loading, "грузится");
                vehicle.QueueTicket = context.Transport.NextTicket();
                if (vehicle.Network.Trains)
                    vehicle.Berth = BerthAtHead(vehicle, context);
                break;
            case Leg.ToB:
                vehicle.SetState(VehicleState.Unloading, "разгружается");
                vehicle.QueueTicket = context.Transport.NextTicket();
                if (vehicle.Network.Trains)
                    vehicle.Berth = BerthAtHead(vehicle, context);
                break;
            case Leg.ToDepot:
                // Заехал в депо: путей больше не занимает и ничего не закрепляет.
                if (vehicle.Network.Trains)
                {
                    var rail = context.Transport.Rail(vehicle.Network.Network);
                    var cell = context.Map.Facilities.TryGet(vehicle.Depot, out var depot) ? depot.Area.Origin : vehicle.Position;
                    rail.ReleaseAhead(vehicle);
                    vehicle.PlaceAt(cell);
                    vehicle.SetState(VehicleState.Servicing, "на обслуживании");
                    rail.Moved(vehicle);
                }
                else
                {
                    vehicle.PlaceAt(vehicle.Position);
                    vehicle.SetState(VehicleState.Servicing, "на обслуживании");
                }
                break;
        }
    }

    private static void Depart(Vehicle vehicle, Leg leg)
    {
        vehicle.Leg = leg;
        vehicle.Berth = null;
        vehicle.ClearPath();
        vehicle.SetState(VehicleState.Travelling, "отправляется");
    }

    // ---------- Погрузка и выгрузка ----------

    private sealed record Ends(Stop A, Stop B, EconomicSite Sender, EconomicSite Receiver);

    private static Ends? FindEnds(Vehicle vehicle, Context context)
    {
        var a = vehicle.A is { } ra ? context.Transport.Stops.Find(ra) : null;
        var b = vehicle.B is { } rb ? context.Transport.Stops.Find(rb) : null;
        if (a is null || b is null)
            return null;
        var sender = context.Economy.At(a.Owner);
        var receiver = context.Economy.At(b.Owner);
        return sender is null || receiver is null ? null : new Ends(a, b, sender, receiver);
    }

    /// <summary>
    /// Сколько технике можно обработать в этот час, или null — тогда она ждёт, а причина записана в <see cref="Vehicle.Reason"/>.
    /// Машины занимают места у пункта по порядку прибытия; поезд уже стоит на своём станционном пути (docs/10).
    /// Общий предел — оставшаяся скорость обработки пункта; состав длиннее платформы обрабатывается медленнее (docs/12).
    /// </summary>
    private static Mass? Admit(Vehicle vehicle, Context context, Stop stop, EconomicSite site, string work)
    {
        if (!vehicle.Network.Trains)
        {
            var position = context.Position(stop, vehicle);
            if (position >= stop.Slots)
            {
                vehicle.Queued = true;
                vehicle.Reason = $"ждёт места у пункта (впереди: {position - stop.Slots + 1})";
                return null;
            }
        }
        var ahead = context.Ahead(site);
        vehicle.Queued = context.Budget(site) <= Mass.Zero;
        if (vehicle.Queued)
        {
            vehicle.Reason = $"в очереди на {work} (впереди: {ahead})";
            return null;
        }
        var fit = Fit(vehicle, stop, context);
        var rate = new Mass((long)(site.Definition.HandlingPerHour.Kg * fit));
        return Mass.Min(context.Budget(site), Mass.Max(rate, new Mass(1)));
    }

    /// <summary>
    /// Груз выбирается сам (docs/12): то, что вагон может везти и что нужно получателю.
    /// Объём ограничен запасом отправителя, свободным местом в технике, скоростью пункта и местом у получателя.
    /// </summary>
    private static void Load(Vehicle vehicle, Context context)
    {
        if (FindEnds(vehicle, context) is not { } ends)
        {
            vehicle.SetState(VehicleState.Stuck, "у пункта маршрута нет склада");
            return;
        }
        var (stop, sender, receiver) = (ends.A, ends.Sender, ends.Receiver);
        bool Wanted(TypeId resource) => receiver.Needs(resource) && receiver.Stock.Stores(resource);

        var loadable = vehicle.CargoUnits.Where(u => u.Carries.Any(Wanted)).ToList();
        if (loadable.Count == 0)
        {
            vehicle.Reason = "нет вагонов для груза, нужного получателю";
            return;
        }
        if (Admit(vehicle, context, stop, sender, "погрузку") is not { } budget)
            return;

        var used = Mass.Zero;
        foreach (var unit in loadable.Where(u => u.Free > Mass.Zero))
        {
            var candidates = unit.CargoType is { } type ? [type] : unit.Carries;
            foreach (var resource in candidates.Where(Wanted))
            {
                var room = receiver.Stock.Free(resource) - vehicle.CargoOf(resource);
                var amount = Mass.Min(Mass.Min(unit.Free, budget - used), Mass.Min(sender.Stock.Amount(resource), room));
                if (amount <= Mass.Zero)
                    continue;
                var taken = sender.Ship(resource, amount);
                unit.CargoType = resource;
                unit.Cargo += taken;
                used += taken;
                break;
            }
        }
        context.Spend(sender, used);

        var full = loadable.All(u => u.Free <= Mass.Zero);
        var waitedEnough = vehicle.HoursInState >= context.Transport.Catalog.Loading.MaxWaitHours && vehicle.Cargo > Mass.Zero;
        if (full || waitedEnough)
        {
            Depart(vehicle, Leg.ToB);
            return;
        }
        vehicle.Reason = used > Mass.Zero ? Working("грузится", vehicle, stop, context)
            : loadable.All(u => u.Carries.Where(Wanted).All(r => receiver.Stock.Free(r) - vehicle.CargoOf(r) <= Mass.Zero))
                ? "ждёт: у получателя нет места"
                : "ждёт груз";
    }

    private static void Unload(Vehicle vehicle, Context context)
    {
        if (FindEnds(vehicle, context) is not { } ends)
        {
            vehicle.SetState(VehicleState.Stuck, "у пункта маршрута нет склада");
            return;
        }
        var (stop, receiver) = (ends.B, ends.Receiver);
        if (Admit(vehicle, context, stop, receiver, "разгрузку") is not { } budget)
            return;

        var used = Mass.Zero;
        foreach (var unit in vehicle.Units.Where(u => u.CargoType is not null))
        {
            var accepted = receiver.Receive(unit.CargoType!.Value, Mass.Min(unit.Cargo, budget - used));
            unit.Cargo -= accepted;
            used += accepted;
            if (unit.Cargo.IsZero)
                unit.CargoType = null;
        }
        context.Spend(receiver, used);
        context.Transport.RecordDelivery(vehicle.A!.Value, vehicle.B!.Value, context.State.Time.Day, used);

        if (vehicle.Cargo.IsZero)
        {
            var needsService = vehicle.Wear >= context.Maintenance.ServiceAt;
            vehicle.AfterDepot = Leg.ToA;
            Depart(vehicle, needsService ? Leg.ToDepot : Leg.ToA);
            return;
        }
        vehicle.Reason = used > Mass.Zero ? Working("разгружается", vehicle, stop, context) : "ждёт: склад получателя полон";
    }

    /// <summary>Какая доля состава у платформы: по платформе, у которой стоит поезд, иначе по длине пункта.</summary>
    private static double Fit(Vehicle vehicle, Stop stop, Context context) =>
        vehicle.Network.Trains && vehicle.Berth is { } berth && context.Map.Rails is { } rails && rails.ContainsBerth(berth)
            ? vehicle.PlatformFitM(rails.Berth(berth).LengthM)
            : vehicle.PlatformFit(stop.Length);

    /// <summary>«грузится» или «грузится медленнее: у платформы 60 % состава» для длинного состава.</summary>
    private static string Working(string verb, Vehicle vehicle, Stop stop, Context context)
    {
        var fit = Fit(vehicle, stop, context);
        return fit < 1 ? $"{verb} медленнее: у платформы {fit:P0} состава" : verb;
    }
}

/// <summary>
/// В начале каждого года сообщает игроку о технике, которая с этого года продаётся (docs/14).
/// Купленная раньше техника продолжает работать.
/// </summary>
public sealed class NewModelsSystem : ISimulationSystem
{
    public string Name => "transport.new_models";
    public Schedule Schedule => Schedule.Yearly;
    public int Phase => Phases.Statistics;

    public void Run(SimulationState state)
    {
        var year = state.Year;
        var fresh = state.Get<TransportState>().Catalog.Models.Where(m => m.AvailableFromYear == year).ToList();
        if (fresh.Count > 0)
            state.Log.Post(state.Time, $"С {year} года доступна новая техника: {string.Join(", ", fresh.Select(m => m.Title))}.");
    }
}

/// <summary>Подключение транспорта к симуляции. Экономика должна быть подключена раньше.</summary>
public static class TransportModule
{
    public static TransportState Install(GameSimulation simulation, TransportCatalog catalog)
    {
        var transport = new TransportState(simulation.State.Map, catalog, () => simulation.State.Year);
        simulation.State.Add(transport);
        simulation.Register(new TransportSystem());
        simulation.Register(new NewModelsSystem());
        return transport;
    }

    /// <summary>
    /// Почему сооружение нельзя снести с точки зрения транспорта (или null):
    /// к депо приписана техника, пункт входит в маршрут.
    /// </summary>
    public static string? WhyFacilityInUse(TransportState transport, Facility facility)
    {
        if (transport.InDepot(facility.Id).Any())
            return "к нему приписана техника";
        var reference = new StopRef($"facility:{facility.Id.Value}");
        return transport.Vehicles.Any(v => v.A == reference || v.B == reference) ? "входит в маршрут" : null;
    }
}
