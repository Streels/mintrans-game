using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>
/// Рельеф одной сеткой треугольников: верх каждой ячейки рельефа и видимые обрывы к более низким соседям.
/// Ячейки рисуются от дальних к ближним (по x + y), поэтому высокие правильно закрывают задние.
/// </summary>
public partial class TerrainView : Node2D
{
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private MapStyle _style = null!;
    private MeshInstance2D _mesh = null!;
    private int _builtRevision = -1;

    public void Init(WorldMap map, IsoProjection projection, MapStyle style)
    {
        _map = map;
        _projection = projection;
        _style = style;
        _mesh = new MeshInstance2D { Name = "TerrainMesh" };
        AddChild(_mesh);
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (Revision() != _builtRevision)
            Rebuild();
    }

    private int Revision() => _map.Terrain.Revision;

    private void Rebuild()
    {
        // Рельеф хранится на крупной сетке (docs/02в): одна плитка — ячейка рельефа из Step×Step строительных клеток.
        var colorizer = new TerrainColorizer(_map, _style);
        var terrain = _map.Terrain;
        var step = terrain.Step;
        var coarse = terrain.Height.Coarse;
        var (width, height) = (coarse.Width, coarse.Height);
        var minHeight = coarse.NonDefault().Select(c => (int)c.Value).Append(coarse.Default).Min() - 3;

        var vertices = new List<Vector2>(width * height * 6);
        var colors = new List<Color>(vertices.Capacity);
        var indices = new List<int>(width * height * 12);

        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            var i = vertices.Count;
            vertices.AddRange([a, b, c, d]);
            colors.AddRange([color, color, color, color]);
            indices.AddRange([i, i + 1, i + 2, i, i + 2, i + 3]);
        }

        int HeightOr(int x, int y, int fallback) =>
            x >= 0 && y >= 0 && x < width && y < height ? coarse[x, y] : fallback;

        for (var diagonal = 0; diagonal < width + height - 1; diagonal++)
        for (var x = Math.Max(0, diagonal - height + 1); x <= Math.Min(diagonal, width - 1); x++)
        {
            var y = diagonal - x;
            int h = coarse[x, y];
            var (x0, y0) = (x * step, y * step);
            var (x1, y1) = (Math.Min(_map.Width, x0 + step), Math.Min(_map.Height, y0 + step));
            var top = _projection.GridPoint(x0, y0, h);
            var right = _projection.GridPoint(x1, y0, h);
            var bottom = _projection.GridPoint(x1, y1, h);
            var left = _projection.GridPoint(x0, y1, h);
            var sample = new Core.Grid.GridPos(Math.Min(x0 + step / 2, _map.Width - 1), Math.Min(y0 + step / 2, _map.Height - 1));
            Quad(top, right, bottom, left, colorizer.TopColor(sample));

            // Обрыв на юго-восток (к соседу x + 1) и на юго-запад (к соседу y + 1), если сосед ниже.
            var eastH = HeightOr(x + 1, y, minHeight);
            if (eastH < h)
            {
                var drop = (h - eastH) * _projection.HeightStep;
                Quad(right, bottom, bottom + new Vector2(0, drop), right + new Vector2(0, drop), _style.CliffRight);
            }
            var southH = HeightOr(x, y + 1, minHeight);
            if (southH < h)
            {
                var drop = (h - southH) * _projection.HeightStep;
                Quad(left, bottom, bottom + new Vector2(0, drop), left + new Vector2(0, drop), _style.CliffLeft);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _mesh.Mesh = mesh;
        _builtRevision = Revision();
    }
}
