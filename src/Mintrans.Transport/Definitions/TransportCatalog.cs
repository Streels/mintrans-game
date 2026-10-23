using Mintrans.Core.Ids;
using Mintrans.Economy;

namespace Mintrans.Transport.Definitions;

/// <summary>Роль единицы техники: тянет состав, везёт груз в составе или сама везёт груз.</summary>
public enum UnitRole
{
    Locomotive,
    Wagon,
    Truck,
}

/// <summary>
/// Модель техники (docs/11): локомотив, полувагон, платформа, грузовик.
/// Скорость — клеток за игровой час; вместимость и допустимые грузы — у вагонов и грузовиков.
/// </summary>
/// <param name="Length">Длина в условных единицах; сколько единиц в клетке, задаёт <see cref="NetworkTransport.UnitsPerCell"/>.</param>
/// <param name="AvailableFromYear">С какого игрового года модель продаётся (docs/14); 0 — всегда.</param>
/// <param name="MaxWagons">Для локомотива — сколько вагонов он может тянуть (упрощённая сила тяги).</param>
/// <param name="Bodies">Для грузовика — какие кузова можно выбрать при покупке; кузов задаёт допустимые грузы (docs/11а).</param>
public sealed record VehicleModel(
    TypeId Id,
    string Title,
    UnitRole Role,
    TypeId Network,
    double Speed,
    Mass Capacity,
    IReadOnlyList<TypeId> Cargo,
    int Length,
    int AvailableFromYear = 0,
    int MaxWagons = 0,
    IReadOnlyList<TypeId>? Bodies = null);

/// <summary>Кузов грузовика (docs/11а): бортовой, самосвал… Определяет, какие грузы машина возит.</summary>
public sealed record TruckBody(TypeId Id, string Title, IReadOnlyList<TypeId> Cargo);

/// <summary>Как устроены перевозки по одной сети: где покупают технику и где грузят.</summary>
/// <param name="Depot">Вид сооружения, в котором покупают и обслуживают технику (депо, автогараж).</param>
/// <param name="StopParts">Части площадок предприятий, которые служат пунктами погрузки (станции).</param>
/// <param name="StopFacilities">Отдельные сооружения-пункты (городской грузовой пункт).</param>
/// <param name="Trains">Техника собирается в составы из локомотива и вагонов. У поездов число мест на станции —
/// число её путей (docs/10), очередь регулируют сигналы, а не <paramref name="StopSlots"/>.</param>
/// <param name="StopSlots">Сколько машин пункт данного вида обслуживает одновременно (docs/12: 2–10); остальные ждут.</param>
/// <param name="WagonsPerStopCell">Сколько вагонов помещается вдоль одной клетки платформы (docs/12: два).
/// Состав длиннее платформы обрабатывается медленнее — пропорционально тому, какая его часть стоит у платформы.</param>
public sealed record NetworkTransport(
    TypeId Network,
    TypeId Depot,
    IReadOnlyList<TypeId> StopParts,
    IReadOnlyList<TypeId> StopFacilities,
    int UnitsPerCell,
    bool Trains,
    IReadOnlyDictionary<TypeId, int>? StopSlots = null,
    int WagonsPerStopCell = 2)
{
    public int SlotsOf(TypeId stopKind) => StopSlots?.GetValueOrDefault(stopKind, 1) ?? 1;
}

/// <summary>
/// Движение поездов по сигналам (docs/10а, docs/12). Числа временные, уточняются при балансе.
/// </summary>
/// <param name="OccupiedDelayHours">Ожидаемая задержка за участок, занятый или закреплённый другим поездом, — при выборе маршрута.</param>
/// <param name="ReverseDelayHours">Ожидаемая задержка разворота — при выборе маршрута.</param>
/// <param name="ReverseAfterHours">Сколько часов поезд с настройкой «разворачиваться» ждёт у красного сигнала, прежде чем развернуться.</param>
/// <param name="StuckWarningHours">Через сколько часов без движения у красного сигнала игрок получает предупреждение о блокировке.</param>
/// <param name="WreckHours">Сколько часов обломки после крушения занимают путь.</param>
/// <param name="CrossingCloseCells">За сколько клеток до переезда приближающийся поезд закрывает его для машин.</param>
public sealed record RailTrafficRules(
    double OccupiedDelayHours = 6,
    double ReverseDelayHours = 2,
    int ReverseAfterHours = 6,
    int StuckWarningHours = 48,
    int WreckHours = 48,
    int CrossingCloseCells = 2);

/// <summary>Износ и обслуживание (docs/11). Числа временные.</summary>
/// <param name="WearPerCell">Прирост износа за клетку пути (1 = 100 %).</param>
/// <param name="ServiceAt">При каком износе техника после разгрузки едет на обслуживание.</param>
/// <param name="ServiceHours">Сколько часов длится обслуживание.</param>
public sealed record MaintenanceRules(double WearPerCell, double ServiceAt, int ServiceHours);

/// <summary>Погрузка (docs/12): сколько часов ждать полной загрузки, прежде чем ехать с тем, что есть.</summary>
public sealed record LoadingRules(int MaxWaitHours);

/// <summary>Все транспортные определения из content/transport/*.json.</summary>
public sealed class TransportCatalog
{
    private readonly Dictionary<TypeId, VehicleModel> _models;
    private readonly Dictionary<TypeId, NetworkTransport> _networks;

    private readonly Dictionary<TypeId, TruckBody> _bodies;

    public TransportCatalog(
        IEnumerable<VehicleModel> models,
        IEnumerable<NetworkTransport> networks,
        MaintenanceRules maintenance,
        LoadingRules loading,
        IEnumerable<TruckBody>? bodies = null,
        RailTrafficRules? rail = null)
    {
        _models = models.ToDictionary(m => m.Id);
        _bodies = (bodies ?? []).ToDictionary(b => b.Id);
        _networks = networks.ToDictionary(n => n.Network);
        Maintenance = maintenance;
        Loading = loading;
        Rail = rail ?? new RailTrafficRules();
        foreach (var model in _models.Values)
        {
            if (!_networks.ContainsKey(model.Network))
                throw new ArgumentException($"Модель «{model.Id}» ездит по сети «{model.Network}», для которой нет правил перевозок.");
            foreach (var body in model.Bodies ?? [])
            {
                if (!_bodies.ContainsKey(body))
                    throw new ArgumentException($"У модели «{model.Id}» неизвестный кузов «{body}».");
            }
        }
    }

    public IReadOnlyCollection<TruckBody> Bodies => _bodies.Values;

    public TruckBody Body(TypeId id) =>
        _bodies.TryGetValue(id, out var body) ? body : throw new KeyNotFoundException($"Нет кузова «{id}».");

    /// <summary>Какие модели продаются в этом году.</summary>
    public IEnumerable<VehicleModel> AvailableIn(int year) => Models.Where(m => m.AvailableFromYear <= year);

    public IReadOnlyCollection<VehicleModel> Models => _models.Values;
    public IReadOnlyCollection<NetworkTransport> Networks => _networks.Values;
    public MaintenanceRules Maintenance { get; }
    public LoadingRules Loading { get; }
    public RailTrafficRules Rail { get; }

    public VehicleModel Model(TypeId id) =>
        _models.TryGetValue(id, out var model) ? model : throw new KeyNotFoundException($"Нет модели техники «{id}».");

    public NetworkTransport Network(TypeId id) =>
        _networks.TryGetValue(id, out var network) ? network : throw new KeyNotFoundException($"Нет правил перевозок для сети «{id}».");

    public NetworkTransport? NetworkByDepot(TypeId depotKind) => _networks.Values.FirstOrDefault(n => n.Depot == depotKind);
}
