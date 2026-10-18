namespace Mintrans.Core.Grid;

/// <summary>
/// Координата клетки на карте. X растёт на восток, Y — на юг.
/// Не зависит от изометрии и вообще от способа отображения.
/// </summary>
public readonly record struct GridPos(int X, int Y)
{
    public static readonly GridPos Zero = new(0, 0);

    public GridPos Step(Direction direction)
    {
        var (dx, dy) = direction.Offset();
        return new GridPos(X + dx, Y + dy);
    }

    /// <summary>Манхэттенское расстояние: число шагов через стороны клеток.</summary>
    public int ManhattanDistance(GridPos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public static GridPos operator +(GridPos a, GridPos b) => new(a.X + b.X, a.Y + b.Y);
    public static GridPos operator -(GridPos a, GridPos b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X}, {Y})";
}
