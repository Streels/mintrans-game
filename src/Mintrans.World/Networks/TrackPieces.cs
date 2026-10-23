using Mintrans.Core.Grid;

namespace Mintrans.World.Networks;

/// <summary>
/// Куски пути внутри клетки, как в OpenTTD: каждый соединяет две стороны.
/// Прямые — через центр (север–юг, восток–запад), угловые — между соседними сторонами.
/// Лесенка угловых кусков даёт диагональ. Несколько кусков в клетке — стрелка или пересечение.
/// </summary>
[Flags]
public enum TrackPieces : byte
{
    None = 0,
    NorthSouth = 1 << 0,
    EastWest = 1 << 1,
    NorthEast = 1 << 2,
    SouthEast = 1 << 3,
    SouthWest = 1 << 4,
    NorthWest = 1 << 5,
}

public static class Tracks
{
    public static readonly IReadOnlyList<TrackPieces> AllPieces =
    [
        TrackPieces.NorthSouth, TrackPieces.EastWest,
        TrackPieces.NorthEast, TrackPieces.SouthEast, TrackPieces.SouthWest, TrackPieces.NorthWest,
    ];

    /// <summary>Две стороны, которые соединяет кусок.</summary>
    public static (Direction A, Direction B) Sides(this TrackPieces piece) => piece switch
    {
        TrackPieces.NorthSouth => (Direction.North, Direction.South),
        TrackPieces.EastWest => (Direction.East, Direction.West),
        TrackPieces.NorthEast => (Direction.North, Direction.East),
        TrackPieces.SouthEast => (Direction.South, Direction.East),
        TrackPieces.SouthWest => (Direction.South, Direction.West),
        TrackPieces.NorthWest => (Direction.North, Direction.West),
        _ => throw new ArgumentException($"«{piece}» — не одиночный кусок пути."),
    };

    /// <summary>Кусок, соединяющий две разные стороны.</summary>
    public static TrackPieces Between(Direction a, Direction b) =>
        AllPieces.FirstOrDefault(p => p.Sides() == (a, b) || p.Sides() == (b, a)) is var piece && piece != TrackPieces.None
            ? piece
            : throw new ArgumentException($"Нельзя соединить сторону {a} саму с собой.");

    public static bool IsStraight(this TrackPieces piece) => piece is TrackPieces.NorthSouth or TrackPieces.EastWest;

    /// <summary>Прямой кусок вдоль стороны: север или юг — «север–юг», восток или запад — «восток–запад».</summary>
    public static TrackPieces StraightThrough(Direction side) => Between(side, side.Opposite());

    public static IEnumerable<TrackPieces> Enumerate(this TrackPieces pieces) =>
        AllPieces.Where(p => (pieces & p) != 0);

    public static int Count(this TrackPieces pieces) => System.Numerics.BitOperations.PopCount((uint)pieces);

    /// <summary>
    /// Допустима ли конфигурация клетки (docs/10б). Куски, сходящиеся у одной стороны, — стрелка;
    /// параллельные угловые — два независимых пути. Две прямые пересекаются в центре клетки —
    /// это пересечение линий в одном уровне, для него нужен мост.
    /// </summary>
    public static bool IsValid(this TrackPieces pieces) =>
        (pieces & (TrackPieces.NorthSouth | TrackPieces.EastWest)) != (TrackPieces.NorthSouth | TrackPieces.EastWest);

    /// <summary>Два параллельных угловых куска: лежат в одной клетке, не касаясь друг друга.</summary>
    public static bool IsParallelPair(this TrackPieces pieces) =>
        pieces is (TrackPieces.NorthEast | TrackPieces.SouthWest) or (TrackPieces.NorthWest | TrackPieces.SouthEast);

    /// <summary>Стрелка: несколько кусков, которые сходятся у общей стороны клетки.</summary>
    public static bool IsJunction(this TrackPieces pieces) => pieces.Count() > 1 && !pieces.IsParallelPair();

    /// <summary>Мешают ли куски друг другу в одной клетке (как в OpenTTD: свободны только параллельные угловые).</summary>
    public static bool Overlap(TrackPieces a, TrackPieces b) => (a & b) != 0 || !(a | b).IsParallelPair();

    /// <summary>Сторона куска напротив <paramref name="side"/>.</summary>
    public static Direction OtherSide(this TrackPieces piece, Direction side)
    {
        var (a, b) = piece.Sides();
        return side == a ? b : side == b ? a : throw new ArgumentException($"Кусок {piece} не выходит к стороне {side}.");
    }

    public static bool Touches(this TrackPieces piece, Direction side)
    {
        var (a, b) = piece.Sides();
        return a == side || b == side;
    }
}
