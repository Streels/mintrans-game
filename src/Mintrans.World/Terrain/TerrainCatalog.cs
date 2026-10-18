using Mintrans.Core.Ids;

namespace Mintrans.World.Terrain;

/// <summary>
/// Вид природного признака внутри категории: "grass" в "surface", "forest" в "vegetation".
/// Сюда позже добавятся свойства для правил строительства (уклон, стоимость, проходимость).
/// </summary>
public sealed record TerrainKind(TypeId Id, byte Index);

/// <summary>
/// Независимый природный слой: поверхность, растительность, вода, месторождения и т.д.
/// Категории не исключают друг друга: лес может расти на склоне, река течь по лугу.
/// </summary>
public sealed class TerrainCategory
{
    private readonly Dictionary<TypeId, TerrainKind> _byId = new();
    private readonly List<TerrainKind> _kinds = new();

    public TerrainCategory(TypeId id, IEnumerable<TypeId> kindIds, TypeId defaultKind)
    {
        Id = id;
        foreach (var kindId in kindIds)
        {
            if (_kinds.Count > byte.MaxValue)
                throw new ArgumentException($"В категории «{id}» больше {byte.MaxValue + 1} видов.");
            var kind = new TerrainKind(kindId, (byte)_kinds.Count);
            if (!_byId.TryAdd(kindId, kind))
                throw new ArgumentException($"В категории «{id}» вид «{kindId}» объявлен дважды.");
            _kinds.Add(kind);
        }
        if (_kinds.Count == 0)
            throw new ArgumentException($"Категория «{id}» не содержит видов.");
        Default = Get(defaultKind);
    }

    public TypeId Id { get; }
    public TerrainKind Default { get; }
    public IReadOnlyList<TerrainKind> Kinds => _kinds;

    public TerrainKind this[byte index] => _kinds[index];

    public bool TryGet(TypeId id, out TerrainKind kind) => _byId.TryGetValue(id, out kind!);

    public TerrainKind Get(TypeId id) =>
        TryGet(id, out var kind) ? kind : throw new KeyNotFoundException($"В категории «{Id}» нет вида «{id}».");
}

/// <summary>Все природные категории игры. Загружается из content/world/terrain.json.</summary>
public sealed class TerrainCatalog
{
    private readonly Dictionary<TypeId, TerrainCategory> _byId = new();
    private readonly List<TerrainCategory> _categories = new();

    public TerrainCatalog(IEnumerable<TerrainCategory> categories)
    {
        foreach (var category in categories)
        {
            if (!_byId.TryAdd(category.Id, category))
                throw new ArgumentException($"Категория рельефа «{category.Id}» объявлена дважды.");
            _categories.Add(category);
        }
    }

    public IReadOnlyList<TerrainCategory> Categories => _categories;

    public bool TryGet(TypeId id, out TerrainCategory category) => _byId.TryGetValue(id, out category!);

    public TerrainCategory Get(TypeId id) =>
        TryGet(id, out var category) ? category : throw new KeyNotFoundException($"Нет категории рельефа «{id}».");
}
