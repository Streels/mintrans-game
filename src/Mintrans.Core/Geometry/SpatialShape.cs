using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;

namespace Mintrans.Core.Geometry;

/// <summary>
/// Физический габарит объекта в плане (docs/02в): из него вычисляются занятые клетки и проверяются коллизии.
/// Клетки производны от габарита, а не наоборот.
/// </summary>
public abstract class SpatialShape
{
    /// <summary>Охватывающий прямоугольник габарита.</summary>
    public abstract WorldBounds Bounds { get; }

    /// <summary>
    /// Клетки, в которые заходит габарит. Запас на допуск приближения дуг ломаной — миллиметр:
    /// индекс может назвать лишнего кандидата, но не пропустит клетку.
    /// </summary>
    public abstract IReadOnlyCollection<GridPos> TouchedCells();
}

/// <summary>
/// Коридор вдоль оси: рельсовый путь или дорога. Габарит — все точки не дальше <see cref="HalfWidthM"/> от оси.
/// Ось — точная метрическая геометрия, она не обязана проходить через центры клеток.
/// </summary>
public sealed class CorridorShape(PathGeometry axis, double halfWidthM) : SpatialShape
{
    private IReadOnlyCollection<GridPos>? _cells;

    public PathGeometry Axis { get; } = axis;
    public double HalfWidthM { get; } = halfWidthM >= 0 ? halfWidthM : throw new ArgumentOutOfRangeException(nameof(halfWidthM));

    public override WorldBounds Bounds => Axis.Bounds.Expand(HalfWidthM);

    public override IReadOnlyCollection<GridPos> TouchedCells() => _cells ??= ComputeCells(HalfWidthM + PathGeometry.FlattenToleranceM);

    private IReadOnlyCollection<GridPos>? _interior;

    /// <summary>
    /// Клетки, в которые габарит действительно заходит, а не только касается их края: то же, что точная проверка
    /// пересечения с квадратом клетки, но за один проход по оси.
    /// </summary>
    public IReadOnlyCollection<GridPos> CoveredCells() => _interior ??= ComputeCells(HalfWidthM - SpatialScale.EpsilonM);

    private SortedSet<GridPos> ComputeCells(double reach)
    {
        // Для индекса ломаная лежит не дальше допуска от дуги: порог расширен на допуск, чтобы не потерять клетку на изгибе.
        var cells = new SortedSet<GridPos>(GridPosOrder.Instance);
        foreach (var segment in Axis.Flat)
        foreach (var cell in SpatialScale.CellsTouching(segment.Bounds.Expand(reach)))
        {
            if (!cells.Contains(cell) && Distances.SegmentRect(segment.A, segment.B, SpatialScale.CellBounds(cell)) < reach)
                cells.Add(cell);
        }
        return cells;
    }
}

/// <summary>Площадь со сторонами вдоль осей: здание, площадка, модуль предприятия. Обычно выровнена по клеткам.</summary>
public sealed class AreaShape(WorldBounds rect) : SpatialShape
{
    private IReadOnlyCollection<GridPos>? _cells;

    public WorldBounds Rect { get; } = rect.Width > 0 && rect.Height > 0
        ? rect
        : throw new ArgumentException($"У площади должна быть положительная площадь: {rect}.", nameof(rect));

    public override WorldBounds Bounds => Rect;

    public override IReadOnlyCollection<GridPos> TouchedCells() => _cells ??= new SortedSet<GridPos>(
        SpatialScale.CellsTouching(Rect).Where(cell => SpatialScale.CellBounds(cell).Overlaps(Shrunk)), GridPosOrder.Instance);

    /// <summary>Прямоугольник без полосы допуска по краям: касание соседа — не пересечение.</summary>
    internal WorldBounds Shrunk => Rect.Expand(-SpatialScale.EpsilonM);

    /// <summary>Площадь ровно по клеткам прямоугольника <paramref name="cells"/>.</summary>
    public static AreaShape OfCells(GridRect cells) => new(new WorldBounds(
        cells.X * SpatialScale.CellM, cells.Y * SpatialScale.CellM, cells.Right * SpatialScale.CellM, cells.Bottom * SpatialScale.CellM));
}

/// <summary>Порядок клеток по строкам карты: одинаковые данные дают одинаковый порядок обхода.</summary>
public sealed class GridPosOrder : IComparer<GridPos>
{
    public static readonly GridPosOrder Instance = new();

    public int Compare(GridPos a, GridPos b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
}
