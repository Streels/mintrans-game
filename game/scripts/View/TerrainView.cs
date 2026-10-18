using Godot;
using Mintrans.Core.Grid;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>
/// Рельеф одной сеткой треугольников: верх каждой клетки и видимые обрывы к более низким соседям.
/// Клетки рисуются от дальних к ближним (по x + y), поэтому высокие клетки правильно закрывают задние.
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

    private int Revision() => _map.Terrain.Height.Revision + _map.Terrain.Layers.Sum(l => l.Cells.Revision);

    private void Rebuild()
    {
        var colorizer = new TerrainColorizer(_map, _style);
        var heights = _map.Terrain.Height;
        var minHeight = heights.AsSpan().ToArray().Min() - 3;

        var vertices = new List<Vector2>(_map.Width * _map.Height * 6);
        var colors = new List<Color>(vertices.Capacity);
        var indices = new List<int>(_map.Width * _map.Height * 12);

        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            var i = vertices.Count;
            vertices.AddRange([a, b, c, d]);
            colors.AddRange([color, color, color, color]);
            indices.AddRange([i, i + 1, i + 2, i, i + 2, i + 3]);
        }

        int HeightOr(int x, int y, int fallback) =>
            _map.Contains(new GridPos(x, y)) ? heights[x, y] : fallback;

        for (var diagonal = 0; diagonal < _map.Width + _map.Height - 1; diagonal++)
        for (var x = Math.Max(0, diagonal - _map.Height + 1); x <= Math.Min(diagonal, _map.Width - 1); x++)
        {
            var cell = new GridPos(x, diagonal - x);
            int h = heights[cell];
            var (top, right, bottom, left) = _projection.CellCorners(cell, h);
            Quad(top, right, bottom, left, colorizer.TopColor(cell));

            // Обрыв на юго-восток (к соседу x + 1) и на юго-запад (к соседу y + 1), если сосед ниже.
            var eastH = HeightOr(cell.X + 1, cell.Y, minHeight);
            if (eastH < h)
            {
                var drop = (h - eastH) * _projection.HeightStep;
                Quad(right, bottom, bottom + new Vector2(0, drop), right + new Vector2(0, drop), _style.CliffRight);
            }
            var southH = HeightOr(cell.X, cell.Y + 1, minHeight);
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
