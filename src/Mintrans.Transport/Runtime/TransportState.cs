using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Rail;
using Mintrans.Transport.Stops;
using Mintrans.World;
using Mintrans.World.Facilities;

namespace Mintrans.Transport.Runtime;

/// <summary>Почему действие с техникой не выполнено (текст для игрока).</summary>
public sealed class FleetException(string message) : Exception(message);

/// <summary>
/// Парк техники и действия игрока с ним (docs/11, docs/12): покупка в депо, сборка состава,
/// назначение маршрута А–Б. Хранится в <c>SimulationState</c>; движение выполняет <see cref="Systems.TransportSystem"/>.
/// Действия выполняются между шагами симуляции, поэтому вступают в силу со следующего часа.
/// </summary>
public sealed class TransportState
{
    private readonly SortedDictionary<int, Vehicle> _vehicles = new();
    private readonly WorldMap _map;
    private readonly Dictionary<TypeId, int> _counters = new();
    private int _nextId = 1;
    private long _nextTicket = 1;

    internal long NextTicket() => _nextTicket++;

    private readonly Func<int> _year;

    /// <param name="year">Текущий игровой год — по нему открываются модели (docs/14). Без него доступно всё.</param>
    public TransportState(WorldMap map, TransportCatalog catalog, Func<int>? year = null)
    {
        _map = map;
        Catalog = catalog;
        Stops = new StopIndex(map, catalog);
        _year = year ?? (() => int.MaxValue);
    }

    public int Year => _year();

    public bool IsAvailable(VehicleModel model) => model.AvailableFromYear <= Year;

    /// <summary>Модели, которые сейчас продаются для данной сети.</summary>
    public IEnumerable<VehicleModel> ForSale(TypeId network) =>
        Catalog.Models.Where(m => m.Network == network && IsAvailable(m));

    public TransportCatalog Catalog { get; }
    public StopIndex Stops { get; }

    private readonly Dictionary<TypeId, RailTraffic> _rail = new();

    /// <summary>Сигнализация и занятость путей рельсовой сети (docs/10а).</summary>
    public RailTraffic Rail(TypeId network)
    {
        if (!_rail.TryGetValue(network, out var traffic))
            _rail[network] = traffic = new RailTraffic(_map, this, Catalog.Network(network));
        return traffic;
    }

    /// <summary>Слой сети, по которой ездит техника данного вида.</summary>
    public World.Networks.NetworkLayer NetworkLayer(TypeId network) => _map.Networks[network];

    /// <summary>Рельсовые сети, по которым ездят поезда.</summary>
    public IEnumerable<RailTraffic> RailNetworks => Catalog.Networks.Where(n => n.Trains).Select(n => Rail(n.Network));

    /// <summary>Закрыт ли переезд в клетке хоть одной рельсовой сетью.</summary>
    public bool IsCrossingClosed(GridPos cell) => RailNetworks.Any(r => r.IsCrossingClosed(cell));

    /// <summary>Убрать технику из игры (обломки после крушения).</summary>
    internal void Remove(Vehicle vehicle)
    {
        if (vehicle.Network.Trains)
            Rail(vehicle.Network.Network).Forget(vehicle);
        _vehicles.Remove(vehicle.Id.Value);
    }

    /// <summary>Вся техника в постоянном порядке — расчёт воспроизводим.</summary>
    public IEnumerable<Vehicle> Vehicles => _vehicles.Values;

    public Vehicle this[VehicleId id] =>
        _vehicles.TryGetValue(id.Value, out var vehicle) ? vehicle : throw new FleetException($"Нет техники {id}.");

    public IEnumerable<Vehicle> InDepot(FacilityId depot) => Vehicles.Where(v => v.Depot == depot);

    /// <summary>Техника, которая сейчас грузится или разгружается в пункте, в порядке прибытия.</summary>
    public IReadOnlyList<Vehicle> AtStop(StopRef stop) =>
        Vehicles.Where(v => (v.State == VehicleState.Loading && v.A == stop) || (v.State == VehicleState.Unloading && v.B == stop))
            .OrderBy(v => v.QueueTicket)
            .ToList();

    /// <summary>Сколько груза сейчас в пути (в технике) — для проверки сохранения груза.</summary>
    public Mass InTransit(TypeId resource) => Vehicles.Aggregate(Mass.Zero, (sum, v) => sum + v.CargoOf(resource));

    /// <summary>
    /// Купить технику в депо или гараже: локомотив — начало нового состава, грузовик — готовая машина.
    /// Вагоны добавляются к составу через <see cref="AddWagon"/>.
    /// </summary>
    /// <param name="bodyId">Кузов грузовика (самосвал, бортовой); для моделей без выбора кузова — null.</param>
    public Vehicle Buy(FacilityId depotId, TypeId modelId, TypeId? bodyId = null)
    {
        if (!_map.Facilities.TryGet(depotId, out var depot))
            throw new FleetException("Депо не найдено.");
        var network = Catalog.NetworkByDepot(depot.Kind) ?? throw new FleetException($"«{depot.Name}» не продаёт технику.");
        var model = Catalog.Model(modelId);
        if (model.Network != network.Network)
            throw new FleetException($"{model.Title} не ездит по этой сети.");
        if (model.Role == UnitRole.Wagon)
            throw new FleetException("Вагон добавляется к уже купленному локомотиву.");
        if (!IsAvailable(model))
            throw new FleetException($"{model.Title} появится в {model.AvailableFromYear} году.");

        TruckBody? body = null;
        if (model.Bodies is { Count: > 0 } bodies)
        {
            if (bodyId is not { } id || !bodies.Contains(id))
                throw new FleetException($"Выберите кузов для {model.Title}.");
            body = Catalog.Body(id);
        }

        var title = body is null ? model.Title : $"{model.Title} {body.Title}";
        var number = _counters[model.Id] = _counters.GetValueOrDefault(model.Id) + 1;
        var vehicle = new Vehicle(new VehicleId(_nextId++), $"{title} №{number}", network, depot);
        vehicle.AddUnit(model, body);
        _vehicles.Add(vehicle.Id.Value, vehicle);
        return vehicle;
    }

    /// <summary>Прицепить вагон к составу, который стоит в депо.</summary>
    public void AddWagon(VehicleId id, TypeId modelId)
    {
        var vehicle = this[id];
        var model = Catalog.Model(modelId);
        if (vehicle.State != VehicleState.InDepot)
            throw new FleetException("Состав можно менять только в депо.");
        if (!vehicle.Network.Trains || model.Role != UnitRole.Wagon || model.Network != vehicle.Network.Network)
            throw new FleetException($"{model.Title} нельзя прицепить к «{vehicle.Name}».");
        if (!IsAvailable(model))
            throw new FleetException($"{model.Title} появится в {model.AvailableFromYear} году.");
        var limit = vehicle.Units.Where(u => u.Model.Role == UnitRole.Locomotive).Sum(u => u.Model.MaxWagons);
        if (limit > 0 && vehicle.WagonCount >= limit)
            throw new FleetException($"Локомотив тянет не больше {limit} вагонов.");
        vehicle.AddUnit(model);
    }

    /// <summary>Отцепить последний вагон (локомотив остаётся).</summary>
    public void RemoveWagon(VehicleId id)
    {
        var vehicle = this[id];
        if (vehicle.State != VehicleState.InDepot)
            throw new FleetException("Состав можно менять только в депо.");
        if (vehicle.Units.Count <= 1 || vehicle.Units[^1].Model.Role != UnitRole.Wagon)
            throw new FleetException("Вагонов нет.");
        vehicle.RemoveLastUnit();
    }

    /// <summary>Что мешает назначить маршрут; пустой список — можно.</summary>
    public IReadOnlyList<string> CheckRoute(VehicleId id, StopRef a, StopRef b)
    {
        var vehicle = this[id];
        var problems = new List<string>();
        var stopA = Stops.Find(a);
        var stopB = Stops.Find(b);
        if (stopA is null || stopB is null)
            problems.Add("Пункт маршрута не найден.");
        else
        {
            if (stopA.Ref == stopB.Ref)
                problems.Add("Пункты А и Б совпадают.");
            if (stopA.Network != vehicle.Network.Network || stopB.Network != vehicle.Network.Network)
                problems.Add("Пункты должны быть на сети этой техники.");
        }
        if (vehicle.Speed <= 0)
            problems.Add("Нет локомотива.");
        if (vehicle.Capacity.IsZero)
            problems.Add("Нет вагонов для груза.");
        return problems;
    }

    /// <summary>Назначить маршрут А–Б и выпустить технику из депо (или перенаправить в пути).</summary>
    public void AssignRoute(VehicleId id, StopRef a, StopRef b)
    {
        var problems = CheckRoute(id, a, b);
        if (problems.Count > 0)
            throw new FleetException(string.Join(" ", problems));
        var vehicle = this[id];
        if (vehicle.State == VehicleState.Crashed)
            throw new FleetException($"{vehicle.Name} разбит.");
        vehicle.A = a;
        vehicle.B = b;
        if (vehicle.State is VehicleState.InDepot or VehicleState.Stuck or VehicleState.Travelling or VehicleState.Stopped)
        {
            vehicle.Leg = Leg.ToA;
            vehicle.AfterDepot = Leg.ToA;
            ReleaseAhead(vehicle);
            vehicle.ClearPath();
            vehicle.SetState(VehicleState.Travelling);
        }
    }

    // ---------- Управление техникой на маршруте (docs/01, docs/11, docs/12) ----------

    /// <summary>
    /// Остановить технику там, где она есть: в пути, у пункта, в очереди. Груз остаётся в ней, поезд продолжает
    /// занимать путь, но закреплённый впереди путь освобождается. Возобновить — <see cref="Resume"/>.
    /// </summary>
    public void Stop(VehicleId id)
    {
        var vehicle = this[id];
        if (vehicle.State is not (VehicleState.Travelling or VehicleState.Stuck or VehicleState.Loading or VehicleState.Unloading))
            throw new FleetException(vehicle.State switch
            {
                VehicleState.Stopped => $"{vehicle.Name} уже остановлен.",
                VehicleState.InDepot => $"{vehicle.Name} стоит в депо.",
                VehicleState.Servicing => $"{vehicle.Name} на обслуживании — дождитесь его конца.",
                _ => $"{vehicle.Name} разбит.",
            });
        vehicle.StoppedFrom = vehicle.State;
        ReleaseAhead(vehicle);
        vehicle.MoveBudget = 0;
        vehicle.BlockedHours = 0;
        vehicle.SetState(VehicleState.Stopped, "остановлен");
    }

    /// <summary>
    /// Возобновить работу: остановленная техника продолжает то, что делала (у пункта встаёт в конец очереди);
    /// стоящая в депо техника с маршрутом выезжает на него — к Б, если в ней груз, иначе к А.
    /// </summary>
    public void Resume(VehicleId id)
    {
        var vehicle = this[id];
        switch (vehicle.State)
        {
            case VehicleState.Stopped when vehicle.StoppedFrom is VehicleState.Loading or VehicleState.Unloading:
                vehicle.QueueTicket = NextTicket();
                vehicle.SetState(vehicle.StoppedFrom, vehicle.StoppedFrom == VehicleState.Loading ? "грузится" : "разгружается");
                return;
            case VehicleState.Stopped:
                vehicle.ClearPath();
                vehicle.SetState(VehicleState.Travelling, "отправляется");
                return;
            case VehicleState.InDepot when vehicle.A is not null && vehicle.B is not null:
                vehicle.Leg = vehicle.Cargo > Mass.Zero ? Leg.ToB : Leg.ToA;
                vehicle.AfterDepot = Leg.ToA;
                vehicle.ClearPath();
                vehicle.SetState(VehicleState.Travelling, "отправляется");
                return;
            case VehicleState.InDepot:
                throw new FleetException("Сначала выберите пункты А и Б.");
            default:
                throw new FleetException($"{vehicle.Name} и так работает.");
        }
    }

    /// <summary>
    /// Отправить технику в депо (гараж) приписки. С <paramref name="service"/> — на техобслуживание с возвратом
    /// на маршрут; без него — обслужиться и остаться в депо, сохранив маршрут (возобновить — <see cref="Resume"/>).
    /// </summary>
    public void SendToDepot(VehicleId id, bool service)
    {
        var vehicle = this[id];
        if (vehicle.State == VehicleState.Crashed)
            throw new FleetException($"{vehicle.Name} разбит.");
        if (vehicle.State == VehicleState.Servicing)
            throw new FleetException($"{vehicle.Name} уже на обслуживании.");
        if (vehicle.State == VehicleState.InDepot)
        {
            if (!service)
                throw new FleetException($"{vehicle.Name} уже в депо.");
            vehicle.AfterDepot = null;
            vehicle.SetState(VehicleState.Servicing, "на обслуживании");
            return;
        }
        if (!_map.Facilities.TryGet(vehicle.Depot, out _))
            throw new FleetException("Депо приписки снесено.");
        vehicle.Leg = Leg.ToDepot;
        vehicle.AfterDepot = service && vehicle.A is not null ? vehicle.Cargo > Mass.Zero ? Leg.ToB : Leg.ToA : null;
        ReleaseAhead(vehicle);
        vehicle.ClearPath();
        vehicle.SetState(VehicleState.Travelling, service ? "едет на обслуживание" : "едет в депо");
    }

    /// <summary>
    /// Продать технику: только стоящую в депо и без груза. Денег пока нет (тема 09) — техника просто уходит из парка.
    /// </summary>
    public void Sell(VehicleId id)
    {
        var vehicle = this[id];
        if (vehicle.State != VehicleState.InDepot)
            throw new FleetException("Продать можно только технику, стоящую в депо.");
        if (vehicle.Cargo > Mass.Zero)
            throw new FleetException("В технике груз — сначала довезите его до получателя.");
        Remove(vehicle);
    }

    private void ReleaseAhead(Vehicle vehicle)
    {
        if (vehicle.Network.Trains)
            Rail(vehicle.Network.Network).ReleaseAhead(vehicle);
    }

    // ---------- Доставки по направлениям (docs/12) ----------

    private readonly Dictionary<(StopRef A, StopRef B), SortedDictionary<long, Mass>> _deliveries = new();

    /// <summary>Сколько суток истории доставок хранится по каждому направлению.</summary>
    public const int DeliveryHistoryDays = 30;

    /// <summary>Учесть груз, выгруженный в Б техникой направления А–Б в сутки <paramref name="day"/>.</summary>
    internal void RecordDelivery(StopRef a, StopRef b, long day, Mass mass)
    {
        if (mass <= Mass.Zero)
            return;
        if (!_deliveries.TryGetValue((a, b), out var days))
            _deliveries[(a, b)] = days = new SortedDictionary<long, Mass>();
        days[day] = days.GetValueOrDefault(day) + mass;
        foreach (var old in days.Keys.Where(d => d <= day - DeliveryHistoryDays).ToList())
            days.Remove(old);
    }

    /// <summary>Сколько груза доставлено по направлению А–Б с суток <paramref name="fromDay"/> включительно.</summary>
    public Mass DeliveredSince(StopRef a, StopRef b, long fromDay) =>
        _deliveries.TryGetValue((a, b), out var days)
            ? days.Where(p => p.Key >= fromDay).Aggregate(Mass.Zero, (sum, p) => sum + p.Value)
            : Mass.Zero;
}
