using Mintrans.Core.Grid;

namespace Mintrans.Core.Spatial;

/// <summary>Адрес индексного чанка: квадрат <see cref="SpatialScale.ChunkCells"/>×<see cref="SpatialScale.ChunkCells"/> клеток.</summary>
public readonly record struct ChunkPos(int X, int Y)
{
    public override string ToString() => $"чанк ({X}, {Y})";
}

/// <summary>Клетка внутри чанка: 0 ≤ X, Y &lt; <see cref="SpatialScale.ChunkCells"/>.</summary>
public readonly record struct LocalCell(int X, int Y);

/// <summary>Клетка на высотном уровне: 0 — земля, 1 и выше — мост, −1 и ниже — тоннель.</summary>
public readonly record struct CellKey(int X, int Y, int Level)
{
    public GridPos Cell => new(X, Y);
}

/// <summary>
/// Масштаб строительной сетки (docs/02в): клетка 5×5 м с общей для всего мира привязкой, индексные чанки по 64×64 клетки.
/// Клетка — только адрес. Перевод точки в клетку — математический floor, корректный и на отрицательных координатах.
/// </summary>
public static class SpatialScale
{
    public const double CellM = 5.0;

    /// <summary>Технический размер чанка индекса; меняется без изменения игровых правил.</summary>
    public const int ChunkCells = 64;

    /// <summary>Общий геометрический допуск сравнения точек, м.</summary>
    public const double EpsilonM = 1e-4;

    public static GridPos ToCell(WorldPoint p) => new((int)Math.Floor(p.X / CellM), (int)Math.Floor(p.Y / CellM));

    public static ChunkPos ToChunk(GridPos cell) => new(FloorDiv(cell.X, ChunkCells), FloorDiv(cell.Y, ChunkCells));

    public static LocalCell ToLocal(GridPos cell) => new(FloorMod(cell.X, ChunkCells), FloorMod(cell.Y, ChunkCells));

    public static GridPos FromLocal(ChunkPos chunk, LocalCell local) =>
        new(chunk.X * ChunkCells + local.X, chunk.Y * ChunkCells + local.Y);

    /// <summary>Квадрат клетки в метрах.</summary>
    public static WorldBounds CellBounds(GridPos cell) =>
        new(cell.X * CellM, cell.Y * CellM, (cell.X + 1) * CellM, (cell.Y + 1) * CellM);

    public static WorldPoint CellCenter(GridPos cell) => new((cell.X + 0.5) * CellM, (cell.Y + 0.5) * CellM);

    public static WorldBounds ChunkBounds(ChunkPos chunk) =>
        new(chunk.X * ChunkCells * CellM, chunk.Y * ChunkCells * CellM,
            (chunk.X + 1) * ChunkCells * CellM, (chunk.Y + 1) * ChunkCells * CellM);

    /// <summary>Все клетки, квадраты которых пересекают или касаются прямоугольника.</summary>
    public static IEnumerable<GridPos> CellsTouching(WorldBounds bounds)
    {
        var (x0, y0) = ((int)Math.Floor(bounds.MinX / CellM), (int)Math.Floor(bounds.MinY / CellM));
        var (x1, y1) = ((int)Math.Floor(bounds.MaxX / CellM), (int)Math.Floor(bounds.MaxY / CellM));
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            yield return new GridPos(x, y);
    }

    public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    public static int FloorMod(int a, int b) => a - FloorDiv(a, b) * b;
}
