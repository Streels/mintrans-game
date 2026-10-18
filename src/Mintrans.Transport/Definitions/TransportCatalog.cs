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
public sealed record VehicleModel(
    TypeId Id,
    string Title,
    UnitRole Role,
    TypeId Network,
    double Speed,
    Mass Capacity,
    IReadOnlyList<TypeId> Cargo,
    int Length);

/// <summary>Как устроены перевозки по одной сети: где покупают технику и где грузят.</summary>
/// <param name="Depot">Вид сооружения, в котором покупают и обслуживают технику (депо, автогараж).</param>
/// <param name="StopParts">Части площадок предприятий, которые служат пунктами погрузки (станции).</param>
/// <param name="StopFacilities">Отдельные сооружения-пункты (городской грузовой пункт).</param>
/// <param name="Trains">Техника собирается в составы из локомотива и вагонов.</param>
public sealed record NetworkTransport(
    TypeId Network,
    TypeId Depot,
    IReadOnlyList<TypeId> StopParts,
    IReadOnlyList<TypeId> StopFacilities,
    int UnitsPerCell,
    bool Trains);

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

    public TransportCatalog(
        IEnumerable<VehicleModel> models,
        IEnumerable<NetworkTransport> networks,
        MaintenanceRules maintenance,
        LoadingRules loading)
    {
        _models = models.ToDictionary(m => m.Id);
        _networks = networks.ToDictionary(n => n.Network);
        Maintenance = maintenance;
        Loading = loading;
        foreach (var model in _models.Values)
        {
            if (!_networks.ContainsKey(model.Network))
                throw new ArgumentException($"Модель «{model.Id}» ездит по сети «{model.Network}», для которой нет правил перевозок.");
        }
    }

    public IReadOnlyCollection<VehicleModel> Models => _models.Values;
    public IReadOnlyCollection<NetworkTransport> Networks => _networks.Values;
    public MaintenanceRules Maintenance { get; }
    public LoadingRules Loading { get; }

    public VehicleModel Model(TypeId id) =>
        _models.TryGetValue(id, out var model) ? model : throw new KeyNotFoundException($"Нет модели техники «{id}».");

    public NetworkTransport Network(TypeId id) =>
        _networks.TryGetValue(id, out var network) ? network : throw new KeyNotFoundException($"Нет правил перевозок для сети «{id}».");

    public NetworkTransport? NetworkByDepot(TypeId depotKind) => _networks.Values.FirstOrDefault(n => n.Depot == depotKind);
}
