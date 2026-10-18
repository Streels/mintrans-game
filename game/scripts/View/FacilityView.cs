using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>
/// Сооружения: депо и гаражи — тёмная площадка с полосой въезда, городские пункты — подсветка клеток улицы.
/// Цвета — из раздела "facilities" стиля карты.
/// </summary>
public partial class FacilityView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private MapStyle _style = null!;
    private int _drawnRevision = -1;

    public void Init(WorldMap map, IsoProjection projection, MapStyle style)
    {
        _map = map;
        _projection = projection;
        _style = style;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_map.Facilities.Revision != _drawnRevision)
            QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var facility in _map.Facilities.All)
        {
            var style = _style.PartFor(facility.Kind, _style.DefaultSite);
            var height = _map.Terrain.Height[facility.Area.Origin];
            var corners = _projection.RectCorners(facility.Area, height);
            DrawColoredPolygon(corners, style.Fill);
            DrawPolyline([.. corners, corners[0]], style.Outline, 2f);
            if (facility.Entrance is { } side)
            {
                // Отметка въезда — короткая черта от центра к стороне въезда.
                var center = _projection.CellCenter(facility.Area.Origin, height);
                var (dx, dy) = side.Offset();
                var edge = _projection.GridPoint(facility.Area.X + 0.5f + dx * 0.5f, facility.Area.Y + 0.5f + dy * 0.5f, height);
                DrawLine(center, edge, style.Outline, 3f);
            }
        }
        _drawnRevision = _map.Facilities.Revision;
    }
}
