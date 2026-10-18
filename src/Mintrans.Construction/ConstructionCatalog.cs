using Mintrans.Core.Ids;

namespace Mintrans.Construction;

/// <summary>
/// Где разрешено класть сеть. По умолчанию — на любые свободные клетки;
/// на площадки объектов — только если тип объекта или вид части перечислены здесь
/// (например, дорога внутри города или рельсы внутри ж/д станции).
/// </summary>
public sealed record NetworkBuildDefinition(
    TypeId Network,
    IReadOnlyList<TypeId> AllowedSiteTypes,
    IReadOnlyList<TypeId> AllowedParts);

/// <summary>
/// Вид грузовой станции (docs/10): полоса клеток вплотную к предприятию, становится частью его площадки.
/// Сквозь неё укладывается сеть <see cref="Network"/>.
/// </summary>
public sealed record StationDefinition(
    TypeId Id,
    TypeId Network,
    IReadOnlyList<TypeId> AttachToSiteTypes,
    int MaxLength);

/// <summary>Как ставится сооружение.</summary>
public enum FacilityPlacement
{
    /// <summary>Депо или гараж: одна свободная клетка с въездом с одной стороны (сторона — по протяжке мышью).</summary>
    Depot,

    /// <summary>Остановка на уже проложенной сети внутри площадки объекта (городской грузовой пункт на улице).</summary>
    StopOnNetwork,
}

/// <summary>Сооружение вне площадок предприятий (docs/10): депо, автогараж, городской пункт.</summary>
/// <param name="Title">Название для игрока; к нему добавляется номер.</param>
/// <param name="SiteTypes">Для остановки — на площадках каких объектов её можно ставить.</param>
public sealed record FacilityDefinition(
    TypeId Id,
    string Title,
    TypeId Network,
    FacilityPlacement Placement,
    IReadOnlyList<TypeId> SiteTypes,
    int MaxLength);

/// <summary>Природа, на которой строить нельзя: категория рельефа и запрещённые виды (вода — пока нет мостов).</summary>
public sealed record ForbiddenTerrain(TypeId Category, IReadOnlyList<TypeId> Kinds);

/// <summary>Правила строительства из content/construction.json.</summary>
public sealed class ConstructionCatalog
{
    private readonly Dictionary<TypeId, NetworkBuildDefinition> _networks;
    private readonly Dictionary<TypeId, StationDefinition> _stations;
    private readonly Dictionary<TypeId, FacilityDefinition> _facilities;

    public ConstructionCatalog(
        IEnumerable<NetworkBuildDefinition> networks,
        IEnumerable<StationDefinition> stations,
        IEnumerable<ForbiddenTerrain> forbiddenTerrain,
        IEnumerable<FacilityDefinition>? facilities = null)
    {
        _networks = networks.ToDictionary(n => n.Network);
        _stations = stations.ToDictionary(s => s.Id);
        _facilities = (facilities ?? []).ToDictionary(f => f.Id);
        ForbiddenTerrain = forbiddenTerrain.ToList();
    }

    public IReadOnlyCollection<FacilityDefinition> Facilities => _facilities.Values;

    public FacilityDefinition Facility(TypeId id) =>
        _facilities.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Нет вида сооружения «{id}».");

    public IReadOnlyCollection<NetworkBuildDefinition> Networks => _networks.Values;
    public IReadOnlyCollection<StationDefinition> Stations => _stations.Values;
    public IReadOnlyList<ForbiddenTerrain> ForbiddenTerrain { get; }

    public NetworkBuildDefinition Network(TypeId id) =>
        _networks.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Для сети «{id}» нет правил строительства.");

    public StationDefinition Station(TypeId id) =>
        _stations.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Нет вида станции «{id}».");

    public bool IsStation(TypeId partKind) => _stations.ContainsKey(partKind);
}
