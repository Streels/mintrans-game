using Mintrans.Transport.Runtime;
using Mintrans.World.Rail;

namespace Mintrans.Transport.Rail;

/// <summary>
/// Кто занимает или закрепил участки путей — по путям, в метрах (docs/10в, раздел 5). Конфликт — общий отрезок
/// одного пути или участки разных путей в конфликтной зоне одной стрелки: там габариты путей пересекаются,
/// и два поезда не могут быть одновременно.
/// </summary>
public sealed class SpanIndex
{
    private readonly Dictionary<TrackEdgeId, List<(TrackSpan Span, VehicleId Owner)>> _byEdge = new();
    private readonly Dictionary<VehicleId, List<TrackSpan>> _byOwner = new();

    public int Revision { get; private set; }

    public void Add(TrackSpan span, VehicleId owner)
    {
        if (span.LengthM <= 0)
            return;
        if (!_byEdge.TryGetValue(span.Edge, out var list))
            _byEdge[span.Edge] = list = new List<(TrackSpan, VehicleId)>(2);
        list.Add((span, owner));
        if (!_byOwner.TryGetValue(owner, out var own))
            _byOwner[owner] = own = [];
        own.Add(span);
        Revision++;
    }

    /// <summary>Заменить все участки владельца.</summary>
    public void Set(VehicleId owner, IEnumerable<TrackSpan> spans)
    {
        RemoveAll(owner);
        foreach (var span in spans)
            Add(span, owner);
    }

    public void RemoveAll(VehicleId owner)
    {
        if (!_byOwner.Remove(owner, out var spans))
            return;
        foreach (var span in spans)
            if (_byEdge.TryGetValue(span.Edge, out var list))
            {
                list.RemoveAll(e => e.Owner == owner);
                if (list.Count == 0)
                    _byEdge.Remove(span.Edge);
            }
        Revision++;
    }

    public void Clear()
    {
        _byEdge.Clear();
        _byOwner.Clear();
        Revision++;
    }

    public IReadOnlyList<TrackSpan> Of(VehicleId owner) => _byOwner.TryGetValue(owner, out var spans) ? spans : [];

    public IEnumerable<VehicleId> Owners => _byOwner.Keys;

    public IEnumerable<(TrackSpan Span, VehicleId Owner)> On(TrackEdgeId edge) =>
        _byEdge.TryGetValue(edge, out var list) ? list : [];

    public bool Owns(TrackSpan span, VehicleId owner) =>
        On(span.Edge).Any(e => e.Owner == owner && e.Span.FromM <= span.FromM + 1e-6 && e.Span.ToM >= span.ToM - 1e-6);

    /// <summary>
    /// Чужой (не <paramref name="except"/>) владелец участка, мешающего данному, или null. Учитываются зоны стрелок сети.
    /// </summary>
    public VehicleId? ConflictWith(RailNetwork network, TrackSpan span, VehicleId except)
    {
        foreach (var (other, owner) in On(span.Edge))
            if (owner != except && other.Overlaps(span))
                return owner;
        foreach (var zone in Zones(network, span))
        foreach (var part in zone.ConflictZone)
        foreach (var (other, owner) in On(part.Edge))
            if (owner != except && other.FromM < part.ToM && other.ToM > part.FromM)
                return owner;
        return null;
    }

    /// <summary>Стрелки, в конфликтную зону которых заходит участок.</summary>
    private static IEnumerable<TrackJunction> Zones(RailNetwork network, TrackSpan span)
    {
        if (!network.ContainsEdge(span.Edge))
            yield break;
        var edge = network.Edge(span.Edge);
        foreach (var node in new[] { edge.From, edge.To })
            if (network.JunctionAt(node) is { } junction &&
                junction.ConflictZone.Any(z => z.Edge == span.Edge && z.FromM < span.ToM && z.ToM > span.FromM))
                yield return junction;
    }

    /// <summary>Есть ли кто-нибудь на участке пути.</summary>
    public bool Any(TrackSpan span) => On(span.Edge).Any(e => e.Span.Overlaps(span, 0));
}
