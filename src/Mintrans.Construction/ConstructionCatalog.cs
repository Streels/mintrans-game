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

/// <summary>Природа, на которой строить нельзя: категория рельефа и запрещённые виды (вода — пока нет мостов).</summary>
public sealed record ForbiddenTerrain(TypeId Category, IReadOnlyList<TypeId> Kinds);

/// <summary>Правила строительства из content/construction.json.</summary>
public sealed class ConstructionCatalog
{
    private readonly Dictionary<TypeId, NetworkBuildDefinition> _networks;
    private readonly Dictionary<TypeId, StationDefinition> _stations;

    public ConstructionCatalog(
        IEnumerable<NetworkBuildDefinition> networks,
        IEnumerable<StationDefinition> stations,
        IEnumerable<ForbiddenTerrain> forbiddenTerrain)
    {
        _networks = networks.ToDictionary(n => n.Network);
        _stations = stations.ToDictionary(s => s.Id);
        ForbiddenTerrain = forbiddenTerrain.ToList();
    }

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
