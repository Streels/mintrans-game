using Mintrans.Core.Spatial;
using Mintrans.World.Rail;

namespace Mintrans.Transport.Rail;

/// <summary>
/// Участок пути, который занимает или закрепляет поезд, в координатах самого пути: метры от его начала (From).
/// </summary>
public readonly record struct TrackSpan(TrackEdgeId Edge, double FromM, double ToM)
{
    public bool Overlaps(TrackSpan other, double epsilonM = SpatialScale.EpsilonM) =>
        Edge == other.Edge && FromM < other.ToM - epsilonM && other.FromM < ToM - epsilonM;

    public double LengthM => ToM - FromM;

    /// <summary>Участок по ходу движения <paramref name="edge"/> от <paramref name="fromM"/> до <paramref name="toM"/> метров этого хода.</summary>
    public static TrackSpan Along(RailNetwork network, DirectedEdge edge, double fromM, double toM)
    {
        var length = network.LengthOf(edge);
        var (a, b) = (Math.Clamp(Math.Min(fromM, toM), 0, length), Math.Clamp(Math.Max(fromM, toM), 0, length));
        return edge.Forward ? new TrackSpan(edge.Edge, a, b) : new TrackSpan(edge.Edge, length - b, length - a);
    }
}

/// <summary>
/// Положение состава на рельсах (docs/10в, раздел 6): путь головы и метры, пройденные по нему по ходу движения,
/// и пути за головой, по которым тянется состав. Длина состава — в метрах; только что выехавший из депо состав
/// выходит на путь постепенно: <see cref="ExtentM"/> растёт, пока не станет равной длине.
/// </summary>
public sealed class TrainBody
{
    private readonly List<DirectedEdge> _edges;

    public TrainBody(DirectedEdge head, double headM, double lengthM, double extentM)
    {
        _edges = [head];
        HeadM = headM;
        LengthM = lengthM;
        ExtentM = Math.Min(extentM, lengthM);
    }

    private TrainBody(List<DirectedEdge> edges, double headM, double lengthM, double extentM)
    {
        _edges = edges;
        HeadM = headM;
        LengthM = lengthM;
        ExtentM = extentM;
    }

    /// <summary>Пути состава от головы к хвосту: первый — путь головы.</summary>
    public IReadOnlyList<DirectedEdge> Edges => _edges;

    public DirectedEdge HeadEdge => _edges[0];

    /// <summary>Сколько метров голова прошла по своему пути по ходу движения.</summary>
    public double HeadM { get; private set; }

    public double LengthM { get; }

    /// <summary>Какая часть состава уже на путях, м (меньше длины — хвост ещё в депо).</summary>
    public double ExtentM { get; private set; }

    public TrackPosition Head => new(HeadEdge, HeadM);

    public bool FullyOut => ExtentM >= LengthM - SpatialScale.EpsilonM;

    /// <summary>Участки путей под составом, от головы к хвосту. У состава в депо (длина на путях 0) — пусто.</summary>
    public IEnumerable<TrackSpan> Spans(RailNetwork network)
    {
        var left = ExtentM;
        if (left <= SpatialScale.EpsilonM)
            yield break;
        for (var i = 0; i < _edges.Count; i++)
        {
            // Голова — в точке HeadM своего пути; пути за ней состав занимает от их конца по ходу движения.
            var top = i == 0 ? HeadM : network.LengthOf(_edges[i]);
            var from = Math.Max(0, top - left);
            yield return TrackSpan.Along(network, _edges[i], from, top);
            left -= top - from;
            if (left <= SpatialScale.EpsilonM)
                yield break;
        }
    }

    /// <summary>
    /// Точки состава по ходу движения: <paramref name="behindM"/> метров позади головы (0 — голова).
    /// Дальше хвоста — точка хвоста.
    /// </summary>
    public TrackPosition PositionBehind(RailNetwork network, double behindM)
    {
        var left = Math.Max(0, behindM);
        var top = HeadM;
        for (var i = 0; i < _edges.Count; i++)
        {
            if (left <= top || i == _edges.Count - 1)
                return new TrackPosition(_edges[i], Math.Max(0, top - left));
            left -= top;
            top = network.LengthOf(_edges[i + 1]);
        }
        return Head;
    }

    /// <summary>Сдвинуть голову вперёд по своему пути (не дальше его конца).</summary>
    internal void Advance(RailNetwork network, double metres)
    {
        HeadM = Math.Min(network.LengthOf(HeadEdge), HeadM + metres);
        ExtentM = Math.Min(LengthM, ExtentM + metres);
        Trim(network);
    }

    /// <summary>Голова переходит на следующий путь в своём начале.</summary>
    internal void Enter(RailNetwork network, DirectedEdge next)
    {
        _edges.Insert(0, next);
        HeadM = 0;
        Trim(network);
    }

    /// <summary>Убрать пути, на которых состава больше нет.</summary>
    private void Trim(RailNetwork network)
    {
        var left = ExtentM - HeadM;
        var keep = 1;
        while (keep < _edges.Count && left > SpatialScale.EpsilonM)
        {
            left -= network.LengthOf(_edges[keep]);
            keep++;
        }
        if (keep < _edges.Count)
            _edges.RemoveRange(keep, _edges.Count - keep);
    }

    /// <summary>
    /// Развернуть состав (docs/10а): головой становится хвост, все пути — в обратную сторону.
    /// Разворачивается только состав, целиком вышедший на путь.
    /// </summary>
    internal TrainBody Flipped(RailNetwork network)
    {
        var tail = PositionBehind(network, ExtentM);
        var tailIndex = _edges.IndexOf(tail.Edge);
        var edges = _edges.Take(tailIndex + 1).Reverse().Select(e => e.Reverse()).ToList();
        var headM = network.LengthOf(tail.Edge) - tail.DistanceM;
        return new TrainBody(edges, headM, LengthM, ExtentM);
    }

    /// <summary>
    /// Перенести положение на пути после деления (docs/10в, раздел 3): каждый путь состава заменяется своими частями
    /// по ходу движения. Null — участок под головой снесён. Сносить пути под поездом не даёт правило транспорта.
    /// </summary>
    internal TrainBody? Remapped(RailNetwork network, RailChange change)
    {
        if (!_edges.Any(e => change.Replaced.ContainsKey(e.Edge)))
            return this;
        var edges = new List<DirectedEdge>();
        var headM = HeadM;
        for (var i = 0; i < _edges.Count; i++)
        {
            var edge = _edges[i];
            if (!change.Replaced.TryGetValue(edge.Edge, out var pieces))
            {
                edges.Add(edge);
                continue;
            }
            // От головы к хвосту — против хода движения.
            IEnumerable<EdgePiece> ordered = edge.Forward ? pieces.OrderByDescending(p => p.OffsetM) : pieces.OrderBy(p => p.OffsetM);
            if (i == 0)
            {
                var length = change.OldLengths[edge.Edge];
                if (change.Map(edge.Edge, edge.Forward ? HeadM : length - HeadM) is not { } mapped)
                    return null;
                var piece = pieces.First(p => p.Edge == mapped.Edge);
                headM = edge.Forward ? mapped.DistanceM : piece.LengthM - mapped.DistanceM;
                ordered = ordered.SkipWhile(p => p.Edge != piece.Edge);
            }
            edges.AddRange(ordered.Select(p => new DirectedEdge(p.Edge, edge.Forward)));
        }
        var body = new TrainBody(edges, headM, LengthM, ExtentM);
        body.Trim(network);
        return body;
    }
}
