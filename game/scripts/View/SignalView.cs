using Godot;
using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Transport.Runtime;
using Mintrans.World;
using Mintrans.World.Networks;

namespace Mintrans.Game.View;

/// <summary>
/// Сигналы и переезды (docs/10а). Сигнал — огонь у края клетки, в сторону которой он регулирует движение,
/// справа по ходу поезда: зелёный — путь открыт, красный — закрыт; у блочного огонь круглый, у маршрутного — квадратный.
/// Переезд — шлагбаумы по обе стороны пути: белые — открыт, красные — закрыт поездом.
/// Состояние берётся из транспорта и обновляется раз в игровой час.
/// </summary>
public partial class SignalView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private MapStyle _style = null!;
    private TransportState _transport = null!;
    private readonly List<GridPos> _crossings = new();
    private int _crossingsRevision = -1;

    public void Init(WorldMap map, IsoProjection projection, MapStyle style, TransportState transport)
    {
        _map = map;
        _projection = projection;
        _style = style;
        _transport = transport;
    }

    /// <summary>Перерисовать после игрового часа или стройки.</summary>
    public void Refresh() => QueueRedraw();

    public override void _Process(double delta)
    {
        if (CrossingsRevision() != _crossingsRevision)
        {
            FindCrossings();
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var style = _style.Rail;
        foreach (var rail in _transport.RailNetworks)
        {
            foreach (var signal in rail.Layer.Signals)
            {
                foreach (var side in signal.Faces.Enumerate())
                {
                    var at = SignalPoint(signal.Cell, side);
                    var color = rail.IsGreen(signal, side) ? style.SignalGreen : style.SignalRed;
                    if (signal.Logic == SignalLogic.Block)
                    {
                        DrawCircle(at, 3.6f, style.SignalPost);
                        DrawCircle(at, 2.4f, color);
                    }
                    else
                    {
                        DrawRect(new Rect2(at - new Vector2(3.6f, 3.6f), new Vector2(7.2f, 7.2f)), style.SignalPost);
                        DrawRect(new Rect2(at - new Vector2(2.3f, 2.3f), new Vector2(4.6f, 4.6f)), color);
                    }
                }
            }
        }
        foreach (var cell in _crossings)
            DrawCrossing(cell, _transport.IsCrossingClosed(cell) ? style.CrossingClosed : style.CrossingOpen);
    }

    /// <summary>Точка огня: ближе к выезду через сторону <paramref name="side"/> и правее по ходу движения.</summary>
    private Vector2 SignalPoint(GridPos cell, Direction side)
    {
        var (dx, dy) = side.Offset();
        var (rx, ry) = ((Direction)(((int)side + 1) % Directions.Count)).Offset();
        return _projection.GridPoint(cell.X + 0.5f + dx * 0.32f + rx * 0.3f, cell.Y + 0.5f + dy * 0.32f + ry * 0.3f,
            _map.Terrain.Height[cell]);
    }

    /// <summary>Шлагбаумы — по обе стороны пути, поперёк дороги.</summary>
    private void DrawCrossing(GridPos cell, Color color)
    {
        var height = _map.Terrain.Height[cell];
        var railAlongY = _map.Networks.Layers.OfType<TrackNetworkLayer>().Any(l => (l.PiecesAt(cell) & TrackPieces.NorthSouth) != 0);
        foreach (var offset in new[] { -0.36f, 0.36f })
        {
            var (a, b) = railAlongY
                ? (_projection.GridPoint(cell.X + 0.5f + offset, cell.Y + 0.2f, height), _projection.GridPoint(cell.X + 0.5f + offset, cell.Y + 0.8f, height))
                : (_projection.GridPoint(cell.X + 0.2f, cell.Y + 0.5f + offset, height), _projection.GridPoint(cell.X + 0.8f, cell.Y + 0.5f + offset, height));
            DrawLine(a, b, _style.Rail.SignalPost, 4f);
            DrawLine(a, b, color, 2f);
        }
    }

    private int CrossingsRevision() => _map.Networks.Layers.Sum(l => l.Revision);

    private void FindCrossings()
    {
        _crossings.Clear();
        var rails = _map.Networks.Layers.OfType<TrackNetworkLayer>().ToList();
        for (var y = 0; y < _map.Height; y++)
        for (var x = 0; x < _map.Width; x++)
        {
            var cell = new GridPos(x, y);
            if (rails.Any(r => r.HasAny(cell)) && NetworkShapes.IsLevelCrossing(_map, cell))
                _crossings.Add(cell);
        }
        _crossingsRevision = CrossingsRevision();
    }
}
