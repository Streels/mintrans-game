namespace Mintrans.Core.Grid;

/// <summary>Прямоугольник из клеток: левый верхний угол и размер. Правая и нижняя границы не включаются.</summary>
public readonly record struct GridRect
{
    public GridRect(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Размер прямоугольника должен быть положительным: {width}x{height}.");
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int Area => Width * Height;
    public GridPos Origin => new(X, Y);
    public GridPos Center => new(X + Width / 2, Y + Height / 2);

    public bool Contains(GridPos pos) => pos.X >= X && pos.X < Right && pos.Y >= Y && pos.Y < Bottom;

    public bool Contains(GridRect other) =>
        other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;

    public bool Intersects(GridRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public IEnumerable<GridPos> Cells()
    {
        for (var y = Y; y < Bottom; y++)
        for (var x = X; x < Right; x++)
            yield return new GridPos(x, y);
    }

    public override string ToString() => $"[{X}, {Y}, {Width}x{Height}]";
}
