using Mintrans.Core.Grid;
using Mintrans.Core.Ids;

namespace Mintrans.World.Networks;

/// <summary>
/// Отрезок сети внутри одной клетки — для отрисовки и описания.
/// <paramref name="From"/> = null означает «от центра клетки» (тупик или перекрёсток дороги).
/// </summary>
public readonly record struct NetworkStroke(GridPos Cell, Direction? From, Direction To);

/// <summary>
/// Шаг прокладки через клетку. <see cref="InOpen"/>/<see cref="OutOpen"/> — сторона ни с чем не стыкуется
/// (начало или конец пути «в чистом поле»): рельсы всё равно кладут прямой кусок, дорога делает тупик.
/// </summary>
public readonly record struct PathStep(GridPos Cell, Direction In, Direction Out, bool InOpen, bool OutOpen);

/// <summary>
/// Транспортная сеть на сетке. Клетки связаны только через общие стороны:
/// путь проходит, если обе клетки выходят на общую сторону.
/// Как сеть устроена внутри клетки, решает конкретная раскладка
/// (<see cref="EdgeNetworkLayer"/> для дорог, <see cref="TrackNetworkLayer"/> для рельсов).
/// Мосты (docs/10б) — часть сети: опоры лежат в клетках, пролёт соединяет их над клетками между ними.
/// </summary>
public abstract class NetworkLayer
{
    private readonly List<Bridge> _bridges = new();
    private readonly Dictionary<GridPos, Bridge> _heads = new();
    private int _nextBridge = 1;

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

    /// <summary>Растёт при каждом изменении — по нему отображение и транспорт понимают, что сеть изменилась.</summary>
    public int Revision => CellRevision + StructureRevision;

    /// <summary>Изменения содержимого клеток.</summary>
    protected abstract int CellRevision { get; }

    /// <summary>Изменения сооружений сети: мостов, сигналов.</summary>
    protected int StructureRevision { get; set; }

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

    /// <summary>
    /// Тупик: сеть в клетке выходит только к стороне <paramref name="side"/> (въезд в депо или гараж)
    /// и стыкуется с сетью соседа за этой стороной, если та есть.
    /// </summary>
    public abstract void LayEnd(GridPos pos, Direction side);

    /// <summary>
    /// Убрать сеть из клетки целиком (снос). Связи соседей с этой клеткой тоже убираются.
    /// Снос опоры убирает весь мост вместе со второй опорой.
    /// </summary>
    public void ClearCell(GridPos pos)
    {
        if (_heads.TryGetValue(pos, out var bridge))
        {
            RemoveBridge(bridge);
            ClearCellCore(bridge.A);
            ClearCellCore(bridge.B);
            return;
        }
        ClearCellCore(pos);
    }

    protected abstract void ClearCellCore(GridPos pos);

    /// <summary>Как нарисовать сеть в клетке.</summary>
    public abstract IEnumerable<NetworkStroke> StrokesAt(GridPos pos);

    /// <summary>Клетки, где есть сеть, в постоянном порядке — без обхода всей карты.</summary>
    public abstract IEnumerable<GridPos> OccupiedCells();

    public IEnumerable<NetworkStroke> Strokes() => OccupiedCells().SelectMany(StrokesAt);

    // ---------- Мосты ----------

    public IReadOnlyList<Bridge> Bridges => _bridges;

    /// <summary>Мост, у которого в клетке опора, или null.</summary>
    public Bridge? BridgeAt(GridPos head) => _heads.GetValueOrDefault(head);

    /// <summary>Мост, пролёт которого проходит над клеткой, или null.</summary>
    public Bridge? BridgeOver(GridPos cell) => _bridges.FirstOrDefault(b => b.IsOver(cell));

    /// <summary>
    /// Построить мост между опорами на одной прямой. В опорах кладётся прямой участок вдоль моста;
    /// проверки места (что под пролётом, свободны ли опоры) — дело правил строительства.
    /// </summary>
    public Bridge AddBridge(TypeId kind, GridPos a, GridPos b)
    {
        if (!Contains(a) || !Contains(b))
            throw new ArgumentOutOfRangeException(nameof(a), $"Опоры моста {a}–{b} вне карты {Width}x{Height}.");
        if (a.X != b.X && a.Y != b.Y)
            throw new ArgumentException($"Мост {a}–{b} должен идти по прямой.");
        if (a.ManhattanDistance(b) < 2)
            throw new ArgumentException($"Между опорами моста {a}–{b} нет пролёта.");
        if (_heads.ContainsKey(a) || _heads.ContainsKey(b))
            throw new ArgumentException($"В клетке {(_heads.ContainsKey(a) ? a : b)} уже стоит опора моста.");

        var bridge = new Bridge(new BridgeId(_nextBridge++), kind, a, b);
        _bridges.Add(bridge);
        _heads[a] = bridge;
        _heads[b] = bridge;
        foreach (var head in new[] { a, b })
            LayThrough(head, bridge.BackSide(head), bridge.SpanSide(head));
        StructureRevision++;
        return bridge;
    }

    private void RemoveBridge(Bridge bridge)
    {
        _bridges.Remove(bridge);
        _heads.Remove(bridge.A);
        _heads.Remove(bridge.B);
        StructureRevision++;
    }

    // ---------- Обход сети ----------

    /// <summary>
    /// Куда попадает техника, выехав из клетки через сторону <paramref name="exit"/>; null — дальше сети нет.
    /// По земле — в соседнюю клетку, если её сеть выходит навстречу. С опоры к пролёту — на мост, по мосту — до второй опоры.
    /// На опору нельзя попасть с земли со стороны пролёта: там клетка под мостом, а не продолжение пути.
    /// </summary>
    public NetworkLink? Follow(GridPos cell, Direction exit, bool elevated = false)
    {
        var next = cell.Step(exit);
        if (!Contains(next))
            return null;
        if (elevated)
        {
            if (BridgeOver(cell) is not { } over || (exit != over.Axis && exit != over.Axis.Opposite()))
                return null;
            return new NetworkLink(next, exit.Opposite(), !over.IsHead(next));
        }
        if (!EdgesAt(cell).Has(exit))
            return null;
        if (BridgeAt(cell) is { } own && own.SpanSide(cell) == exit)
            return new NetworkLink(next, exit.Opposite(), true);
        if (!EdgesAt(next).Has(exit.Opposite()))
            return null;
        if (BridgeAt(next) is { } bridge && bridge.SpanSide(next) == exit.Opposite())
            return null;
        return new NetworkLink(next, exit.Opposite(), false);
    }

    /// <summary>
    /// Через какие стороны можно выехать из клетки, въехав через <paramref name="entry"/>
    /// (null — техника стоит в клетке и может ехать в любую сторону). На пролёте — только прямо.
    /// </summary>
    public IEnumerable<Direction> ExitsFrom(GridPos cell, Direction? entry, bool elevated = false)
    {
        if (elevated)
        {
            if (entry is { } e)
                yield return e.Opposite();
            else if (BridgeOver(cell) is { } bridge)
            {
                yield return bridge.Axis;
                yield return bridge.Axis.Opposite();
            }
            yield break;
        }
        foreach (var side in EdgesAt(cell).Enumerate())
        {
            if (entry is not { } from || (side != from && Connects(cell, from, side)))
                yield return side;
        }
    }

    // ---------- Прокладка ----------

    /// <summary>
    /// Проверяет цепочку и возвращает для каждой клетки сторону входа и выхода — то, что уложит <see cref="LayPath"/>.
    /// Концы пути пристыковываются к уже лежащей сети, как в OpenTTD: если в крайней клетке или
    /// у соседней клетки уже есть сеть, путь поворачивает к ней, а не уходит прямо мимо.
    /// </summary>
    public IReadOnlyList<PathStep> PlanSteps(IReadOnlyList<GridPos> path)
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

        var result = new List<PathStep>(path.Count);
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
            result.Add(new PathStep(path[i], entry, exit, inOpen, outOpen));
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
    protected bool IsLinkedAcross(GridPos cell, Direction side) => Follow(cell, side) is { Elevated: false };

    /// <summary>
    /// Можно ли пристыковаться к сети соседней клетки со стороны <paramref name="side"/>.
    /// Рельсы — только если у соседа путь уже смотрит сюда; дорога — к любой клетке с дорогой.
    /// К опоре моста — только с тыльной стороны.
    /// </summary>
    protected bool AcceptsJoin(GridPos neighbor, Direction side) =>
        BridgeAt(neighbor) is { } bridge ? bridge.BackSide(neighbor) == side : AcceptsJoinCore(neighbor, side);

    protected virtual bool AcceptsJoinCore(GridPos neighbor, Direction side) => EdgesAt(neighbor).Has(side);
}
