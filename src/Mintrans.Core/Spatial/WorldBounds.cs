namespace Mintrans.Core.Spatial;

/// <summary>
/// Прямоугольник мира в метрах со сторонами вдоль осей: граница карты, охватывающий прямоугольник фигуры (AABB),
/// квадрат клетки. Минимальные границы входят, максимальные — нет (полуоткрытый, как клетка, docs/02в).
/// </summary>
public readonly record struct WorldBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public static WorldBounds Of(WorldPoint a, WorldPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static WorldBounds Sized(double widthM, double heightM) => new(0, 0, widthM, heightM);

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public WorldPoint Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public WorldBounds Expand(double marginM) => new(MinX - marginM, MinY - marginM, MaxX + marginM, MaxY + marginM);

    public WorldBounds Union(WorldBounds other) =>
        new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public WorldBounds Include(WorldPoint p) =>
        new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public bool Contains(WorldPoint p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    /// <summary>Лежит ли <paramref name="other"/> целиком внутри, с допуском <paramref name="epsilonM"/>.</summary>
    public bool Contains(WorldBounds other, double epsilonM = 0) =>
        other.MinX >= MinX - epsilonM && other.MinY >= MinY - epsilonM &&
        other.MaxX <= MaxX + epsilonM && other.MaxY <= MaxY + epsilonM;

    /// <summary>Есть ли общая область; касание по границе пересечением не считается.</summary>
    public bool Overlaps(WorldBounds other) =>
        MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;

    /// <summary>Пересекаются или касаются (для отбора кандидатов).</summary>
    public bool Touches(WorldBounds other) =>
        MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;

    public override string ToString() => $"[{MinX:0.###}…{MaxX:0.###}] × [{MinY:0.###}…{MaxY:0.###}] м";
}
