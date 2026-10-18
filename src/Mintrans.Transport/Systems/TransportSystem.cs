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
        var context = new Context(state.Map, transport, economy, handling);
        foreach (var vehicle in transport.Vehicles)
        {
            Update(vehicle, context);
            vehicle.HoursInState++;
        }
    }

    private sealed record Context(WorldMap Map, TransportState Transport, EconomyState Economy, Dictionary<SiteId, Mass> Handling)
    {
        public MaintenanceRules Maintenance => Transport.Catalog.Maintenance;

        public Mass Budget(EconomicSite site) =>
            Handling.TryGetValue(site.Site, out var left) ? left : site.Definition.HandlingPerHour;

        public void Spend(EconomicSite site, Mass used) => Handling[site.Site] = Budget(site) - used;
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
            Arrive(vehicle);
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

    private static void Arrive(Vehicle vehicle)
    {
        vehicle.Path = [];
        vehicle.MoveBudget = 0;
        switch (vehicle.Leg)
        {
            case Leg.ToA:
                vehicle.SetState(VehicleState.Loading, "грузится");
                break;
            case Leg.ToB:
                vehicle.SetState(VehicleState.Unloading, "разгружается");
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

    private static (EconomicSite? Sender, EconomicSite? Receiver) Ends(Vehicle vehicle, Context context)
    {
        Stop? Find(StopRef? r) => r is { } value ? context.Transport.Stops.Find(value) : null;
        var a = Find(vehicle.A);
        var b = Find(vehicle.B);
        return (a is null ? null : context.Economy.At(a.Owner), b is null ? null : context.Economy.At(b.Owner));
    }

    /// <summary>
    /// Груз выбирается сам (docs/12): то, что вагон может везти и что нужно получателю.
    /// Объём ограничен запасом отправителя, свободным местом в технике, скоростью пункта и местом у получателя.
    /// </summary>
    private static void Load(Vehicle vehicle, Context context)
    {
        var (sender, receiver) = Ends(vehicle, context);
        if (sender is null || receiver is null)
        {
            vehicle.SetState(VehicleState.Stuck, "у пункта маршрута нет склада");
            return;
        }

        bool Wanted(TypeId resource) => receiver.Needs(resource) && receiver.Stock.Stores(resource);
        var budget = context.Budget(sender);
        var used = Mass.Zero;
        foreach (var unit in vehicle.Units.Where(u => u.Free > Mass.Zero))
        {
            var candidates = unit.CargoType is { } type ? [type] : unit.Model.Cargo;
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

        var loadable = vehicle.Units.Where(u => u.Model.Cargo.Any(Wanted)).ToList();
        if (loadable.Count == 0)
        {
            vehicle.Reason = "нет вагонов для груза, нужного получателю";
            return;
        }
        var full = loadable.All(u => u.Free <= Mass.Zero);
        var waitedEnough = vehicle.HoursInState >= context.Transport.Catalog.Loading.MaxWaitHours && vehicle.Cargo > Mass.Zero;
        if (full || waitedEnough)
        {
            Depart(vehicle, Leg.ToB);
            return;
        }
        vehicle.Reason = used > Mass.Zero ? "грузится"
            : loadable.All(u => u.Model.Cargo.Where(Wanted).All(r => receiver.Stock.Free(r) - vehicle.CargoOf(r) <= Mass.Zero))
                ? "ждёт: у получателя нет места"
                : "ждёт груз";
    }

    private static void Unload(Vehicle vehicle, Context context)
    {
        var (_, receiver) = Ends(vehicle, context);
        if (receiver is null)
        {
            vehicle.SetState(VehicleState.Stuck, "у пункта маршрута нет склада");
            return;
        }

        var budget = context.Budget(receiver);
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

        if (vehicle.Cargo.IsZero)
        {
            var needsService = vehicle.Wear >= context.Maintenance.ServiceAt;
            Depart(vehicle, needsService ? Leg.ToDepot : Leg.ToA);
            return;
        }
        vehicle.Reason = used > Mass.Zero ? "разгружается" : "ждёт: склад получателя полон";
    }
}

/// <summary>Подключение транспорта к симуляции. Экономика должна быть подключена раньше.</summary>
public static class TransportModule
{
    public static TransportState Install(GameSimulation simulation, TransportCatalog catalog)
    {
        var transport = new TransportState(simulation.State.Map, catalog);
        simulation.State.Add(transport);
        simulation.Register(new TransportSystem());
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
