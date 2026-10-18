using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Economy.Runtime;
using Mintrans.Simulation;
using Mintrans.Simulation.Systems;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.World;
using Mintrans.World.Facilities;

namespace Mintrans.Transport.Systems;

/// <summary>
/// Почасовой расчёт транспорта (docs/03, docs/11, docs/12): движение по кратчайшему пути,
/// погрузка в А, выгрузка в Б, заезд на обслуживание. Обходит только технику, а не всю карту.
/// Груз всегда ровно в одном месте: склад отправителя → техника → склад получателя.
/// </summary>
public sealed class TransportSystem : ISimulationSystem
{
    public string Name => "transport";
    public Schedule Schedule => Schedule.Hourly;
    public int Phase => Phases.Transport;

    public void Run(SimulationState state)
    {
        var transport = state.Get<TransportState>();
        var economy = state.Get<EconomyState>();
        // Скорость погрузки и разгрузки — общий предел пункта на час, делится между всей техникой в нём.
        var handling = new Dictionary<SiteId, Mass>();

        // Очередь (docs/10, docs/12): техника у пунктов обслуживается в порядке прибытия.
        // Пункт одновременно принимает столько единиц, сколько у него мест; кому не хватило места
        // или скорости обработки в этот час, ждёт.
        var working = transport.Vehicles
            .Where(v => v.State is VehicleState.Loading or VehicleState.Unloading)
            .OrderBy(v => v.QueueTicket)
            .ToList();
        var atStops = working
            .GroupBy(v => v.State == VehicleState.Loading ? v.A : v.B)
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => g.ToList());
        var context = new Context(state.Map, transport, economy, handling, new Dictionary<SiteId, int>(), atStops);
        foreach (var vehicle in working)
            Update(vehicle, context);
        foreach (var vehicle in transport.Vehicles.Except(working))
            Update(vehicle, context);
        foreach (var vehicle in transport.Vehicles)
            vehicle.HoursInState++;
    }

    private sealed record Context(
        WorldMap Map,
        TransportState Transport,
        EconomyState Economy,
        Dictionary<SiteId, Mass> Handling,
        Dictionary<SiteId, int> Served,
        Dictionary<StopRef, List<Vehicle>> AtStops)
    {
        public MaintenanceRules Maintenance => Transport.Catalog.Maintenance;

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
    }

    private static void Update(Vehicle vehicle, Context context)
    {
        switch (vehicle.State)
        {
            case VehicleState.InDepot:
                return;
            case VehicleState.Servicing:
                if (vehicle.HoursInState >= context.Maintenance.ServiceHours)
                {
                    vehicle.Wear = 0;
                    if (vehicle.A is null)
                        vehicle.SetState(VehicleState.InDepot);
                    else
                        Depart(vehicle, Leg.ToA);
                }
                return;
            case VehicleState.Travelling:
            case VehicleState.Stuck:
                Travel(vehicle, context);
                return;
            case VehicleState.Loading:
                Load(vehicle, context);
                return;
            case VehicleState.Unloading:
                Unload(vehicle, context);
                return;
        }
    }

    // ---------- Движение ----------

    private static void Travel(Vehicle vehicle, Context context)
    {
        var (targets, targetName, farthest) = Targets(vehicle, context);
        if (targets is null)
        {
            vehicle.SetState(VehicleState.Stuck, targetName);
            return;
        }

        var network = context.Map.Networks[vehicle.Network.Network];
        var revision = network.Revision * 31 + context.Map.Facilities.Revision;
        var needsPath = vehicle.Path.Count == 0
            || vehicle.PathRevision != revision
            || !targets.Contains(vehicle.Path[^1]);
        if (needsPath)
        {
            // Путь пересчитывается только при смене цели или изменении сети (docs/03), а не каждый час.
            var path = Pathfinder.Find(network, vehicle.Position, targets, cell => Passable(vehicle, context, cell), farthest);
            if (path is null)
            {
                vehicle.SetState(VehicleState.Stuck, $"нет пути до «{targetName}»");
                return;
            }
            vehicle.Path = path;
            vehicle.PathIndex = 0;
            vehicle.PathRevision = revision;
        }
        if (vehicle.State == VehicleState.Stuck)
            vehicle.SetState(VehicleState.Travelling);
        vehicle.Reason = $"едет: {targetName}";

        vehicle.MoveBudget += vehicle.Speed;
        while (vehicle.MoveBudget >= 1 && vehicle.PathIndex < vehicle.Path.Count - 1)
        {
            vehicle.StepForward();
            vehicle.Wear += context.Maintenance.WearPerCell;
            vehicle.MoveBudget -= 1;
        }
        if (vehicle.PathIndex >= vehicle.Path.Count - 1)
            Arrive(vehicle, context.Transport);
    }

    private static (IReadOnlySet<GridPos>? Targets, string Name, bool Farthest) Targets(Vehicle vehicle, Context context)
    {
        if (vehicle.Leg == Leg.ToDepot)
        {
            return context.Map.Facilities.TryGet(vehicle.Depot, out var depot)
                ? (depot.Area.Cells().ToHashSet(), depot.Name, false)
                : (null, "депо приписки снесено", false);
        }
        var reference = vehicle.Leg == Leg.ToA ? vehicle.A : vehicle.B;
        if (reference is not { } r || context.Transport.Stops.Find(r) is not { } stop)
            return (null, "пункт маршрута снесён", false);
        // Поезд проезжает станцию до дальнего конца, чтобы весь состав встал на платформу.
        return (stop.Area.Cells().ToHashSet(), stop.Name, vehicle.Network.Trains);
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

    private static void Arrive(Vehicle vehicle, TransportState transport)
    {
        vehicle.Path = [];
        vehicle.MoveBudget = 0;
        switch (vehicle.Leg)
        {
            case Leg.ToA:
                vehicle.SetState(VehicleState.Loading, "грузится");
                vehicle.QueueTicket = transport.NextTicket();
                (vehicle.PartIndex, vehicle.ShuntHoursLeft) = (0, 0);
                break;
            case Leg.ToB:
                vehicle.SetState(VehicleState.Unloading, "разгружается");
                vehicle.QueueTicket = transport.NextTicket();
                (vehicle.PartIndex, vehicle.ShuntHoursLeft) = (0, 0);
                break;
            case Leg.ToDepot:
                vehicle.ClearTrail();
                vehicle.SetState(VehicleState.Servicing, "на обслуживании");
                break;
        }
    }

    private static void Depart(Vehicle vehicle, Leg leg)
    {
        vehicle.Leg = leg;
        vehicle.Path = [];
        vehicle.SetState(VehicleState.Travelling);
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
    /// Можно ли технике работать с грузом в этот час. Порядок: свободное место у пункта (по порядку прибытия),
    /// перестановка между частями длинного состава, оставшаяся скорость обработки пункта.
    /// Возвращает вагоны текущей части или null — тогда техника ждёт, а причина записана в <see cref="Vehicle.Reason"/>.
    /// </summary>
    private static IReadOnlyList<VehicleUnit>? Admit(Vehicle vehicle, Context context, Stop stop, EconomicSite site, string work)
    {
        var position = context.Position(stop, vehicle);
        if (position >= stop.Slots)
        {
            vehicle.Queued = true;
            vehicle.Reason = $"ждёт места у пункта (впереди: {position - stop.Slots + 1})";
            return null;
        }
        var parts = vehicle.Parts(stop.Length);
        if (vehicle.ShuntHoursLeft > 0)
        {
            vehicle.ShuntHoursLeft--;
            vehicle.Queued = false;
            vehicle.Reason = $"перестановка: подаётся часть {vehicle.PartIndex + 1} из {parts}";
            return null;
        }
        var ahead = context.Ahead(site);
        vehicle.Queued = context.Budget(site) <= Mass.Zero;
        if (vehicle.Queued)
        {
            vehicle.Reason = $"в очереди на {work} (впереди: {ahead})";
            return null;
        }
        return vehicle.PartUnits(stop.Length);
    }

    /// <summary>Перейти к следующей части состава, если она есть. Перестановка занимает время (docs/12).</summary>
    private static bool NextPart(Vehicle vehicle, Stop stop)
    {
        var parts = vehicle.Parts(stop.Length);
        if (vehicle.PartIndex >= parts - 1)
            return false;
        vehicle.PartIndex++;
        vehicle.ShuntHoursLeft = vehicle.Network.ShuntHours;
        vehicle.Reason = $"перестановка: подаётся часть {vehicle.PartIndex + 1} из {parts}";
        return true;
    }

    /// <summary>
    /// Груз выбирается сам (docs/12): то, что вагон может везти и что нужно получателю.
    /// Объём ограничен запасом отправителя, свободным местом в технике, скоростью пункта и местом у получателя.
    /// Состав длиннее платформы грузится по частям.
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
        if (Admit(vehicle, context, stop, sender, "погрузку") is not { } part)
            return;

        var budget = context.Budget(sender);
        var used = Mass.Zero;
        foreach (var unit in part.Where(u => u.Free > Mass.Zero))
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

        var partFull = part.Where(u => u.Carries.Any(Wanted)).All(u => u.Free <= Mass.Zero);
        if (partFull && NextPart(vehicle, stop))
            return;
        var full = loadable.All(u => u.Free <= Mass.Zero);
        var waitedEnough = vehicle.HoursInState >= context.Transport.Catalog.Loading.MaxWaitHours && vehicle.Cargo > Mass.Zero;
        if (full || waitedEnough)
        {
            Depart(vehicle, Leg.ToB);
            return;
        }
        vehicle.Reason = used > Mass.Zero ? Working("грузится", vehicle, stop)
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
        if (Admit(vehicle, context, stop, receiver, "разгрузку") is not { } part)
            return;

        var budget = context.Budget(receiver);
        var used = Mass.Zero;
        foreach (var unit in part.Where(u => u.CargoType is not null))
        {
            var accepted = receiver.Receive(unit.CargoType!.Value, Mass.Min(unit.Cargo, budget - used));
            unit.Cargo -= accepted;
            used += accepted;
            if (unit.Cargo.IsZero)
                unit.CargoType = null;
        }
        context.Spend(receiver, used);

        if (vehicle.Cargo.IsZero)
        {
            var needsService = vehicle.Wear >= context.Maintenance.ServiceAt;
            Depart(vehicle, needsService ? Leg.ToDepot : Leg.ToA);
            return;
        }
        if (part.All(u => u.Cargo.IsZero) && NextPart(vehicle, stop))
            return;
        vehicle.Reason = used > Mass.Zero ? Working("разгружается", vehicle, stop) : "ждёт: склад получателя полон";
    }

    /// <summary>«грузится» или «грузится, часть 2 из 3» для длинного состава.</summary>
    private static string Working(string verb, Vehicle vehicle, Stop stop)
    {
        var parts = vehicle.Parts(stop.Length);
        return parts > 1 ? $"{verb}, часть {vehicle.PartIndex + 1} из {parts}" : verb;
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
