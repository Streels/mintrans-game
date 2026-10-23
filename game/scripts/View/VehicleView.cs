using Godot;
using Mintrans.Core.Grid;
using Mintrans.Simulation.Time;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>
/// Техника на карте: поезд — толстая линия по занятым клеткам (голова темнее), грузовик — маленький ромб.
/// Между игровыми часами голова плавно сдвигается к следующей клетке; на расчёты это не влияет.
/// Цвет показывает состояние: едет, грузится, стоит, на обслуживании; разбитый состав — тёмные обломки.
/// На мосту техника рисуется на высоте настила.
/// </summary>
public partial class VehicleView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private TransportState _transport = null!;
    private SimulationRunner _runner = null!;
    private MapStyle _style = null!;

    public void Init(WorldMap map, IsoProjection projection, TransportState transport, SimulationRunner runner, MapStyle style)
    {
        _map = map;
        _projection = projection;
        _transport = transport;
        _runner = runner;
        _style = style;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        foreach (var vehicle in _transport.Vehicles)
        {
            if (vehicle.State is VehicleState.InDepot or VehicleState.Servicing)
                continue;
            var color = vehicle.State switch
            {
                VehicleState.Travelling when vehicle.BlockedHours > 0 => new Color(1f, 0.3f, 0.25f),
                VehicleState.Travelling => vehicle.Cargo.IsZero ? new Color(0.85f, 0.85f, 0.9f) : new Color(1f, 0.82f, 0.25f),
                VehicleState.Loading or VehicleState.Unloading => new Color(0.4f, 0.85f, 1f),
                VehicleState.Crashed => _style.Rail.Wreck,
                _ => new Color(1f, 0.3f, 0.25f),
            };
            var head = HeadPosition(vehicle);
            if (vehicle.Network.Trains)
            {
                var points = new List<Vector2> { head, Center(vehicle.Head) };
                points.AddRange(vehicle.TrailPoints.Select(Center));
                if (points.Count == 2)
                    points.Add(head);
                DrawPolyline(points.ToArray(), new Color(0.1f, 0.1f, 0.12f), 7f);
                DrawPolyline(points.ToArray(), color, 4f);
                DrawCircle(head, 4.5f, new Color(0.15f, 0.15f, 0.18f));
            }
            else
            {
                Vector2[] diamond = [head + new Vector2(0, -5), head + new Vector2(7, 0), head + new Vector2(0, 5), head + new Vector2(-7, 0)];
                DrawColoredPolygon(diamond, color);
                DrawPolyline([.. diamond, diamond[0]], new Color(0.1f, 0.1f, 0.12f), 1.5f);
            }
        }
    }

    private Vector2 Center(RoutePoint point) =>
        _projection.CellCenter(point.Cell, Bridges.HeightAt(_map, point.Cell, point.Elevated, _style.Rail.LiftLevels));

    private Vector2 HeadPosition(Vehicle vehicle)
    {
        var current = Center(vehicle.Head);
        if (vehicle.State != VehicleState.Travelling || vehicle.BlockedHours > 0 || vehicle.NextPoint is not { } next)
            return current;
        var progress = Mathf.Clamp((float)(vehicle.MoveBudget + _runner.HourProgress * vehicle.Speed), 0f, 1f);
        return current.Lerp(Center(next), progress);
    }
}
