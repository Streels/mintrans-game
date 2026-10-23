using Mintrans.Core.Grid;
using Mintrans.Core.Ids;

namespace Mintrans.World.Networks;

public readonly record struct BridgeId(int Value)
{
    public override string ToString() => $"bridge#{Value}";
}

/// <summary>
/// Мост (docs/10б): прямой участок сети между двумя опорами. Опоры стоят на земле, пролёт идёт над клетками
/// между ними, и под ним может проходить другая линия, дорога или вода. С земли на пролёт не попасть:
/// въезд на мост — только через опору с её тыльной стороны.
/// </summary>
/// <param name="Kind">Вид моста из правил строительства.</param>
public sealed record Bridge(BridgeId Id, TypeId Kind, GridPos A, GridPos B)
{
    /// <summary>Сторона опоры <see cref="A"/>, обращённая к пролёту.</summary>
    public Direction Axis => Directions.Between(A, new GridPos(A.X + Math.Sign(B.X - A.X), A.Y + Math.Sign(B.Y - A.Y)))
        ?? throw new InvalidOperationException($"Опоры моста {A} и {B} не на одной прямой.");

    public int Length => Math.Abs(B.X - A.X) + Math.Abs(B.Y - A.Y) + 1;

    public bool IsHead(GridPos cell) => cell == A || cell == B;

    /// <summary>Сторона опоры, обращённая к пролёту.</summary>
    public Direction SpanSide(GridPos head) =>
        head == A ? Axis : head == B ? Axis.Opposite() : throw new ArgumentException($"{head} — не опора моста {Id}.");

    /// <summary>Тыльная сторона опоры — через неё мост стыкуется с сетью на земле.</summary>
    public Direction BackSide(GridPos head) => SpanSide(head).Opposite();

    /// <summary>Клетки под пролётом (без опор).</summary>
    public IEnumerable<GridPos> Span()
    {
        var cell = A.Step(Axis);
        while (cell != B)
        {
            yield return cell;
            cell = cell.Step(Axis);
        }
    }

    public bool IsOver(GridPos cell) =>
        !IsHead(cell) && (A.X == B.X
            ? cell.X == A.X && cell.Y > Math.Min(A.Y, B.Y) && cell.Y < Math.Max(A.Y, B.Y)
            : cell.Y == A.Y && cell.X > Math.Min(A.X, B.X) && cell.X < Math.Max(A.X, B.X));
}

/// <summary>
/// Переход по сети: куда попадёт техника, выехав из клетки через сторону.
/// <paramref name="Elevated"/> — клетка пролёта моста (не путь на земле под ним).
/// </summary>
public readonly record struct NetworkLink(GridPos Cell, Direction Entry, bool Elevated);
