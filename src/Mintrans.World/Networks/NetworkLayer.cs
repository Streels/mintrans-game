using Mintrans.Core.Grid;
using Mintrans.Core.Ids;

namespace Mintrans.World.Networks;

/// <summary>
/// Отрезок сети внутри одной клетки — для отрисовки и описания.
/// <paramref name="From"/> = null означает «от центра клетки» (тупик или перекрёсток дороги).
/// </summary>
public readonly record struct NetworkStroke(GridPos Cell, Direction? From, Direction To);

/// <summary>
/// Транспортная сеть на сетке. Клетки связаны только через общие стороны:
/// путь проходит, если обе клетки выходят на общую сторону.
/// Как сеть устроена внутри клетки, решает конкретная раскладка
/// (<see cref="EdgeNetworkLayer"/> для дорог, <see cref="TrackNetworkLayer"/> для рельсов).
/// </summary>
public abstract class NetworkLayer
{
    protected NetworkLayer(TypeId type, int width, int height)
    {
        Type = type;
        Width = width;
        Height = height;
    }

    public TypeId Type { get; }
    public int Width { get; }
    public int Height { get; }
    public GridRect Bounds => new(0, 0, Width, Height);

    /// <summary>Растёт при каждом изменении — по нему отображение понимает, что пора перерисовать.</summary>
    public abstract int Revision { get; }

    public bool Contains(GridPos pos) => Bounds.Contains(pos);

    /// <summary>На какие стороны клетки выходит сеть.</summary>
    public abstract DirectionMask EdgesAt(GridPos pos);

    public bool HasAny(GridPos pos) => EdgesAt(pos) != DirectionMask.None;

    /// <summary>Можно ли внутри клетки проехать от стороны <paramref name="from"/> к стороне <paramref name="to"/>.</summary>
    public abstract bool Connects(GridPos pos, Direction from, Direction to);

    /// <summary>Проложить сеть по цепочке соседних клеток (каждая следующая — через сторону от предыдущей).</summary>
    public abstract void LayPath(IReadOnlyList<GridPos> path);

    /// <summary>Провести сеть сквозь одну клетку от стороны <paramref name="a"/> к стороне <paramref name="b"/> (например, через станцию).</summary>
    public abstract void LayThrough(GridPos pos, Direction a, Direction b);

    /// <summary>Убрать сеть из клетки целиком (снос). Связи соседей с этой клеткой тоже убираются.</summary>
    public abstract void ClearCell(GridPos pos);

    /// <summary>Как нарисовать сеть в клетке.</summary>
    public abstract IEnumerable<NetworkStroke> StrokesAt(GridPos pos);

    public IEnumerable<NetworkStroke> Strokes()
    {
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var pos = new GridPos(x, y);
            if (HasAny(pos))
            {
                foreach (var stroke in StrokesAt(pos))
                    yield return stroke;
            }
        }
    }

    /// <summary>
    /// Шаг прокладки через клетку. <see cref="InOpen"/>/<see cref="OutOpen"/> — сторона ни с чем не стыкуется
    /// (начало или конец пути «в чистом поле»): рельсы всё равно кладут прямой кусок, дорога делает тупик.
    /// </summary>
    protected readonly record struct WalkStep(GridPos Cell, Direction In, Direction Out, bool InOpen, bool OutOpen);

    /// <summary>
    /// Проверяет цепочку и возвращает для каждой клетки сторону входа и выхода.
    /// Концы пути пристыковываются к уже лежащей сети, как в OpenTTD: если в крайней клетке или
    /// у соседней клетки уже есть сеть, путь поворачивает к ней, а не уходит прямо мимо.
    /// </summary>
    protected IReadOnlyList<WalkStep> Walk(IReadOnlyList<GridPos> path)
    {
        if (path.Count < 2)
            throw new ArgumentException("Путь должен содержать хотя бы две клетки.");
        foreach (var cell in path)
        {
            if (!Contains(cell))
                throw new ArgumentOutOfRangeException(nameof(path), $"Клетка {cell} вне карты {Width}x{Height}.");
        }

        var steps = new Direction[path.Count - 1];
        for (var i = 0; i + 1 < path.Count; i++)
        {
            steps[i] = Directions.Between(path[i], path[i + 1])
                ?? throw new ArgumentException($"Клетки {path[i]} и {path[i + 1]} не имеют общей стороны.");
        }

        var result = new List<WalkStep>(path.Count);
        for (var i = 0; i < path.Count; i++)
        {
            var (entry, exit, inOpen, outOpen) = (default(Direction), default(Direction), false, false);
            if (i > 0)
                entry = steps[i - 1].Opposite();
            if (i < steps.Length)
                exit = steps[i];

            if (i == 0)
            {
                var snap = SnapSide(path[i], exit, path.Count > 1 ? path[1] : null);
                (entry, inOpen) = snap is { } s ? (s, false) : (exit.Opposite(), true);
            }
            if (i == path.Count - 1)
            {
                var snap = SnapSide(path[i], entry, path[i - 1]);
                (exit, outOpen) = snap is { } s ? (s, false) : (entry.Opposite(), true);
            }
            if (entry == exit)
                throw new ArgumentException($"Путь разворачивается на месте в клетке {path[i]}.");
            result.Add(new WalkStep(path[i], entry, exit, inOpen, outOpen));
        }
        return result;
    }

    /// <summary>
    /// К какой стороне пристыковать край пути, у которого уже занята сторона <paramref name="used"/>.
    /// Сначала — к сети в самой клетке (продолжить тупик или сделать стрелку), затем — к соседу,
    /// чья сеть смотрит на эту клетку. Прямое продолжение проверяется первым. Null — стыковаться не с чем.
    /// </summary>
    private Direction? SnapSide(GridPos cell, Direction used, GridPos? pathNeighbor)
    {
        var candidates = new[] { used.Opposite() }
            .Concat(Directions.All.Where(d => d != used && d != used.Opposite()))
            .ToList();

        var own = EdgesAt(cell);
        if (own != DirectionMask.None)
        {
            // Своя сеть в клетке: сначала сторона, через которую она реально связана с соседом,
            // а не «висящий» конец тупика.
            var linked = candidates.Where(side => own.Has(side) && IsLinkedAcross(cell, side)).ToList();
            if (linked.Count > 0)
                return linked[0];
            return candidates.Where(side => own.Has(side)).Cast<Direction?>().FirstOrDefault();
        }
        foreach (var side in candidates)
        {
            var neighbor = cell.Step(side);
            if (neighbor != pathNeighbor && Contains(neighbor) && AcceptsJoin(neighbor, side.Opposite()))
                return side;
        }
        return null;
    }

    /// <summary>Выходит ли сеть из клетки через сторону и встречает ли там сеть соседа.</summary>
    protected bool IsLinkedAcross(GridPos cell, Direction side)
    {
        var neighbor = cell.Step(side);
        return EdgesAt(cell).Has(side) && Contains(neighbor) && EdgesAt(neighbor).Has(side.Opposite());
    }

    /// <summary>
    /// Можно ли пристыковаться к сети соседней клетки со стороны <paramref name="side"/>.
    /// Рельсы — только если у соседа путь уже смотрит сюда; дорога — к любой клетке с дорогой.
    /// </summary>
    protected virtual bool AcceptsJoin(GridPos neighbor, Direction side) => EdgesAt(neighbor).Has(side);
}
