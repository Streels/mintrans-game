using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;

namespace Mintrans.World.Networks;

/// <summary>
/// Раскладка «кусками пути» — для железной дороги. Проехать внутри клетки можно только
/// по уложенному куску: пересечение двух прямых не даёт повернуть, повороты задаются явно.
/// </summary>
public sealed class TrackNetworkLayer : NetworkLayer
{
    public const string LayoutName = "tracks";

    private readonly CellLayer<TrackPieces> _pieces;

    public TrackNetworkLayer(TypeId type, int width, int height) : base(type, width, height)
    {
        _pieces = new CellLayer<TrackPieces>(width, height);
    }

    public override int Revision => _pieces.Revision;

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

    public void AddPiece(GridPos pos, TrackPieces piece) => _pieces[pos] |= piece;

    public void RemovePiece(GridPos pos, TrackPieces piece) => _pieces[pos] &= ~piece;

    public override void LayThrough(GridPos pos, Direction a, Direction b) => AddPiece(pos, Tracks.Between(a, b));

    /// <summary>Куски пути соседей остаются: они лежат в своих клетках и просто становятся тупиками.</summary>
    public override void ClearCell(GridPos pos) => _pieces[pos] = TrackPieces.None;

    public override void LayPath(IReadOnlyList<GridPos> path)
    {
        foreach (var step in Walk(path))
            AddPiece(step.Cell, Tracks.Between(step.In, step.Out));
    }

    public override IEnumerable<NetworkStroke> StrokesAt(GridPos pos) =>
        _pieces[pos].Enumerate().Select(piece =>
        {
            var (a, b) = piece.Sides();
            return new NetworkStroke(pos, a, b);
        });
}
