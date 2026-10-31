using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;

namespace Mintrans.World.Networks;

/// <summary>Как сигнал решает, пустить ли поезд (docs/10а, модель OpenTTD).</summary>
public enum SignalLogic
{
    /// <summary>Блочный: зелёный, если следующий участок до сигналов пуст.</summary>
    Block,

    /// <summary>Маршрутный: зелёный, если удалось закрепить путь поезда до следующего безопасного места.</summary>
    Path,
}

/// <summary>
/// Сигнал на куске пути (docs/10а). <paramref name="Faces"/> — стороны куска, в сторону которых сигнал регулирует
/// движение: одна (односторонний) или обе (двусторонний). Сигнал не меняет геометрию и не соединяет пути.
/// </summary>
/// <param name="Kind">Вид сигнала из правил строительства.</param>
public sealed record Signal(GridPos Cell, TrackPieces Piece, TypeId Kind, SignalLogic Logic, DirectionMask Faces)
{
    /// <summary>Регулирует ли сигнал поезд, выезжающий из клетки через сторону <paramref name="exit"/>.</summary>
    public bool Controls(Direction exit) => Faces.Has(exit);

    public bool TwoWay => Faces.Enumerate().Count() == 2;
}

/// <summary>
/// Раскладка «кусками пути» — для железной дороги. Проехать внутри клетки можно только
/// по уложенному куску: повороты и стрелки задаются явно, пересечение двух прямых не допускается (docs/10б).
/// На кусках пути стоят сигналы.
/// </summary>
public sealed class TrackNetworkLayer : NetworkLayer
{
    public const string LayoutName = "tracks";

    private readonly CellLayer<TrackPieces> _pieces;
    private readonly SortedDictionary<(int Y, int X, TrackPieces Piece), Signal> _signals = new();

    public TrackNetworkLayer(TypeId type, int width, int height) : base(type, width, height)
    {
        _pieces = new CellLayer<TrackPieces>(width, height);
    }

    protected override int CellRevision => _pieces.Revision;

    public TrackPieces PiecesAt(GridPos pos) => _pieces[pos];

    public override DirectionMask EdgesAt(GridPos pos)
    {
        var mask = DirectionMask.None;
        foreach (var piece in _pieces[pos].Enumerate())
        {
            var (a, b) = piece.Sides();
            mask |= a.ToMask() | b.ToMask();
        }
        return mask;
    }

    public override bool Connects(GridPos pos, Direction from, Direction to) =>
        from != to && (_pieces[pos] & Tracks.Between(from, to)) != 0;

    /// <summary>
    /// По какому куску едет поезд в клетке. Въезд и выезд известны — кусок между ними.
    /// Известна одна сторона (начало, конец пути или разворот в тупике) — прямой кусок через неё, если есть, иначе первый, что к ней выходит.
    /// </summary>
    public TrackPieces PieceFor(GridPos pos, Direction? entry, Direction? exit)
    {
        var pieces = _pieces[pos];
        if (entry is { } e && exit is { } x && e != x)
            return Tracks.Between(e, x) is var between && (pieces & between) != 0 ? between : TrackPieces.None;
        var side = entry ?? exit;
        if (side is not { } s)
            return pieces.Enumerate().FirstOrDefault();
        var straight = Tracks.StraightThrough(s);
        return (pieces & straight) != 0 ? straight : pieces.Enumerate().FirstOrDefault(p => p.Touches(s));
    }

    public void AddPiece(GridPos pos, TrackPieces piece) => _pieces[pos] |= piece;

    public void RemovePiece(GridPos pos, TrackPieces piece)
    {
        _pieces[pos] &= ~piece;
        foreach (var signal in SignalsAt(pos).Where(s => (s.Piece & piece) != 0).ToList())
            RemoveSignal(signal);
    }

    public override void LayThrough(GridPos pos, Direction a, Direction b) => AddPiece(pos, Tracks.Between(a, b));

    /// <summary>Рельсы не обрываются посреди клетки: в тупике кладётся прямой кусок вдоль стороны въезда.</summary>
    public override void LayEnd(GridPos pos, Direction side) => AddPiece(pos, Tracks.Between(side, side.Opposite()));

    /// <summary>Куски пути соседей остаются: они лежат в своих клетках и просто становятся тупиками.</summary>
    protected override void ClearCellCore(GridPos pos)
    {
        _pieces[pos] = TrackPieces.None;
        foreach (var signal in SignalsAt(pos).ToList())
            RemoveSignal(signal);
    }

    /// <summary>Кусок, который уложит шаг прокладки (для правил строительства).</summary>
    public static TrackPieces PieceOf(PathStep step) => Tracks.Between(step.In, step.Out);

    public override void LayPath(IReadOnlyList<GridPos> path)
    {
        foreach (var step in PlanSteps(path))
            AddPiece(step.Cell, PieceOf(step));
    }

    public override IEnumerable<NetworkStroke> StrokesAt(GridPos pos) =>
        _pieces[pos].Enumerate().Select(piece =>
        {
            var (a, b) = piece.Sides();
            return new NetworkStroke(pos, a, b);
        });

    // ---------- Сигналы ----------

    /// <summary>Все сигналы в постоянном порядке (по строкам карты) — расчёт воспроизводим.</summary>
    public IEnumerable<Signal> Signals => _signals.Values;

    public IEnumerable<Signal> SignalsAt(GridPos pos) =>
        _signals.Where(p => p.Key.Y == pos.Y && p.Key.X == pos.X).Select(p => p.Value);

    public Signal? SignalOn(GridPos pos, TrackPieces piece) => _signals.GetValueOrDefault((pos.Y, pos.X, piece));

    /// <summary>Поставить сигнал на кусок пути; прежний сигнал на этом куске заменяется. Где ставить можно, решают правила строительства.</summary>
    public void SetSignal(Signal signal)
    {
        if ((_pieces[signal.Cell] & signal.Piece) == 0 || signal.Piece.Count() != 1)
            throw new ArgumentException($"В клетке {signal.Cell} нет куска пути {signal.Piece}.");
        if (signal.Faces == DirectionMask.None || signal.Faces.Enumerate().Any(side => !signal.Piece.Touches(side)))
            throw new ArgumentException($"Сигнал на куске {signal.Piece} не может смотреть в сторону {signal.Faces}.");
        _signals[(signal.Cell.Y, signal.Cell.X, signal.Piece)] = signal;
        StructureRevision++;
    }

    public void RemoveSignal(Signal signal)
    {
        if (_signals.Remove((signal.Cell.Y, signal.Cell.X, signal.Piece)))
            StructureRevision++;
    }
}
