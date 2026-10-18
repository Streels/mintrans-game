using Godot;
using Mintrans.Core.Grid;
using Mintrans.Game.View;
using Mintrans.World;

namespace Mintrans.Game.Tools;

/// <summary>
/// Ввод строительства: левая кнопка — начать протяжку, отпустить — построить. Esc — выйти из инструмента.
/// Пока кнопка зажата, клетки будущей стройки подсвечиваются: зелёные — можно, красные — мешают.
/// </summary>
public partial class BuildController : Node2D
{
    private static readonly Color Good = new(0.35f, 0.9f, 0.4f, 0.45f);
    private static readonly Color Blocked = new(0.95f, 0.65f, 0.2f, 0.4f);
    private static readonly Color Bad = new(1f, 0.25f, 0.2f, 0.6f);

    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private GridPos? _dragFrom;
    private GridPos? _hover;
    private ToolPreview? _preview;
    private Vector2? _lastMouse;

    public IBuildTool? Tool { get; private set; }

    /// <summary>Сообщение для строки состояния (пустое — нечего сказать).</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Инструмент сменился (в том числе на «нет инструмента»).</summary>
    public event Action<IBuildTool?>? ToolChanged;

    public void Init(WorldMap map, IsoProjection projection)
    {
        _map = map;
        _projection = projection;
    }

    public void SetTool(IBuildTool? tool)
    {
        Tool = tool;
        _dragFrom = null;
        UpdatePreview();
        ToolChanged?.Invoke(tool);
    }

    /// <summary>Клетка под курсором берётся из событий мыши — так ввод одинаков для мыши и сценариев проверки.</summary>
    private void TrackHover(Vector2 viewportPosition)
    {
        _lastMouse = viewportPosition;
        var world = GetCanvasTransform().AffineInverse() * viewportPosition;
        var cell = _projection.ScreenToCell(world, _map.Bounds, c => _map.Terrain.Height[c]);
        if (cell == _hover)
            return;
        _hover = cell;
        UpdatePreview();
    }

    /// <summary>Камера могла сдвинуться без движения мыши — пересчитать клетку под курсором.</summary>
    public override void _Process(double delta)
    {
        if (_lastMouse is { } mouse)
            TrackHover(mouse);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                TrackHover(motion.Position);
                break;
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when Tool is not null:
                SetTool(null);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button when Tool is not null:
                TrackHover(button.Position);
                if (button.Pressed && _hover is { } start)
                    _dragFrom = start;
                else if (!button.Pressed && _dragFrom is { } from)
                    Commit(from, _hover ?? from);
                UpdatePreview();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void Commit(GridPos from, GridPos to)
    {
        _dragFrom = null;
        var result = Tool!.Apply(from, to);
        StatusChanged?.Invoke(result.CanBuild ? "Построено. " + result.Message : "Не построено: " + result.Message);
    }

    private void UpdatePreview()
    {
        _preview = Tool is not null && _hover is { } hover ? Tool.Preview(_dragFrom ?? hover, hover) : null;
        if (_preview is not null && _dragFrom is not null)
            StatusChanged?.Invoke(_preview.Message);
        QueueRedraw();
    }

    public override void _Draw()
    {
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
    }
}
