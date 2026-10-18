using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Sites;

namespace Mintrans.Game.View;

/// <summary>
/// Площадки объектов: заливка по частям и контур по внешней границе всей площадки,
/// поэтому непрямоугольная форма (завод со станцией и модулями) видна целиком.
/// Подписи — в <see cref="SiteLabels"/>.
/// </summary>
public partial class SiteView : Node2D
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
        if (_map.Sites.Revision != _drawnRevision)
            QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var site in _map.Sites.All)
        {
            var style = _style.SiteFor(site.Type);
            var height = SiteHeight(_map, site);
            foreach (var part in site.Parts)
            {
                var corners = _projection.RectCorners(part.Area, height);
                var partStyle = _style.PartFor(part.Kind, style);
                DrawColoredPolygon(corners, partStyle.Fill);
                if (part.Kind != Site.BasePart)
                    DrawPolyline([.. corners, corners[0]], partStyle.Outline with { A = 0.5f }, 1f);
            }
            DrawMultiline(Outline(site, height), style.Outline, 2f);
        }
        _drawnRevision = _map.Sites.Revision;
    }

    /// <summary>Стороны клеток площадки, за которыми уже не площадка.</summary>
    private Vector2[] Outline(Site site, int height)
    {
        var points = new List<Vector2>();
        foreach (var cell in site.Cells())
        {
            foreach (var side in Directions.All)
            {
                if (site.Contains(cell.Step(side)))
                    continue;
                var (a, b) = side switch
                {
                    Direction.North => ((cell.X, cell.Y), (cell.X + 1, cell.Y)),
                    Direction.East => ((cell.X + 1, cell.Y), (cell.X + 1, cell.Y + 1)),
                    Direction.South => ((cell.X, cell.Y + 1), (cell.X + 1, cell.Y + 1)),
                    _ => ((cell.X, cell.Y), (cell.X, cell.Y + 1)),
                };
                points.Add(_projection.GridPoint(a.Item1, a.Item2, height));
                points.Add(_projection.GridPoint(b.Item1, b.Item2, height));
            }
        }
        return points.ToArray();
    }

    /// <summary>Высота, на которой рисуется объект: по углу его базовой области.</summary>
    public static int SiteHeight(WorldMap map, Site site) => map.Terrain.Height[site.Base.Origin];
}
