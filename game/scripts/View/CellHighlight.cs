using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>Контур клетки под курсором.</summary>
public partial class CellHighlight : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private MapStyle _style = null!;
    private GridPos? _cell;

    public void Init(WorldMap map, IsoProjection projection, MapStyle style)
    {
        _map = map;
        _projection = projection;
        _style = style;
    }

    public GridPos? Cell
    {
        get => _cell;
        set
        {
            if (_cell == value)
                return;
            _cell = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_cell is not { } cell)
            return;
        var (top, right, bottom, left) = _projection.CellCorners(cell, _map.Terrain.Height[cell]);
        DrawPolyline([top, right, bottom, left, top], _style.Highlight, 1.5f);
    }
}
