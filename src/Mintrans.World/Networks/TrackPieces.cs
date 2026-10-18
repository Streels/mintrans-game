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

    public static IEnumerable<TrackPieces> Enumerate(this TrackPieces pieces) =>
        AllPieces.Where(p => (pieces & p) != 0);
}
