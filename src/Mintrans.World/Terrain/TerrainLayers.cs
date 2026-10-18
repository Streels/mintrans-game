using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;

namespace Mintrans.World.Terrain;

/// <summary>Слой одной природной категории: в каждой клетке хранится индекс вида.</summary>
public sealed class TerrainLayer
{
    internal TerrainLayer(TerrainCategory category, int width, int height)
    {
        Category = category;
        Cells = new CellLayer<byte>(width, height, category.Default.Index);
    }

    public TerrainCategory Category { get; }
    public CellLayer<byte> Cells { get; }

    public TerrainKind KindAt(GridPos pos) => Category[Cells[pos]];

    public bool Is(GridPos pos, TypeId kind) => KindAt(pos).Id == kind;

    public void Set(GridPos pos, TypeId kind) => Cells[pos] = Category.Get(kind).Index;
}

/// <summary>
/// Природа карты: высота клетки плюс по слою на каждую категорию из каталога.
/// Высота — целые уровни; сколько метров в уровне, решим вместе с масштабом клетки.
/// </summary>
public sealed class TerrainLayers
{
    private readonly Dictionary<TypeId, TerrainLayer> _layers = new();

    public TerrainLayers(TerrainCatalog catalog, int width, int height)
    {
        Catalog = catalog;
        Height = new CellLayer<short>(width, height);
        foreach (var category in catalog.Categories)
            _layers.Add(category.Id, new TerrainLayer(category, width, height));
    }

    public TerrainCatalog Catalog { get; }
    public CellLayer<short> Height { get; }
    public IEnumerable<TerrainLayer> Layers => _layers.Values;

    public bool TryGet(TypeId category, out TerrainLayer layer) => _layers.TryGetValue(category, out layer!);

    public TerrainLayer this[TypeId category] =>
        TryGet(category, out var layer) ? layer : throw new KeyNotFoundException($"Нет слоя рельефа «{category}».");
}
