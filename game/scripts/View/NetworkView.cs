using Godot;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Rail;

namespace Mintrans.Game.View;

/// <summary>
/// Транспортные сети. Дороги — по клеткам: отрезки от центра клетки к стороне, мосты дорог — настил над рельефом.
/// Рельсы — ломаные по точной метрической геометрии путей (прямые и дуги, docs/10в), мосты рельсов — путь на высоте
/// настила с опорами под пролётом; платформы станций — светлая полоса вдоль пути со стороны предприятия.
/// </summary>
public partial class NetworkView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private MapStyle _style = null!;
    private readonly Dictionary<NetworkLayer, Vector2[]> _lines = new();
    private readonly List<(Vector2[] Points, bool Bridge, Vector2[] Piers)> _rails = new();
    private readonly List<Vector2[]> _platforms = new();
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
        if (_map.Rails is not { } rails)
            return;
        var rail = _style.Rail;
        var line = _style.NetworkFor(rails.Settings.Kind);
        foreach (var platform in _platforms)
            DrawPolyline(platform, rail.Platform, line.Width + 3f);
        foreach (var (points, bridge, piers) in _rails)
        {
            if (bridge)
            {
                for (var i = 0; i + 1 < piers.Length; i += 2)
                    DrawLine(piers[i], piers[i + 1], rail.BridgePier, 3f);
                DrawPolyline(points, rail.BridgeEdge, line.Width + 6f);
                DrawPolyline(points, rail.BridgeDeck, line.Width + 3f);
            }
            DrawPolyline(points, line.Color, line.Width);
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

    private int Revision() => _map.Networks.Layers.Sum(l => l.Revision) * 31 + (_map.Rails?.Revision ?? 0);

    private void Rebuild()
    {
        _lines.Clear();
        foreach (var layer in _map.Networks.Layers)
        {
            _lines[layer] = layer.Strokes()
                .SelectMany(s => new[] { Anchor(s.Cell, s.From), Anchor(s.Cell, s.To) })
                .ToArray();
        }
        _rails.Clear();
        _platforms.Clear();
        if (_map.Rails is { } rails)
        {
            var lift = _style.Rail.LiftLevels;
            foreach (var edge in rails.Edges)
            {
                var deck = RailLines.DeckOf(_map, rails, edge, lift);
                var piers = new List<Vector2>();
                if (deck is { } top)
                {
                    // Опоры через клетку под пролётом.
                    for (var s = SpatialScale.CellM; s < edge.LengthM - SpatialScale.CellM / 2; s += SpatialScale.CellM)
                    {
                        var at = edge.Geometry.PointAt(s);
                        piers.Add(_projection.World(at, RailLines.Ground(_map, at)));
                        piers.Add(_projection.World(at, top));
                    }
                }
                _rails.Add((RailLines.Points(edge.Geometry, _map, _projection, deck), deck is not null, piers.ToArray()));
            }
            foreach (var berth in rails.Berths.Where(b => b.Kind == BerthKind.Platform))
                _platforms.Add(Platform(rails, berth));
        }
        _builtRevision = Revision();
        QueueRedraw();
    }

    /// <summary>Полоса платформы: вдоль пути, в 3 м от оси со стороны предприятия.</summary>
    private Vector2[] Platform(RailNetwork rails, RailBerth berth)
    {
        var geometry = rails.Edge(berth.Edge).Geometry;
        var (a, b) = (geometry.PointAt(berth.FromM), geometry.PointAt(berth.ToM));
        var normal = (b - a).Normalized().Perpendicular(+1);
        if (_map.Sites.All.FirstOrDefault(s => s.Id == berth.Site) is { } site)
        {
            var center = SpatialScale.CellCenter(site.Base.Center);
            if ((center - a).Dot(normal) < 0)
                normal = -normal;
        }
        var offset = normal * 3.0;
        return [_projection.World(a + offset, RailLines.Ground(_map, a)), _projection.World(b + offset, RailLines.Ground(_map, b))];
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

/// <summary>Высота настила моста дороги для отрисовки: на <c>lift</c> уровней выше самой высокой клетки под ним.</summary>
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
