namespace Mintrans.Core.Grid;

/// <summary>
/// Четыре стороны клетки. Клетки соединяются только через общую сторону:
/// диагональ строится лесенкой из клеток с угловыми кусками пути, как в OpenTTD (см. docs/02_world_map.md).
/// </summary>
public enum Direction : byte
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

/// <summary>Набор сторон клетки в одном байте: бит N соответствует <see cref="Direction"/> со значением N.</summary>
[Flags]
public enum DirectionMask : byte
{
    None = 0,
    North = 1 << Direction.North,
    East = 1 << Direction.East,
    South = 1 << Direction.South,
    West = 1 << Direction.West,
    All = North | East | South | West,
}

public static class Directions
{
    public const int Count = 4;

    /// <summary>Все стороны по часовой стрелке, начиная с севера.</summary>
    public static readonly IReadOnlyList<Direction> All =
    [
        Direction.North, Direction.East, Direction.South, Direction.West,
    ];

    private static readonly (int Dx, int Dy)[] Offsets = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public static (int Dx, int Dy) Offset(this Direction direction) => Offsets[(int)direction];

    public static Direction Opposite(this Direction direction) => (Direction)(((int)direction + 2) % Count);

    /// <summary>Стороны, лежащие напротив друг друга (север–юг, восток–запад).</summary>
    public static bool IsOpposite(this Direction a, Direction b) => a.Opposite() == b;

    public static DirectionMask ToMask(this Direction direction) => (DirectionMask)(1 << (int)direction);

    public static bool Has(this DirectionMask mask, Direction direction) => (mask & direction.ToMask()) != 0;

    /// <summary>Сторона, через которую клетка <paramref name="from"/> граничит с <paramref name="to"/>, или null.</summary>
    public static Direction? Between(GridPos from, GridPos to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        for (var i = 0; i < Count; i++)
        {
            if (Offsets[i].Dx == dx && Offsets[i].Dy == dy)
                return (Direction)i;
        }
        return null;
    }

    public static IEnumerable<Direction> Enumerate(this DirectionMask mask)
    {
        for (var i = 0; i < Count; i++)
        {
            if (((int)mask & (1 << i)) != 0)
                yield return (Direction)i;
        }
    }
}
