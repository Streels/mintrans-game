using Mintrans.Core.Ids;
using Mintrans.Economy.Definitions;
using Mintrans.World;

namespace Mintrans.Economy.Runtime;

/// <summary>
/// Общий баланс по ресурсам за всю игру: сколько задано сценарием, произведено, потреблено и потеряно.
/// По нему проверяется сохранение груза: запасы + груз в пути = начальные + произведено − потреблено − потеряно.
/// </summary>
public sealed class EconomyTotals
{
    private readonly Dictionary<TypeId, Mass> _seeded = new();
    private readonly Dictionary<TypeId, Mass> _produced = new();
    private readonly Dictionary<TypeId, Mass> _consumed = new();
    private readonly Dictionary<TypeId, Mass> _lost = new();

    public Mass Seeded(TypeId resource) => _seeded.GetValueOrDefault(resource);
    public Mass Produced(TypeId resource) => _produced.GetValueOrDefault(resource);
    public Mass Consumed(TypeId resource) => _consumed.GetValueOrDefault(resource);

    /// <summary>Сколько груза погибло вне складов — например, в крушении поезда (docs/10а).</summary>
    public Mass Lost(TypeId resource) => _lost.GetValueOrDefault(resource);

    /// <summary>Сколько ресурса должно существовать в мире (на складах и в транспорте).</summary>
    public Mass Expected(TypeId resource) => Seeded(resource) + Produced(resource) - Consumed(resource) - Lost(resource);

    internal void AddSeeded(TypeId resource, Mass mass) => _seeded[resource] = Seeded(resource) + mass;
    internal void AddProduced(TypeId resource, Mass mass) => _produced[resource] = Produced(resource) + mass;
    internal void AddConsumed(TypeId resource, Mass mass) => _consumed[resource] = Consumed(resource) + mass;
    internal void AddLost(TypeId resource, Mass mass) => _lost[resource] = Lost(resource) + mass;
}

/// <summary>
/// Экономика всей карты: по одному <see cref="EconomicSite"/> на каждый объект, чей тип описан в каталоге.
/// Хранится в <c>SimulationState</c>; транспорт будет обращаться к складам через <see cref="At"/>.
/// </summary>
public sealed class EconomyState
{
    private readonly SortedDictionary<int, EconomicSite> _sites = new();

    public EconomyState(EconomyCatalog catalog, WorldMap map, long day = 0)
    {
        Catalog = catalog;
        foreach (var site in map.Sites.All)
        {
            if (catalog.TryGetSite(site.Type, out var definition))
                _sites.Add(site.Id.Value, new EconomicSite(site.Id, definition, Totals, day));
        }
    }

    public EconomyCatalog Catalog { get; }
    public EconomyTotals Totals { get; } = new();

    /// <summary>Объекты в постоянном порядке (по идентификатору) — расчёт воспроизводим.</summary>
    public IEnumerable<EconomicSite> Sites => _sites.Values;

    public EconomicSite? At(SiteId site) => _sites.GetValueOrDefault(site.Value);

    /// <summary>Сколько ресурса сейчас лежит на всех складах.</summary>
    public Mass InStock(TypeId resource) => Sites.Aggregate(Mass.Zero, (sum, s) => sum + s.Stock.Amount(resource));

    /// <summary>
    /// Списать груз, погибший вне складов (крушение). Груз уже снят с техники вызывающим модулем;
    /// здесь он только учитывается в балансе, чтобы проверка сохранения груза сходилась.
    /// </summary>
    public void WriteOff(TypeId resource, Mass mass)
    {
        if (mass > Mass.Zero)
            Totals.AddLost(resource, mass);
    }

    /// <summary>Задать начальный запас объекта из сценария.</summary>
    public void Seed(SiteId site, TypeId resource, Mass mass)
    {
        var economic = At(site) ?? throw new ArgumentException($"У объекта {site} нет экономики.");
        economic.Seed(resource, mass);
    }
}
