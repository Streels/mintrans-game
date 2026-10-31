using Mintrans.Core.Grid;
using Mintrans.World.Networks;

namespace Mintrans.Transport.Routing;

/// <summary>
/// Точка маршрута: клетка, сторона въезда (null — техника стоит здесь с начала и может ехать куда угодно),
/// сторона выезда (null — конец маршрута) и на пролёте ли моста клетка. Въезд и выезд в одну сторону — разворот в тупике.
/// </summary>
public readonly record struct RoutePoint(GridPos Cell, Direction? Entry, Direction? Exit, bool Elevated = false)
{
    public bool IsReversal => Entry is { } e && Exit == e;

    /// <summary>Та же точка при движении в обратную сторону.</summary>
    public RoutePoint Reversed() => new(Cell, Exit, Entry, Elevated);

    public override string ToString() => $"{Cell}{(Elevated ? "^" : "")} {Entry?.ToString() ?? "·"}→{Exit?.ToString() ?? "·"}";
}

/// <summary>Настройки поиска пути. Цена — ожидаемое время в часах (docs/12).</summary>
public sealed class RouteOptions
{
    /// <summary>Можно ли заезжать в клетку (например, нельзя сквозь чужое депо).</summary>
    public Func<GridPos, bool>? Passable { get; init; }

    /// <summary>
    /// Дополнительная ожидаемая задержка шага из точки в следующую клетку, ч (занятый участок, сигнал).
    /// Null — шаг запрещён (например, против одностороннего маршрутного сигнала).
    /// </summary>
    public Func<RoutePoint, NetworkLink, double?>? StepDelay { get; init; }

    /// <summary>Время проезда одной клетки, ч.</summary>
    public double CellHours { get; init; } = 1;

    /// <summary>Доплата за разворот в тупике, ч.</summary>
    public double ReverseHours { get; init; }

    /// <summary>Проехать через цель до её дальнего края: поезд встаёт на платформу целиком.</summary>
    public bool RunThrough { get; init; }
}

/// <summary>
/// Поиск пути с минимальным ожидаемым временем (docs/12). Учитывает устройство клетки: по рельсам можно свернуть
/// только по уложенному куску; мосты соединяют только свои опоры. Стоящая техника может ехать в любую сторону;
/// в пути разворот возможен только в тупике.
/// </summary>
public static class Pathfinder
{
    private readonly record struct State(GridPos Cell, Direction? Entry, bool Elevated);

    /// <summary>
    /// Маршрут от <paramref name="start"/> (его <see cref="RoutePoint.Entry"/> — откуда техника въехала; выезд не учитывается)
    /// до одной из клеток <paramref name="targets"/>. Null, если пути нет.
    /// </summary>
    public static List<RoutePoint>? Find(NetworkLayer network, RoutePoint start, IReadOnlySet<GridPos> targets, RouteOptions? options = null) =>
        Search(network, start, targets, options)?.Points;

    /// <summary>Маршрут и его ожидаемое время в часах (docs/12).</summary>
    public sealed record Route(List<RoutePoint> Points, double Hours);

    public static Route? Search(NetworkLayer network, RoutePoint start, IReadOnlySet<GridPos> targets, RouteOptions? options = null)
    {
        options ??= new RouteOptions();
        if (!network.Contains(start.Cell) || (!start.Elevated && !network.HasAny(start.Cell)) || targets.Count == 0)
            return null;
        if (targets.Contains(start.Cell) && !start.Elevated && !options.RunThrough)
            return new Route([start with { Exit = null }], 0);

        var origin = new State(start.Cell, start.Entry, start.Elevated);
        var cost = new Dictionary<State, double> { [origin] = 0 };
        var parent = new Dictionary<State, (State From, Direction Exit)>();
        var queue = new PriorityQueue<State, (double Cost, long Order)>();
        long order = 0;
        queue.Enqueue(origin, (0, order++));

        List<RoutePoint>? best = null;
        var bestCost = double.PositiveInfinity;
        while (queue.TryDequeue(out var state, out var priority))
        {
            if (priority.Cost > cost[state] || priority.Cost >= bestCost)
                continue;
            if (state != origin && !state.Elevated && targets.Contains(state.Cell))
            {
                // Цель: путь до неё плюс проезд по платформе до дальнего края — цена всей стоянки.
                var route = Build(parent, origin, state);
                var total = priority.Cost + (options.RunThrough ? RunThrough(network, route, targets, options) : 0);
                if (total < bestCost)
                    (best, bestCost) = (route, total);
                continue;
            }

            var forward = 0;
            foreach (var exit in network.ExitsFrom(state.Cell, state.Entry, state.Elevated))
            {
                if (Step(state, exit, 0))
                    forward++;
            }
            // Тупик: дальше ехать некуда — разворот на месте (как поезд в конце пути в OpenTTD).
            if (forward == 0 && state.Entry is { } back && !state.Elevated)
                Step(state, back, options.ReverseHours);
        }
        return best is null ? null : new Route(best, bestCost);

        bool Step(State from, Direction exit, double extra)
        {
            if (network.Follow(from.Cell, exit, from.Elevated) is not { } link)
                return false;
            if (options.Passable is { } passable && !passable(link.Cell))
                return false;
            var delay = options.StepDelay is { } stepDelay ? stepDelay(new RoutePoint(from.Cell, from.Entry, exit, from.Elevated), link) : 0;
            if (delay is not { } d)
                return false;
            var next = new State(link.Cell, link.Entry, link.Elevated);
            var total = cost[from] + options.CellHours + extra + d;
            if (cost.TryGetValue(next, out var known) && known <= total)
                return true;
            cost[next] = total;
            parent[next] = (from, exit);
            queue.Enqueue(next, (total, order++));
            return true;
        }
    }

    /// <summary>Продлить маршрут по клеткам цели до её дальнего края. Возвращает добавленное время.</summary>
    private static double RunThrough(NetworkLayer network, List<RoutePoint> route, IReadOnlySet<GridPos> targets, RouteOptions options)
    {
        var added = 0.0;
        var seen = new HashSet<GridPos> { route[^1].Cell };
        while (true)
        {
            var last = route[^1];
            var exits = network.ExitsFrom(last.Cell, last.Entry)
                .OrderBy(side => last.Entry is { } e && side == e.Opposite() ? 0 : 1);
            var moved = false;
            foreach (var exit in exits)
            {
                if (network.Follow(last.Cell, exit) is not { Elevated: false } link || !targets.Contains(link.Cell) || !seen.Add(link.Cell))
                    continue;
                if (options.Passable is { } passable && !passable(link.Cell))
                    continue;
                var here = last with { Exit = exit };
                if ((options.StepDelay is { } delay ? delay(here, link) : 0) is not { } d)
                    continue;
                route[^1] = here;
                route.Add(new RoutePoint(link.Cell, link.Entry, null));
                added += options.CellHours + d;
                moved = true;
                break;
            }
            if (!moved)
                return added;
        }
    }

    private static List<RoutePoint> Build(Dictionary<State, (State From, Direction Exit)> parent, State origin, State end)
    {
        var points = new List<RoutePoint> { new(end.Cell, end.Entry, null, end.Elevated) };
        var state = end;
        while (state != origin)
        {
            var (from, exit) = parent[state];
            points.Add(new RoutePoint(from.Cell, from.Entry, exit, from.Elevated));
            state = from;
        }
        points.Reverse();
        return points;
    }
}
