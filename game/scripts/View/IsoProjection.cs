using Godot;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;

namespace Mintrans.Game.View;

/// <summary>
/// Перевод между клетками карты и экраном в изометрии. Единственное место, где живёт изометрия:
/// смена угла, размера тайла или поворот камеры меняют только этот класс.
/// Точка сетки (gx, gy) — угол клетки; клетка (x, y) занимает углы (x, y)…(x + 1, y + 1).
/// </summary>
public sealed class IsoProjection(float tileWidth, float tileHeight, float heightStep)
{
    public float TileWidth { get; } = tileWidth;
    public float TileHeight { get; } = tileHeight;

    /// <summary>Сколько пикселей по вертикали даёт один уровень высоты.</summary>
    public float HeightStep { get; } = heightStep;

    public Vector2 GridPoint(float gx, float gy, float height) =>
        new((gx - gy) * TileWidth / 2f, (gx + gy) * TileHeight / 2f - height * HeightStep);

    public Vector2 CellCenter(GridPos cell, float height) => GridPoint(cell.X + 0.5f, cell.Y + 0.5f, height);

    /// <summary>Точка мира в метрах (docs/02в) на экране: клетка — 5 м, сетка привязана к мировым координатам.</summary>
    public Vector2 World(WorldPoint p, float height) =>
        GridPoint((float)(p.X / SpatialScale.CellM), (float)(p.Y / SpatialScale.CellM), height);

    /// <summary>
    /// Точка мира под точкой экрана: высота меняет положение на экране, поэтому уточняется по высоте найденной клетки.
    /// Null — за пределами карты.
    /// </summary>
    public WorldPoint? ScreenToWorld(Vector2 screen, GridRect bounds, Func<GridPos, int> heightAt)
    {
        if (ScreenToCell(screen, bounds, heightAt) is not { } cell)
            return null;
        var height = heightAt(cell);
        var a = screen.X / (TileWidth / 2f);
        var b = (screen.Y + height * HeightStep) / (TileHeight / 2f);
        return new WorldPoint((a + b) / 2f * SpatialScale.CellM, (b - a) / 2f * SpatialScale.CellM);
    }

    /// <summary>Углы клетки по часовой: верхний, правый, нижний, левый.</summary>
    public (Vector2 Top, Vector2 Right, Vector2 Bottom, Vector2 Left) CellCorners(GridPos cell, float height) =>
    (
        GridPoint(cell.X, cell.Y, height),
        GridPoint(cell.X + 1, cell.Y, height),
        GridPoint(cell.X + 1, cell.Y + 1, height),
        GridPoint(cell.X, cell.Y + 1, height)
    );

    /// <summary>Углы прямоугольника клеток на одной высоте.</summary>
    public Vector2[] RectCorners(GridRect rect, float height) =>
    [
        GridPoint(rect.X, rect.Y, height),
        GridPoint(rect.Right, rect.Y, height),
        GridPoint(rect.Right, rect.Bottom, height),
        GridPoint(rect.X, rect.Bottom, height),
    ];

    /// <summary>
    /// Клетка под точкой экрана. Высота меняет положение клетки на экране,
    /// поэтому уточняем несколько раз по высоте найденной клетки.
    /// </summary>
    public GridPos? ScreenToCell(Vector2 screen, GridRect bounds, Func<GridPos, int> heightAt)
    {
        GridPos? found = null;
        var height = 0;
        for (var i = 0; i < 4; i++)
        {
            var cell = FlatCell(screen, height);
            if (!bounds.Contains(cell))
                return found;
            if (found == cell)
                break;
            found = cell;
            height = heightAt(cell);
        }
        return found;
    }

    private GridPos FlatCell(Vector2 screen, int height)
    {
        var a = screen.X / (TileWidth / 2f);
        var b = (screen.Y + height * HeightStep) / (TileHeight / 2f);
        return new GridPos((int)MathF.Floor((a + b) / 2f), (int)MathF.Floor((b - a) / 2f));
    }
}
