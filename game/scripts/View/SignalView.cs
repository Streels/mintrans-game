using Godot;
using Mintrans.Core.Spatial;
using Mintrans.Transport.Runtime;
using Mintrans.World;
using Mintrans.World.Rail;

namespace Mintrans.Game.View;

/// <summary>
/// Сигналы и переезды (docs/10а, docs/10в). Сигнал — огонь в его точке на пути, чуть впереди по ходу регулируемого
/// направления и правее: зелёный — путь открыт, красный — закрыт; у блочного огонь круглый, у маршрутного — квадратный.
/// У двустороннего — по огню в каждую сторону. Переезд — шлагбаумы по обе стороны пути: белые — открыт, красные — закрыт.
/// Состояние берётся из транспорта и обновляется раз в игровой час.
/// </summary>
public partial class SignalView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private MapStyle _style = null!;
    private TransportState _transport = null!;

    public void Init(WorldMap map, IsoProjection projection, MapStyle style, TransportState transport)
    {
        _map = map;
        _projection = projection;
        _style = style;
        _transport = transport;
    }

    /// <summary>Перерисовать после игрового часа или стройки.</summary>
    public void Refresh() => QueueRedraw();

    private int _drawnRevision = -1;

    public override void _Process(double delta)
    {
        var revision = (_map.Rails?.Revision ?? 0) * 31 + _map.Networks.Layers.Sum(l => l.Revision);
        if (revision != _drawnRevision)
        {
            _drawnRevision = revision;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_map.Rails is not { } rails)
            return;
        var style = _style.Rail;
        var traffic = _transport.RailNetworks.FirstOrDefault(r => r.Rails == rails);
        foreach (var signal in rails.Signals)
        {
            var geometry = rails.Edge(signal.Edge).Geometry;
            foreach (var forward in new[] { true, false }.Where(signal.Controls))
            {
                var heading = WorldPoint.FromAngle(geometry.HeadingAt(signal.DistanceM)) * (forward ? 1 : -1);
                // Огонь — на 1,5 м впереди по ходу и на 2,5 м правее (правее — к +Y от направления движения).
                var at = geometry.PointAt(signal.DistanceM) + heading * 1.5 + heading.Perpendicular(+1) * 2.5;
                var point = _projection.World(at, RailLines.Ground(_map, at));
                var color = traffic?.IsGreen(signal, forward) == true ? style.SignalGreen : style.SignalRed;
                if (signal.Logic == SignalLogic.Block)
                {
                    DrawCircle(point, 3.6f, style.SignalPost);
                    DrawCircle(point, 2.4f, color);
                }
                else
                {
                    DrawRect(new Rect2(point - new Vector2(3.6f, 3.6f), new Vector2(7.2f, 7.2f)), style.SignalPost);
                    DrawRect(new Rect2(point - new Vector2(2.3f, 2.3f), new Vector2(4.6f, 4.6f)), color);
                }
            }
        }
        foreach (var crossing in _map.Crossings.All)
            DrawCrossing(rails, crossing, _transport.IsCrossingClosed(crossing.Cell) ? style.CrossingClosed : style.CrossingOpen);
    }

    /// <summary>Шлагбаумы — по обе стороны пути, вдоль дороги поперёк неё.</summary>
    private void DrawCrossing(RailNetwork rails, RailCrossing crossing, Color color)
    {
        var geometry = rails.Edge(crossing.Edge).Geometry;
        var middle = (crossing.FromM + crossing.ToM) / 2;
        var center = geometry.PointAt(middle);
        var road = crossing.RoadAlongY ? new WorldPoint(0, 1) : new WorldPoint(1, 0);
        var across = road.Perpendicular(+1);
        var height = RailLines.Ground(_map, center);
        foreach (var side in new[] { -1, 1 })
        {
            // Шлагбаум стоит на дороге за габаритом пути и перекрывает её поперёк.
            var gate = center + road * (side * 3.2);
            var (a, b) = (gate - across * 2.0, gate + across * 2.0);
            var (pa, pb) = (_projection.World(a, height), _projection.World(b, height));
            DrawLine(pa, pb, _style.Rail.SignalPost, 4f);
            DrawLine(pa, pb, color, 2f);
        }
    }
}
