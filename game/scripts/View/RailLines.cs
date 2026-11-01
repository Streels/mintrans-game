using Godot;
using Mintrans.Core.Geometry;
using Mintrans.Core.Spatial;
using Mintrans.World;
using Mintrans.World.Rail;

namespace Mintrans.Game.View;

/// <summary>
/// Оси рельсов на экране: метрическая геометрия (docs/10в) → ломаная в изометрии с высотой рельефа под каждой точкой.
/// Мост рисуется на высоте настила: на <c>lift</c> уровней выше самой высокой клетки под ним, с пандусами у опор.
/// Только отображение: на правила не влияет.
/// </summary>
public static class RailLines
{
    /// <summary>Шаг точек ломаной вдоль пути, м: дуги выглядят гладкими, а высота следует за рельефом.</summary>
    public const double StepM = 2.5;

    /// <summary>Ломаная оси на экране; <paramref name="deck"/> — высота настила моста или null для пути на земле.</summary>
    public static Vector2[] Points(PathGeometry axis, WorldMap map, IsoProjection projection, float? deck)
    {
        var count = Math.Max(1, (int)Math.Ceiling(axis.Length / StepM));
        var points = new Vector2[count + 1];
        for (var i = 0; i <= count; i++)
        {
            var s = axis.Length * i / count;
            points[i] = projection.World(axis.PointAt(s), Height(map, axis, s, deck));
        }
        return points;
    }

    /// <summary>Высота точки пути для отрисовки: рельеф или настил моста с пандусом в пределах клетки у опоры.</summary>
    public static float Height(WorldMap map, PathGeometry axis, double s, float? deck)
    {
        var ground = Ground(map, axis.PointAt(s));
        if (deck is not { } top)
            return ground;
        var ramp = Math.Min(s, axis.Length - s) / SpatialScale.CellM;
        return ramp >= 1 ? top : ground + (top - ground) * (float)ramp;
    }

    public static float Ground(WorldMap map, WorldPoint at)
    {
        var cell = SpatialScale.ToCell(at);
        cell = new(Math.Clamp(cell.X, 0, map.Width - 1), Math.Clamp(cell.Y, 0, map.Height - 1));
        return map.Terrain.Height[cell];
    }

    /// <summary>Высота настила моста: на <paramref name="lift"/> уровней выше самой высокой клетки под ним.</summary>
    public static float Deck(WorldMap map, PathGeometry axis, int lift)
    {
        var top = float.NegativeInfinity;
        for (var s = 0.0; s <= axis.Length; s += SpatialScale.CellM / 2)
            top = Math.Max(top, Ground(map, axis.PointAt(s)));
        return top + lift;
    }

    /// <summary>Высота настила пути или null, если путь лежит на земле.</summary>
    public static float? DeckOf(WorldMap map, RailNetwork rails, TrackEdge edge, int lift) =>
        rails.IsBridge(edge) ? Deck(map, edge.Geometry, lift) : null;
}
