using Godot;
using Mintrans.Core.Grid;
using Mintrans.Game.View;
using Mintrans.World;

namespace Mintrans.Game.Debug;

/// <summary>
/// Проверка интерфейса строительства без человека (параметр --ui-demo): нажимает кнопки панели
/// и протягивает мышь по клеткам, отправляя игре обычные события мыши. В конце сохраняет кадр.
/// </summary>
public partial class UiScenario : Node
{
    private readonly Queue<Action> _actions = new();
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private int _wait;

    public string? ScreenshotPath { get; set; }

    public void Init(WorldMap map, IsoProjection projection)
    {
        _map = map;
        _projection = projection;
        Wait(10);

        // Ж/д станция над заводом и путь от её торца на восток с поворотом на север.
        ClickButton("Ж/д станция");
        Drag(new GridPos(150, 149), new GridPos(163, 149));
        ClickButton("Ж/д путь");
        Drag(new GridPos(164, 149), new GridPos(185, 149));
        Drag(new GridPos(185, 149), new GridPos(195, 139));
        // Автостанция слева от завода и дорога от неё на запад.
        ClickButton("Автостанция");
        Drag(new GridPos(149, 158), new GridPos(149, 161));
        ClickButton("Дорога");
        Drag(new GridPos(148, 160), new GridPos(128, 160));
        // Неудачная попытка: путь сквозь площадку завода — должен показать причину и ничего не построить.
        ClickButton("Ж/д путь");
        Press(new GridPos(156, 165));
        Move(new GridPos(156, 152));
        Wait(5);
    }

    public override void _Process(double delta)
    {
        if (_wait > 0)
        {
            _wait--;
            return;
        }
        if (_actions.TryDequeue(out var action))
        {
            action();
            return;
        }
        if (ScreenshotPath is { } path)
        {
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"Скриншот сохранён: {path}");
        }
        GetTree().Quit();
    }

    private void Wait(int frames) => _actions.Enqueue(() => _wait = frames);

    private void ClickButton(string text)
    {
        _actions.Enqueue(() =>
        {
            var button = FindButton(GetTree().Root, text) ?? throw new InvalidOperationException($"Нет кнопки «{text}».");
            var center = button.GetGlobalRect().GetCenter();
            Push(new InputEventMouseMotion { Position = center, GlobalPosition = center });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = center, GlobalPosition = center });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = center, GlobalPosition = center });
        });
        Wait(2);
    }

    private void Drag(GridPos from, GridPos to)
    {
        Press(from);
        Move(to);
        _actions.Enqueue(() => Push(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = ScreenOf(to), GlobalPosition = ScreenOf(to),
        }));
        Wait(2);
    }

    private void Press(GridPos cell)
    {
        _actions.Enqueue(() => Push(new InputEventMouseMotion { Position = ScreenOf(cell), GlobalPosition = ScreenOf(cell) }));
        _actions.Enqueue(() => Push(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = ScreenOf(cell), GlobalPosition = ScreenOf(cell),
        }));
    }

    private void Move(GridPos cell) =>
        _actions.Enqueue(() => Push(new InputEventMouseMotion { Position = ScreenOf(cell), GlobalPosition = ScreenOf(cell) }));

    private void Push(InputEvent @event) => GetViewport().PushInput(@event);

    /// <summary>Точка окна над центром клетки при текущем положении камеры.</summary>
    private Vector2 ScreenOf(GridPos cell) =>
        GetViewport().GetCanvasTransform() * _projection.CellCenter(cell, _map.Terrain.Height[cell]);

    private static Button? FindButton(Node node, string text)
    {
        if (node is Button button && button.Text == text)
            return button;
        foreach (var child in node.GetChildren())
        {
            if (FindButton(child, text) is { } found)
                return found;
        }
        return null;
    }
}
