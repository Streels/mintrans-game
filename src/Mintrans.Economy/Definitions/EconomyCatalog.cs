using Mintrans.Core.Ids;

namespace Mintrans.Economy.Definitions;

/// <summary>Грузовой ресурс (docs/04): уголь, руда, сталь. Новые ресурсы — записи в content/economy/resources.json.</summary>
public sealed record ResourceDefinition(TypeId Id);

/// <summary>Когда процесс выполняется в суточном расчёте (docs/03): производство, затем потребление.</summary>
public enum ProcessPhase
{
    Production,
    Consumption,
}

/// <summary>Количество одного ресурса в рецепте.</summary>
public sealed record ResourceAmount(TypeId Resource, Mass Amount);

/// <summary>
/// Процесс объекта: добыча, переработка или потребление. За один цикл списывает входы и добавляет выходы;
/// за сутки выполняет не больше <see cref="CyclesPerDay"/> циклов.
/// Шахта — процесс без входов, завод — с входами и выходами, городской спрос — только с входами.
/// </summary>
public sealed record ProcessDefinition(
    string Id,
    ProcessPhase Phase,
    IReadOnlyList<ResourceAmount> Inputs,
    IReadOnlyList<ResourceAmount> Outputs,
    int CyclesPerDay)
{
    /// <summary>Максимум выпуска ресурса за сутки.</summary>
    public Mass MaxOutputPerDay(TypeId resource) =>
        Outputs.Where(o => o.Resource == resource).Aggregate(Mass.Zero, (sum, o) => sum + o.Amount * CyclesPerDay);
}

/// <summary>
/// Экономика объекта данного типа (docs/06): склады по ресурсам, скорость погрузки и разгрузки, процессы.
/// </summary>
public sealed record EconomicSiteDefinition(
    TypeId SiteType,
    IReadOnlyDictionary<TypeId, Mass> Storage,
    Mass HandlingPerHour,
    IReadOnlyList<ProcessDefinition> Processes);

/// <summary>Все экономические определения игры.</summary>
public sealed class EconomyCatalog
{
    private readonly Dictionary<TypeId, ResourceDefinition> _resources;
    private readonly Dictionary<TypeId, EconomicSiteDefinition> _sites;

    public EconomyCatalog(IEnumerable<ResourceDefinition> resources, IEnumerable<EconomicSiteDefinition> sites)
    {
        _resources = resources.ToDictionary(r => r.Id);
        _sites = sites.ToDictionary(s => s.SiteType);
        foreach (var site in _sites.Values)
        {
            var used = site.Storage.Keys
                .Concat(site.Processes.SelectMany(p => p.Inputs.Concat(p.Outputs)).Select(a => a.Resource));
            foreach (var resource in used)
            {
                if (!_resources.ContainsKey(resource))
                    throw new ArgumentException($"У «{site.SiteType}» неизвестный ресурс «{resource}».");
            }
            foreach (var amount in site.Processes.SelectMany(p => p.Inputs.Concat(p.Outputs)))
            {
                if (!site.Storage.ContainsKey(amount.Resource))
                    throw new ArgumentException($"У «{site.SiteType}» нет склада для «{amount.Resource}», нужного процессу.");
            }
        }
    }

    public IReadOnlyCollection<ResourceDefinition> Resources => _resources.Values;
    public IReadOnlyCollection<EconomicSiteDefinition> Sites => _sites.Values;

    public bool TryGetSite(TypeId siteType, out EconomicSiteDefinition definition) =>
        _sites.TryGetValue(siteType, out definition!);
}
