using Mintrans.Core.Ids;
using Mintrans.World.Networks;

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
/// Вид грузовой станции (docs/10): прямоугольник клеток вплотную к предприятию, становится частью его площадки.
/// Сквозь него укладывается сеть <see cref="Network"/>: вдоль длинной стороны — платформа длиной до <see cref="MaxLength"/>,
/// поперёк — до <see cref="MaxTracks"/> параллельных путей (на каждом обслуживается один состав).
/// </summary>
public sealed record StationDefinition(
    TypeId Id,
    TypeId Network,
    IReadOnlyList<TypeId> AttachToSiteTypes,
    int MaxLength,
    int MaxTracks = 1);

/// <summary>Вид моста (docs/10б): прямой, длиной до <see cref="MaxLength"/> клеток вместе с опорами.</summary>
public sealed record BridgeDefinition(TypeId Id, TypeId Network, int MaxLength);

/// <summary>Вид сигнала (docs/10а): на какой сети ставится и как решает, пустить ли поезд.</summary>
public sealed record SignalDefinition(TypeId Id, TypeId Network, SignalLogic Logic);

/// <summary>Пара сетей, которые могут пересекаться в одном уровне отдельной клеткой-переездом (дорога и рельсы, docs/10а).</summary>
public sealed record LevelCrossing(TypeId A, TypeId B)
{
    public bool Matches(TypeId x, TypeId y) => (x == A && y == B) || (x == B && y == A);
}

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
    private readonly Dictionary<TypeId, BridgeDefinition> _bridges;
    private readonly Dictionary<TypeId, SignalDefinition> _signals;

    public ConstructionCatalog(
        IEnumerable<NetworkBuildDefinition> networks,
        IEnumerable<StationDefinition> stations,
        IEnumerable<ForbiddenTerrain> forbiddenTerrain,
        IEnumerable<FacilityDefinition>? facilities = null,
        IEnumerable<BridgeDefinition>? bridges = null,
        IEnumerable<SignalDefinition>? signals = null,
        IEnumerable<LevelCrossing>? levelCrossings = null)
    {
        _networks = networks.ToDictionary(n => n.Network);
        _stations = stations.ToDictionary(s => s.Id);
        _facilities = (facilities ?? []).ToDictionary(f => f.Id);
        _bridges = (bridges ?? []).ToDictionary(b => b.Id);
        _signals = (signals ?? []).ToDictionary(s => s.Id);
        LevelCrossings = (levelCrossings ?? []).ToList();
        ForbiddenTerrain = forbiddenTerrain.ToList();
    }

    public IReadOnlyCollection<BridgeDefinition> Bridges => _bridges.Values;
    public IReadOnlyCollection<SignalDefinition> Signals => _signals.Values;
    public IReadOnlyList<LevelCrossing> LevelCrossings { get; }

    public bool AllowsLevelCrossing(TypeId a, TypeId b) => LevelCrossings.Any(c => c.Matches(a, b));

    public BridgeDefinition Bridge(TypeId id) =>
        _bridges.TryGetValue(id, out var definition) ? definition : throw new KeyNotFoundException($"Нет вида моста «{id}».");

    public SignalDefinition Signal(TypeId id) =>
        _signals.TryGetValue(id, out var definition) ? definition : throw new KeyNotFoundException($"Нет вида сигнала «{id}».");

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
