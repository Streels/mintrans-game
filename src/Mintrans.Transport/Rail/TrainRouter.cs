using Mintrans.Core.Spatial;
using Mintrans.World.Rail;

namespace Mintrans.Transport.Rail;

/// <summary>
/// Маршрут поезда: пути по порядку движения от пути головы и место остановки на последнем пути (метры по ходу движения).
/// Разворот в тупике — два соседних элемента: путь и он же в обратную сторону. <paramref name="Hours"/> — ожидаемое время.
/// </summary>
public sealed record TrainRoute(IReadOnlyList<DirectedEdge> Edges, double TargetM, double Hours)
{
    /// <summary>Разворачивается ли маршрут после пути с номером <paramref name="index"/>.</summary>
    public bool ReversesAfter(int index) => index + 1 < Edges.Count && Edges[index + 1] == Edges[index].Reverse();
}

/// <summary>Настройки поиска маршрута поезда (docs/12: путь с минимальным ожидаемым временем).</summary>
public sealed class TrainRouteOptions
{
    /// <summary>Скорость, м/ч.</summary>
    public double SpeedMph { get; init; } = 1;

    /// <summary>Доплата за разворот в тупике, ч.</summary>
    public double ReverseHours { get; init; }

    /// <summary>Ожидаемая задержка въезда на путь, ч; null — въезд запрещён (односторонний маршрутный сигнал навстречу).</summary>
    public Func<DirectedEdge, double?>? EnterDelay { get; init; }

    /// <summary>
    /// Преграды на пути по ходу движения: метры хода, дальше которых проехать нельзя (въезд в чужое депо).
    /// До преграды доехать можно — например, к платформе перед депо, — а сквозь неё нет.
    /// </summary>
    public Func<DirectedEdge, IEnumerable<double>>? Barriers { get; init; }
}

/// <summary>
/// Поиск маршрута по рельсовому графу (docs/12): вершины — пути в направлении движения, цена — ожидаемое время в часах:
/// длина пути при скорости поезда плюс задержки занятых участков. Повернуть можно только по путям без излома;
/// развернуться — в тупике (как поезд в конце пути в OpenTTD).
/// </summary>
public static class TrainRouter
{
    public static TrainRoute? Find(RailNetwork network, TrackPosition start, IReadOnlyCollection<TrackPosition> targets, TrainRouteOptions options)
    {
        if (targets.Count == 0)
            return null;
        var speed = Math.Max(1e-6, options.SpeedMph);
        // Ближайшая преграда по ходу движения после точки from (в метрах хода) или бесконечность.
        double Barrier(DirectedEdge edge, double from) =>
            options.Barriers?.Invoke(edge).Where(b => b > from + SpatialScale.EpsilonM).DefaultIfEmpty(double.PositiveInfinity).Min()
            ?? double.PositiveInfinity;
        TrainRoute? best = null;
        // Цель на пути головы, впереди неё и до преграды.
        var startBarrier = Barrier(start.Edge, start.DistanceM);
        foreach (var target in targets.Where(t => t.Edge == start.Edge && t.DistanceM >= start.DistanceM - SpatialScale.EpsilonM &&
                                                  t.DistanceM <= startBarrier + SpatialScale.EpsilonM))
        {
            var hours = (target.DistanceM - start.DistanceM) / speed;
            if (best is null || hours < best.Hours)
                best = new TrainRoute([start.Edge], target.DistanceM, hours);
        }
        var byEdge = targets.GroupBy(t => t.Edge).ToDictionary(g => g.Key, g => g.Select(t => t.DistanceM).ToList());

        // Цена — время до начала пути в направлении движения.
        var cost = new Dictionary<DirectedEdge, double>();
        var parent = new Dictionary<DirectedEdge, DirectedEdge>();
        var queue = new PriorityQueue<DirectedEdge, (double, int, bool)>();
        void Relax(DirectedEdge next, DirectedEdge via, double hours)
        {
            if (cost.TryGetValue(next, out var known) && known <= hours)
                return;
            cost[next] = hours;
            parent[next] = via;
            queue.Enqueue(next, (hours, next.Edge.Value, !next.Forward));
        }
        void Expand(DirectedEdge from, double atEnd)
        {
            var any = false;
            foreach (var next in network.Next(from))
            {
                any = true;
                if ((options.EnterDelay is { } delay ? delay(next) : 0) is { } d)
                    Relax(next, from, atEnd + d);
            }
            if (!any)
                Relax(from.Reverse(), from, atEnd + options.ReverseHours);
        }

        if (double.IsPositiveInfinity(startBarrier))
            Expand(start.Edge, (network.LengthOf(start.Edge) - start.DistanceM) / speed);
        var settled = new HashSet<DirectedEdge>();
        while (queue.TryDequeue(out var current, out var key))
        {
            if (!settled.Add(current) || cost[current] < key.Item1 - 1e-12)
                continue;
            if (best is not null && key.Item1 >= best.Hours)
                break;
            var barrier = Barrier(current, -1);
            if (byEdge.TryGetValue(current, out var onEdge) && onEdge.Where(t => t <= barrier + SpatialScale.EpsilonM).DefaultIfEmpty(-1).Min() is var targetM and >= 0)
            {
                var hours = key.Item1 + targetM / speed;
                if (best is null || hours < best.Hours)
                    best = new TrainRoute(Unwind(parent, start.Edge, current), targetM, hours);
            }
            if (double.IsPositiveInfinity(barrier))
                Expand(current, key.Item1 + network.LengthOf(current) / speed);
        }
        return best;
    }

    /// <summary>Пути от <paramref name="start"/> до <paramref name="end"/>; если это один путь (по кругу) — хотя бы один шаг назад.</summary>
    private static List<DirectedEdge> Unwind(Dictionary<DirectedEdge, DirectedEdge> parent, DirectedEdge start, DirectedEdge end)
    {
        var edges = new List<DirectedEdge> { end };
        var at = end;
        do
        {
            at = parent[at];
            edges.Add(at);
        } while (at != start && edges.Count <= parent.Count + 1);
        edges.Reverse();
        return edges;
    }
}
