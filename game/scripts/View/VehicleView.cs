using Godot;
using Mintrans.Core.Grid;
using Mintrans.Simulation.Time;
using Mintrans.Transport.Runtime;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>
/// Техника на карте: поезд — толстая линия по занятым клеткам (голова темнее), грузовик — маленький ромб.
/// Между игровыми часами голова плавно сдвигается к следующей клетке; на расчёты это не влияет.
/// Цвет показывает состояние: едет, грузится, стоит, на обслуживании.
/// </summary>
public partial class VehicleView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private TransportState _transport = null!;
    private SimulationRunner _runner = null!;

    public void Init(WorldMap map, IsoProjection projection, TransportState transport, SimulationRunner runner)
    {
        _map = map;
        _projection = projection;
        _transport = transport;
        _runner = runner;
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
                VehicleState.Travelling => vehicle.Cargo.IsZero ? new Color(0.85f, 0.85f, 0.9f) : new Color(1f, 0.82f, 0.25f),
                VehicleState.Loading or VehicleState.Unloading => new Color(0.4f, 0.85f, 1f),
                _ => new Color(1f, 0.3f, 0.25f),
            };
            var head = HeadPosition(vehicle);
            if (vehicle.Network.Trains)
            {
                var points = new List<Vector2> { head, Center(vehicle.Position) };
                points.AddRange(vehicle.Trail.Select(Center));
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

    private Vector2 Center(GridPos cell) => _projection.CellCenter(cell, _map.Terrain.Height[cell]);

    private Vector2 HeadPosition(Vehicle vehicle)
    {
        var current = Center(vehicle.Position);
        if (vehicle.State != VehicleState.Travelling || vehicle.NextCell is not { } next)
            return current;
        var progress = Mathf.Clamp((float)(vehicle.MoveBudget + _runner.HourProgress * vehicle.Speed), 0f, 1f);
        return current.Lerp(Center(next), progress);
    }
}
