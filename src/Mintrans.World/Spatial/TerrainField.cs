using Mintrans.Core.Geometry;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;

namespace Mintrans.World.Spatial;

/// <summary>Рельеф в точке: высота и вид по каждой природной категории (поверхность, вода, лес, месторождение).</summary>
public sealed record TerrainSample(double HeightM, IReadOnlyDictionary<TypeId, TypeId> Kinds)
{
    public TypeId? KindOf(TypeId category) => Kinds.TryGetValue(category, out var kind) ? kind : (TypeId?)null;
}

/// <summary>
/// Местность — отдельный пространственный слой (docs/02в, раздел 5), не связанный со строительными клетками:
/// её можно спросить в любой точке мира, даже там, где никогда ничего не строили.
/// </summary>
public interface ITerrainField
{
    TerrainSample Sample(WorldPoint p);
}

/// <summary>Ровная однородная местность — для тестов и пустых карт.</summary>
public sealed class FlatTerrain(IReadOnlyDictionary<TypeId, TypeId>? kinds = null, double heightM = 0) : ITerrainField
{
    private readonly TerrainSample _sample = new(heightM, kinds ?? new Dictionary<TypeId, TypeId>());

    public TerrainSample Sample(WorldPoint p) => _sample;
}

/// <summary>
/// Местность на крупной сетке со своим шагом (например, 100 м), независимой от строительной сетки 5 м.
/// Виды — по ближайшей крупной ячейке, высота — билинейно между узлами. Шаг и плотность — параметры сценария.
/// </summary>
public sealed class GridTerrain : ITerrainField
{
    private readonly int _width, _height;
    private readonly double[] _heights;
    private readonly Dictionary<TypeId, TypeId?[]> _kinds = new();
    private readonly IReadOnlyDictionary<TypeId, TypeId> _defaults;

    public GridTerrain(WorldBounds bounds, double cellM, IReadOnlyDictionary<TypeId, TypeId>? defaults = null)
    {
        if (cellM <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellM));
        Bounds = bounds;
        CellM = cellM;
        _width = Math.Max(1, (int)Math.Ceiling(bounds.Width / cellM));
        _height = Math.Max(1, (int)Math.Ceiling(bounds.Height / cellM));
        _heights = new double[(_width + 1) * (_height + 1)];
        _defaults = defaults ?? new Dictionary<TypeId, TypeId>();
    }

    public WorldBounds Bounds { get; }
    public double CellM { get; }
    public int Width => _width;
    public int Height => _height;

    /// <summary>Крупная ячейка, в которую попадает точка (с ограничением краем карты).</summary>
    public (int X, int Y) CellOf(WorldPoint p) => (
        Math.Clamp((int)Math.Floor((p.X - Bounds.MinX) / CellM), 0, _width - 1),
        Math.Clamp((int)Math.Floor((p.Y - Bounds.MinY) / CellM), 0, _height - 1));

    public void SetKind(int x, int y, TypeId category, TypeId? kind)
    {
        if (!_kinds.TryGetValue(category, out var layer))
            _kinds[category] = layer = new TypeId?[_width * _height];
        layer[y * _width + x] = kind;
    }

    /// <summary>Высота узла крупной сетки (узлов на один больше, чем ячеек, по каждой оси).</summary>
    public void SetNodeHeight(int x, int y, double heightM) => _heights[y * (_width + 1) + x] = heightM;

    public TerrainSample Sample(WorldPoint p)
    {
        var (cx, cy) = CellOf(p);
        var kinds = new Dictionary<TypeId, TypeId>(_defaults);
        foreach (var (category, layer) in _kinds)
            if (layer[cy * _width + cx] is { } kind)
                kinds[category] = kind;
        return new TerrainSample(HeightAt(p), kinds);
    }

    private double HeightAt(WorldPoint p)
    {
        var gx = Math.Clamp((p.X - Bounds.MinX) / CellM, 0, _width);
        var gy = Math.Clamp((p.Y - Bounds.MinY) / CellM, 0, _height);
        var (x0, y0) = (Math.Min((int)gx, _width - 1), Math.Min((int)gy, _height - 1));
        var (tx, ty) = (gx - x0, gy - y0);
        double H(int x, int y) => _heights[y * (_width + 1) + x];
        var top = H(x0, y0) * (1 - tx) + H(x0 + 1, y0) * tx;
        var bottom = H(x0, y0 + 1) * (1 - tx) + H(x0 + 1, y0 + 1) * tx;
        return top * (1 - ty) + bottom * ty;
    }
}

/// <summary>Точки, в которых проверяется местность под габаритом.</summary>
public static class TerrainProbe
{
    /// <summary>Ось коридора — с шагом <paramref name="stepM"/>; площадь — сеткой точек с тем же шагом, включая края.</summary>
    public static IEnumerable<WorldPoint> Points(SpatialShape shape, double stepM)
    {
        switch (shape)
        {
            case CorridorShape corridor:
                var count = Math.Max(1, (int)Math.Ceiling(corridor.Axis.Length / stepM));
                for (var i = 0; i <= count; i++)
                    yield return corridor.Axis.PointAt(corridor.Axis.Length * i / count);
                break;
            case AreaShape area:
                var nx = Math.Max(1, (int)Math.Ceiling(area.Rect.Width / stepM));
                var ny = Math.Max(1, (int)Math.Ceiling(area.Rect.Height / stepM));
                for (var y = 0; y <= ny; y++)
                for (var x = 0; x <= nx; x++)
                    yield return new WorldPoint(area.Rect.MinX + area.Rect.Width * x / nx, area.Rect.MinY + area.Rect.Height * y / ny);
                break;
            default:
                throw new NotSupportedException($"Нет проверки местности для {shape.GetType().Name}.");
        }
    }
}
