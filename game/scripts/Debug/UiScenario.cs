using Godot;
using Mintrans.Core.Grid;
using Mintrans.Game.View;
using Mintrans.World;

namespace Mintrans.Game.Debug;

/// <summary>
/// Проверка интерфейса без человека (параметр --ui-demo[=имя]): нажимает кнопки и щёлкает или протягивает
/// мышь по клеткам, отправляя игре обычные события мыши. В конце сохраняет кадр.
/// Сценарии: "build" — стройка у завода; "chain" — вся цепочка прототипа с поездами и грузовиками.
/// </summary>
public partial class UiScenario : Node
{
    private readonly Queue<Action> _actions = new();
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private Action<long> _skipHours = null!;
    private int _wait;

    public string? ScreenshotPath { get; set; }

    public void Init(WorldMap map, IsoProjection projection, string scenario, Action<long> skipHours)
    {
        _map = map;
        _projection = projection;
        _skipHours = skipHours;
        Wait(10);
        switch (scenario)
        {
            case "chain":
                Chain();
                break;
            default:
                Build();
                break;
        }
    }

    private static GridPos P(int x, int y) => new(x, y);

    /// <summary>Ж/д станция и путь у завода, автостанция и дорога, неудачная попытка сквозь завод.</summary>
    private void Build()
    {
        ClickButton("Ж/д станция");
        Drag(P(150, 149), P(163, 149));
        ClickButton("Ж/д путь");
        Drag(P(164, 149), P(185, 149));
        Drag(P(185, 149), P(195, 139));
        ClickButton("Автостанция");
        Drag(P(149, 158), P(149, 161));
        ClickButton("Дорога");
        Drag(P(148, 160), P(128, 160));
        ClickButton("Ж/д путь");
        Press(P(156, 165));
        Move(P(156, 152));
        Wait(5);
    }

    /// <summary>Вся цепочка docs/01: станции, пути, дорога, депо, гараж, городской пункт, техника и маршруты.</summary>
    private void Chain()
    {
        Key(Godot.Key.F); // вся карта в окне
        ClickButton("Ж/д станция");
        Drag(P(200, 48), P(207, 48));
        Drag(P(62, 50), P(69, 50));
        Drag(P(150, 149), P(163, 149));
        ClickButton("Автостанция");
        Drag(P(149, 158), P(149, 161));
        ClickButton("Ж/д путь");
        Drag(P(208, 48), P(208, 140));
        Drag(P(208, 140), P(200, 149));
        Drag(P(200, 149), P(164, 149));
        Drag(P(61, 50), P(61, 100));
        Drag(P(61, 100), P(110, 149));
        Drag(P(110, 149), P(149, 149));
        ClickButton("Дорога");
        Drag(P(148, 160), P(80, 160));
        Drag(P(80, 160), P(54, 186));
        Drag(P(54, 186), P(53, 186));
        ClickButton("Городской пункт");
        Drag(P(50, 186), P(52, 186));
        ClickButton("Ж/д депо");
        Drag(P(180, 152), P(180, 151));
        ClickButton("Ж/д путь");
        Drag(P(180, 151), P(181, 149));
        ClickButton("Автогараж");
        Drag(P(147, 161), P(147, 160));

        // Два поезда по 11 полувагонов: уголь и руда на завод.
        ClickButton("Осмотр");
        ClickCell(P(180, 152));
        foreach (var mine in new[] { P(203, 48), P(65, 50) })
        {
            ClickButton("Купить: Грузовой локомотив");
            for (var i = 0; i < 11; i++)
                ClickButton("+ Полувагон");
            ClickButton("Выбрать А");
            // Угольная шахта при виде всей карты оказывается под панелью депо — сдвигаем карту, как сделал бы игрок.
            Pan(-450);
            ClickCell(mine);
            Pan(450);
            ClickButton("Выбрать Б");
            ClickCell(P(156, 149));
            ClickButton("Отправить по маршруту");
        }
        // Три грузовика: сталь с завода в город.
        ClickCell(P(147, 161));
        for (var i = 0; i < 3; i++)
        {
            ClickButton("Купить: Грузовик");
            ClickButton("Выбрать А");
            ClickCell(P(149, 159));
            ClickButton("Выбрать Б");
            ClickCell(P(51, 186));
            ClickButton("Отправить по маршруту");
        }
        // Двадцать суток, затем панель депо — посмотреть на состояние поездов.
        _actions.Enqueue(() => _skipHours(24 * 20));
        ClickCell(P(180, 152));
        // Крупный план у завода, чтобы была видна техника.
        _actions.Enqueue(() =>
        {
            var camera = GetViewport().GetCamera2D();
            camera.Zoom = new Vector2(0.9f, 0.9f);
            camera.Position = _projection.CellCenter(P(160, 152), 0) + new Vector2(150, 0);
        });
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

    /// <summary>Сдвинуть карту на экране по горизонтали (в пикселях окна).</summary>
    private void Pan(float screenPixels)
    {
        _actions.Enqueue(() =>
        {
            var camera = GetViewport().GetCamera2D();
            camera.Position -= new Vector2(screenPixels / camera.Zoom.X, 0);
        });
        Wait(2);
    }

    private void Key(Key key)
    {
        _actions.Enqueue(() =>
        {
            Push(new InputEventKey { Keycode = key, Pressed = true });
            Push(new InputEventKey { Keycode = key, Pressed = false });
        });
        Wait(2);
    }

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

    private void ClickCell(GridPos cell)
    {
        Press(cell);
        Release(cell);
        Wait(2);
    }

    private void Drag(GridPos from, GridPos to)
    {
        Press(from);
        Move(to);
        Release(to);
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

    private void Release(GridPos cell) =>
        _actions.Enqueue(() => Push(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = ScreenOf(cell), GlobalPosition = ScreenOf(cell),
        }));

    private void Move(GridPos cell) =>
        _actions.Enqueue(() => Push(new InputEventMouseMotion { Position = ScreenOf(cell), GlobalPosition = ScreenOf(cell) }));

    private void Push(InputEvent @event) => GetViewport().PushInput(@event);

    /// <summary>Точка окна над центром клетки при текущем положении камеры.</summary>
    private Vector2 ScreenOf(GridPos cell) =>
        GetViewport().GetCanvasTransform() * _projection.CellCenter(cell, _map.Terrain.Height[cell]);

    private static Button? FindButton(Node node, string text)
    {
        if (node is Button { Visible: true } button && button.Text == text && !button.IsQueuedForDeletion())
            return button;
        foreach (var child in node.GetChildren())
        {
            if (FindButton(child, text) is { } found)
                return found;
        }
        return null;
    }
}
