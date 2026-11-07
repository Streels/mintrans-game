using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;

namespace Mintrans.World.Terrain;

/// <summary>
/// Значение рельефа на крупной сетке (docs/02в, раздел 5): ячейка рельефа — квадрат <see cref="Step"/>×<see cref="Step"/>
/// строительных клеток. Читать и писать можно по строительной клетке — она попадает в свою ячейку рельефа.
/// Так местность 40×40 км с шагом 100 м занимает 400×400 значений, а не 8 000×8 000.
/// </summary>
public sealed class TerrainGrid<T> where T : unmanaged
{
    internal TerrainGrid(int width, int height, int step, T initial = default)
    {
        Step = step;
        Coarse = new CellLayer<T>((width + step - 1) / step, (height + step - 1) / step, initial);
    }

    /// <summary>Сколько строительных клеток по стороне в одной ячейке рельефа.</summary>
    public int Step { get; }

    /// <summary>Ячейки рельефа.</summary>
    public CellLayer<T> Coarse { get; }

    public int Revision => Coarse.Revision;

    public T this[GridPos cell]
    {
        get => Coarse[cell.X / Step, cell.Y / Step];
        set => Coarse[cell.X / Step, cell.Y / Step] = value;
    }

    public T this[int x, int y]
    {
        get => this[new GridPos(x, y)];
        set => this[new GridPos(x, y)] = value;
    }

    /// <summary>Ячейка рельефа, в которую попадает строительная клетка.</summary>
    public GridPos CoarseOf(GridPos cell) => new(cell.X / Step, cell.Y / Step);
}

/// <summary>Слой одной природной категории: в каждой ячейке рельефа хранится индекс вида.</summary>
public sealed class TerrainLayer
{
    internal TerrainLayer(TerrainCategory category, int width, int height, int step)
    {
        Category = category;
        Cells = new TerrainGrid<byte>(width, height, step, category.Default.Index);
    }

    public TerrainCategory Category { get; }
    public TerrainGrid<byte> Cells { get; }

    public TerrainKind KindAt(GridPos pos) => Category[Cells[pos]];

    public bool Is(GridPos pos, TypeId kind) => KindAt(pos).Id == kind;

    public void Set(GridPos pos, TypeId kind) => Cells[pos] = Category.Get(kind).Index;
}

/// <summary>
/// Природа карты: высота плюс по слою на каждую категорию из каталога — на крупной сетке рельефа с шагом
/// <see cref="Step"/> строительных клеток (docs/02в: местность — отдельный слой, не связанный со строительными клетками).
/// Высота — целые уровни; сколько метров в уровне, решим вместе с уклонами.
/// </summary>
public sealed class TerrainLayers
{
    private readonly Dictionary<TypeId, TerrainLayer> _layers = new();

    /// <param name="step">Шаг рельефа в строительных клетках; 1 — рельеф в каждой клетке.</param>
    public TerrainLayers(TerrainCatalog catalog, int width, int height, int step = 1)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step), $"Шаг рельефа должен быть положительным: {step}.");
        Catalog = catalog;
        Width = width;
        _rows = height;
        Step = step;
        Height = new TerrainGrid<short>(width, height, step);
        foreach (var category in catalog.Categories)
            _layers.Add(category.Id, new TerrainLayer(category, width, height, step));
    }

    public TerrainCatalog Catalog { get; }

    /// <summary>Сколько строительных клеток по стороне в одной ячейке рельефа.</summary>
    public int Step { get; }

    /// <summary>Ширина карты в строительных клетках.</summary>
    public int Width { get; }

    private readonly int _rows;

    public TerrainGrid<short> Height { get; }
    public IEnumerable<TerrainLayer> Layers => _layers.Values;

    public int Revision => Height.Revision + _layers.Values.Sum(l => l.Cells.Revision);

    public bool TryGet(TypeId category, out TerrainLayer layer) => _layers.TryGetValue(category, out layer!);

    public TerrainLayer this[TypeId category] =>
        TryGet(category, out var layer) ? layer : throw new KeyNotFoundException($"Нет слоя рельефа «{category}».");

    /// <summary>
    /// По одной строительной клетке на каждую ячейку рельефа, задевающую прямоугольник <paramref name="area"/>:
    /// центральная клетка ячейки. Операции карты проверяют фигуру в этих клетках и пишут в ячейку целиком.
    /// </summary>
    public IEnumerable<GridPos> Samples(GridRect area)
    {
        var (x0, y0) = (Math.Max(0, area.X) / Step, Math.Max(0, area.Y) / Step);
        var (x1, y1) = (Math.Min(Width, area.Right) - 1, Math.Min(_rows, area.Bottom) - 1);
        if (x1 < 0 || y1 < 0)
            yield break;
        for (var cy = y0; cy <= y1 / Step; cy++)
        for (var cx = x0; cx <= x1 / Step; cx++)
        {
            var cell = new GridPos(Math.Min(cx * Step + Step / 2, Width - 1), Math.Min(cy * Step + Step / 2, _rows - 1));
            yield return cell;
        }
    }
}
