using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;
using Mintrans.World.Networks;

namespace Mintrans.Game.View;

/// <summary>
/// Транспортные сети. Каждая клетка рисуется своими отрезками: от центра к стороне (дорога)
/// или от стороны к стороне (кусок рельсов). Угловые куски дают диагональ, как в OpenTTD.
/// Мосты — настил над рельефом от опоры до опоры, с опорами под пролётом (docs/10б).
/// Отрисовка не зависит от раскладки сети — только от <see cref="NetworkStroke"/> и мостов.
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
        foreach (var layer in _map.Networks.Layers)
        {
            foreach (var bridge in layer.Bridges)
                DrawBridge(bridge, _style.NetworkFor(layer.Type));
        }
    }

    /// <summary>Настил с пандусами от центров опор, опоры под каждой клеткой пролёта, полотно сети сверху.</summary>
    private void DrawBridge(Bridge bridge, LineStyle network)
    {
        var rail = _style.Rail;
        var deck = Bridges.DeckHeight(_map, bridge, rail.LiftLevels);
        var (dx, dy) = bridge.Axis.Offset();
        Vector2 At(GridPos cell, float along, float height) =>
            _projection.GridPoint(cell.X + 0.5f + dx * along, cell.Y + 0.5f + dy * along, height);

        foreach (var cell in bridge.Span())
            DrawLine(At(cell, 0, _map.Terrain.Height[cell]), At(cell, 0, deck), rail.BridgePier, 3f);
        Vector2[] line =
        [
            At(bridge.A, 0, _map.Terrain.Height[bridge.A]),
            At(bridge.A, 0.5f, deck),
            At(bridge.B, -0.5f, deck),
            At(bridge.B, 0, _map.Terrain.Height[bridge.B]),
        ];
        DrawPolyline(line, rail.BridgeEdge, network.Width + 6f);
        DrawPolyline(line, rail.BridgeDeck, network.Width + 3f);
        DrawPolyline(line, network.Color, network.Width);
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

/// <summary>Высота настила моста для отрисовки: на <c>lift</c> уровней выше самой высокой клетки под ним.</summary>
public static class Bridges
{
    public static float DeckHeight(WorldMap map, Bridge bridge, int lift)
    {
        var top = bridge.Span().Append(bridge.A).Append(bridge.B).Max(cell => map.Terrain.Height[cell]);
        return top + lift;
    }

    /// <summary>Высота точки для отрисовки: на пролёте — высота настила, иначе — рельеф.</summary>
    public static float HeightAt(WorldMap map, GridPos cell, bool elevated, int lift)
    {
        if (elevated)
        {
            foreach (var layer in map.Networks.Layers)
            {
                if (layer.BridgeOver(cell) is { } bridge)
                    return DeckHeight(map, bridge, lift);
            }
        }
        return map.Terrain.Height[cell];
    }
}
