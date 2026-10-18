using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Networks;

namespace Mintrans.Game.View;

/// <summary>
/// Транспортные сети. Каждая клетка рисуется своими отрезками: от центра к стороне (дорога)
/// или от стороны к стороне (кусок рельсов). Угловые куски дают диагональ, как в OpenTTD.
/// Отрисовка не зависит от раскладки сети — только от <see cref="NetworkStroke"/>.
/// </summary>
public partial class NetworkView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private MapStyle _style = null!;
    private readonly Dictionary<NetworkLayer, Vector2[]> _lines = new();
    private int _builtRevision = -1;

    public void Init(WorldMap map, IsoProjection projection, MapStyle style)
    {
        _map = map;
        _projection = projection;
        _style = style;
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (Revision() != _builtRevision)
            Rebuild();
    }

    public override void _Draw()
    {
        foreach (var (layer, points) in _lines)
        {
            if (points.Length == 0)
                continue;
            var style = _style.NetworkFor(layer.Type);
            DrawMultiline(points, style.Color, style.Width);
        }
    }

    private int Revision() => _map.Networks.Layers.Sum(l => l.Revision);

    private void Rebuild()
    {
        _lines.Clear();
        foreach (var layer in _map.Networks.Layers)
        {
            _lines[layer] = layer.Strokes()
                .SelectMany(s => new[] { Anchor(s.Cell, s.From), Anchor(s.Cell, s.To) })
                .ToArray();
        }
        _builtRevision = Revision();
        QueueRedraw();
    }

    /// <summary>Центр клетки (side = null) или середина её стороны.</summary>
    private Vector2 Anchor(GridPos cell, Direction? side)
    {
        var height = _map.Terrain.Height[cell];
        if (side is not { } s)
            return _projection.CellCenter(cell, height);
        var (dx, dy) = s.Offset();
        return _projection.GridPoint(cell.X + 0.5f + dx * 0.5f, cell.Y + 0.5f + dy * 0.5f, height);
    }
}
