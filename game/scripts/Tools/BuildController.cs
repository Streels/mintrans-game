using Godot;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.Game.View;
using Mintrans.World;

namespace Mintrans.Game.Tools;

/// <summary>
/// Ввод строительства: левая кнопка — начать протяжку, отпустить — построить. Esc — выйти из инструмента.
/// Пока кнопка зажата, клетки будущей стройки подсвечиваются: зелёные — можно, красные — мешают; оси будущих путей
/// рисуются линией. Рельсы «А→Б» после протяжки ждут подтверждения (docs/10в): колесо мыши — радиус (а не масштаб карты),
/// Tab — вариант, щелчок — построить, Esc или правая кнопка — отменить.
/// </summary>
public partial class BuildController : Node2D
{
    private static readonly Color Good = new(0.35f, 0.9f, 0.4f, 0.45f);
    private static readonly Color Blocked = new(0.95f, 0.65f, 0.2f, 0.4f);
    private static readonly Color Bad = new(1f, 0.25f, 0.2f, 0.6f);
    private static readonly Color AxisGood = new(0.2f, 1f, 0.35f, 0.95f);
    private static readonly Color AxisBad = new(1f, 0.3f, 0.2f, 0.95f);

    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private BuildPoint? _dragFrom;
    private BuildPoint? _hover;
    private ToolPreview? _preview;
    private Vector2? _lastMouse;

    public IBuildTool? Tool { get; private set; }

    /// <summary>Сообщение для строки состояния (пустое — нечего сказать).</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Инструмент сменился (в том числе на «нет инструмента»).</summary>
    public event Action<IBuildTool?>? ToolChanged;

    /// <summary>Колесо мыши занято предпросмотром радиуса — камера его не трогает.</summary>
    public bool CapturesWheel => Tool is IPendingTool { HasPending: true };

    public void Init(WorldMap map, IsoProjection projection)
    {
        _map = map;
        _projection = projection;
    }

    public void SetTool(IBuildTool? tool)
    {
        (Tool as IPendingTool)?.Cancel();
        Tool = tool;
        _dragFrom = null;
        UpdatePreview();
        ToolChanged?.Invoke(tool);
    }

    /// <summary>Точка под курсором берётся из событий мыши — так ввод одинаков для мыши и сценариев проверки.</summary>
    private void TrackHover(Vector2 viewportPosition)
    {
        _lastMouse = viewportPosition;
        var world = GetCanvasTransform().AffineInverse() * viewportPosition;
        Func<GridPos, int> height = c => _map.Terrain.Height[c];
        var point = _projection.ScreenToWorld(world, _map.Bounds, height) is { } at
            ? new BuildPoint(SpatialScale.ToCell(at), at)
            : (BuildPoint?)null;
        if (point == _hover)
            return;
        _hover = point;
        if (Tool is not IPendingTool { HasPending: true })
            UpdatePreview();
    }

    /// <summary>Камера могла сдвинуться без движения мыши — пересчитать точку под курсором и сетку стройки.</summary>
    public override void _Process(double delta)
    {
        if (_lastMouse is { } mouse)
            TrackHover(mouse);
        var transform = GetCanvasTransform();
        if (Tool is not null && (transform.Scale.X != _gridZoom || transform.Origin != _gridOrigin))
        {
            (_gridZoom, _gridOrigin) = (transform.Scale.X, transform.Origin);
            QueueRedraw();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var pending = Tool as IPendingTool;
        switch (@event)
        {
            case InputEventMouseMotion motion:
                TrackHover(motion.Position);
                break;
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when pending is { HasPending: true }:
                pending.Cancel();
                StatusChanged?.Invoke("Отменено.");
                UpdatePreview();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when Tool is not null:
                SetTool(null);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Keycode: Key.Tab, Pressed: true } when pending is { HasPending: true }:
                pending.NextVariant();
                ShowPending(pending);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel
                when pending is { HasPending: true }:
                pending.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                ShowPending(pending);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when pending is { HasPending: true }:
                pending.Cancel();
                StatusChanged?.Invoke("Отменено.");
                UpdatePreview();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when pending is { HasPending: true }:
                var result = pending.Confirm();
                StatusChanged?.Invoke(result.CanBuild ? "Построено. " + result.Message : "Не построено: " + result.Message);
                UpdatePreview();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button when Tool is not null:
                TrackHover(button.Position);
                if (button.Pressed && _hover is { } start)
                    _dragFrom = start;
                else if (!button.Pressed && _dragFrom is { } from)
                    Commit(from, _hover ?? from);
                if (Tool is not IPendingTool { HasPending: true })
                    UpdatePreview();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click when Tool is null:
                // Без инструмента щелчок — выбор: депо, пункт для маршрута и т. п.
                TrackHover(click.Position);
                if (_hover is { } cell)
                    CellClicked?.Invoke(cell.Cell);
                break;
        }
    }

    /// <summary>Щелчок по клетке без выбранного инструмента.</summary>
    public event Action<GridPos>? CellClicked;

    private void Commit(BuildPoint from, BuildPoint to)
    {
        _dragFrom = null;
        var result = Tool!.Apply(from, to);
        if (Tool is IPendingTool { HasPending: true } pending)
        {
            ShowPending(pending);
            return;
        }
        StatusChanged?.Invoke(result.CanBuild ? "Построено. " + result.Message : "Не построено: " + result.Message);
    }

    private void ShowPending(IPendingTool pending)
    {
        _preview = pending.PendingPreview;
        if (_preview is not null)
            StatusChanged?.Invoke(_preview.Message);
        QueueRedraw();
    }

    private void UpdatePreview()
    {
        if (Tool is IPendingTool { HasPending: true } pending)
        {
            ShowPending(pending);
            return;
        }
        _preview = Tool is not null && _hover is { } hover ? Tool.Preview(_dragFrom ?? hover, hover) : null;
        if (_preview is not null && _dragFrom is not null)
            StatusChanged?.Invoke(_preview.Message);
        QueueRedraw();
    }

    private static readonly Color GridLine = new(1f, 1f, 1f, 0.16f);

    /// <summary>С какого размера клетки на экране (в пикселях) показывать сетку стройки: мельче она превращается в шум.</summary>
    private const float GridMinCellPixels = 10f;

    /// <summary>
    /// Сетка стройки (docs/02в, раздел 6): только видимая часть единой мировой сетки 5 м, только с выбранным
    /// инструментом и только при достаточном приближении. Привязана к мировым координатам, рельеф не учитывает.
    /// </summary>
    private void DrawGrid()
    {
        var zoom = Math.Max(1e-4f, GetCanvasTransform().Scale.X);
        if (Tool is null || _projection.TileWidth * zoom < GridMinCellPixels)
            return;
        var inverse = GetCanvasTransform().AffineInverse();
        var size = GetViewportRect().Size;
        Vector2[] screen = [Vector2.Zero, new(size.X, 0), size, new(0, size.Y)];
        var grid = screen.Select(p => Flat(inverse * p)).ToList();
        var (x0, x1) = ((int)Math.Floor(grid.Min(g => g.X)), (int)Math.Ceiling(grid.Max(g => g.X)));
        var (y0, y1) = ((int)Math.Floor(grid.Min(g => g.Y)), (int)Math.Ceiling(grid.Max(g => g.Y)));
        (x0, x1) = (Math.Max(0, x0), Math.Min(_map.Width, x1));
        (y0, y1) = (Math.Max(0, y0), Math.Min(_map.Height, y1));
        if (x1 - x0 > 600 || y1 - y0 > 600)
            return;
        var width = 1f / zoom;
        for (var x = x0; x <= x1; x++)
            DrawLine(_projection.GridPoint(x, y0, 0), _projection.GridPoint(x, y1, 0), GridLine, width);
        for (var y = y0; y <= y1; y++)
            DrawLine(_projection.GridPoint(x0, y, 0), _projection.GridPoint(x1, y, 0), GridLine, width);
    }

    /// <summary>Точка сетки (дробные клетки) под точкой экрана на нулевой высоте.</summary>
    private Vector2 Flat(Vector2 screen)
    {
        var a = screen.X / (_projection.TileWidth / 2f);
        var b = screen.Y / (_projection.TileHeight / 2f);
        return new Vector2((a + b) / 2f, (b - a) / 2f);
    }

    private float _gridZoom;
    private Vector2 _gridOrigin;

    public override void _Draw()
    {
        DrawGrid();
        if (_preview is null)
            return;
        foreach (var cell in _preview.Cells)
        {
            if (!_map.Contains(cell))
                continue;
            var color = _preview.BadCells.Contains(cell) ? Bad : _preview.CanBuild ? Good : Blocked;
            var (top, right, bottom, left) = _projection.CellCorners(cell, _map.Terrain.Height[cell]);
            DrawColoredPolygon([top, right, bottom, left], color);
        }
        var zoom = Math.Max(0.05f, GetCanvasTransform().Scale.X);
        foreach (var shape in _preview.Shapes)
        {
            var points = RailLines.Points(shape, _map, _projection, 0);
            if (points.Length >= 2)
                DrawPolyline(points, _preview.CanBuild ? AxisGood : AxisBad, 3f / zoom);
        }
    }
}
