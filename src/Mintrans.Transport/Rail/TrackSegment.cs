using Mintrans.Core.Grid;
using Mintrans.Transport.Runtime;
using Mintrans.World.Networks;

namespace Mintrans.Transport.Rail;

/// <summary>
/// Участок пути, который занимает или закрепляет поезд: кусок пути в клетке — на земле или на пролёте моста.
/// Два поезда в одной клетке мешают друг другу, если их куски пересекаются или сходятся (docs/10а);
/// параллельные угловые куски независимы.
/// </summary>
public readonly record struct TrackSegment(GridPos Cell, TrackPieces Piece, bool Elevated)
{
    public bool Conflicts(TrackSegment other) =>
        Cell == other.Cell && Elevated == other.Elevated && Tracks.Overlap(Piece, other.Piece);
}

/// <summary>Кто занимает или закрепил участки пути — с быстрым поиском по клетке.</summary>
public sealed class SegmentIndex
{
    private readonly Dictionary<(GridPos Cell, bool Elevated), List<(TrackPieces Piece, VehicleId Owner)>> _cells = new();

    public int Revision { get; private set; }

    public void Add(TrackSegment segment, VehicleId owner)
    {
        if (Owns(segment, owner))
            return;
        var key = (segment.Cell, segment.Elevated);
        if (!_cells.TryGetValue(key, out var list))
            _cells[key] = list = new List<(TrackPieces, VehicleId)>(2);
        list.Add((segment.Piece, owner));
        Revision++;
    }

    public void Remove(TrackSegment segment, VehicleId owner)
    {
        var key = (segment.Cell, segment.Elevated);
        if (!_cells.TryGetValue(key, out var list) || list.RemoveAll(e => e.Piece == segment.Piece && e.Owner == owner) == 0)
            return;
        if (list.Count == 0)
            _cells.Remove(key);
        Revision++;
    }

    public void RemoveAll(VehicleId owner)
    {
        foreach (var segment in Of(owner).ToList())
            Remove(segment, owner);
    }

    public void Clear()
    {
        _cells.Clear();
        Revision++;
    }

    public bool Owns(TrackSegment segment, VehicleId owner) =>
        _cells.TryGetValue((segment.Cell, segment.Elevated), out var list) && list.Any(e => e.Piece == segment.Piece && e.Owner == owner);

    /// <summary>Чужой (не <paramref name="except"/>) владелец участка, мешающего данному, или null.</summary>
    public VehicleId? ConflictWith(TrackSegment segment, VehicleId except)
    {
        if (!_cells.TryGetValue((segment.Cell, segment.Elevated), out var list))
            return null;
        foreach (var (piece, owner) in list)
        {
            if (owner != except && Tracks.Overlap(piece, segment.Piece))
                return owner;
        }
        return null;
    }

    /// <summary>Есть ли кто-нибудь на земле в клетке.</summary>
    public bool AnyAt(GridPos cell) => _cells.ContainsKey((cell, false));

    /// <summary>Владелец ровно этого участка (любой, если их несколько) или null.</summary>
    public VehicleId? OwnerOf(TrackSegment segment) =>
        _cells.TryGetValue((segment.Cell, segment.Elevated), out var list) && list.FirstOrDefault(e => e.Piece == segment.Piece) is { Piece: not TrackPieces.None } entry
            ? entry.Owner
            : null;

    public IEnumerable<TrackSegment> Of(VehicleId owner) =>
        _cells.SelectMany(p => p.Value.Where(e => e.Owner == owner).Select(e => new TrackSegment(p.Key.Cell, e.Piece, p.Key.Elevated)));
}
