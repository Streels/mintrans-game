using Godot;
using Mintrans.Core.Grid;
using Mintrans.Game.View;
using Mintrans.World;

namespace Mintrans.Game.Debug;

/// <summary>
/// Проверка интерфейса без человека (параметр --ui-demo[=имя]): нажимает кнопки и щёлкает или протягивает
/// мышь по клеткам, отправляя игре обычные события мыши. В конце сохраняет кадр.
/// Сценарии: "build" — стройка у завода и предпросмотр соединения рельсов; "chain" — вся цепочка прототипа с поездами и грузовиками.
/// </summary>
public partial class UiScenario : Node
{
    private readonly Queue<Action> _actions = new();
    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private Action<long> _skipHours = null!;
    private Func<GridPos?> _firstTrain = null!;

    /// <summary>Строки о технике для журнала: сценарий печатает их в конце.</summary>
    public Func<IEnumerable<string>>? Report { get; set; }
    private int _wait;

    public string? ScreenshotPath { get; set; }

    /// <param name="firstTrain">Клетка головы первого поезда на линии — для крупного плана в конце сценария «chain».</param>
    public void Init(WorldMap map, IsoProjection projection, string scenario, Action<long> skipHours, Func<GridPos?> firstTrain)
    {
        _map = map;
        _projection = projection;
        _skipHours = skipHours;
        _firstTrain = firstTrain;
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

    /// <summary>
    /// Ж/д путь и платформа у завода, автостанция и дорога; в конце — соединение от конца пути, ждущее подтверждения
    /// (виден радиус и подсказка про колесо мыши).
    /// </summary>
    private void Build()
    {
        ClickButton("Ж/д путь А→Б");
        Rail(P(120, 147), P(170, 147));
        ClickButton("Ж/д станция");
        Drag(P(150, 147), P(163, 147));
        ClickButton("Автостанция");
        Drag(P(149, 158), P(149, 161));
        ClickButton("Дорога");
        Drag(P(148, 160), P(128, 160));
        ClickButton("Ж/д путь А→Б");
        Drag(P(170, 147), P(215, 120));
        Scroll(-1);
        Wait(5);
    }

    /// <summary>Вся цепочка docs/01: пути, платформы, депо, дорога, гараж, городской пункт, техника и маршруты.</summary>
    private void Chain()
    {
        Key(Godot.Key.F); // вся карта в окне
        // Железная дорога как в тестах прототипа: прямые у предприятий и петли в два шага (docs/10в).
        ClickButton("Ж/д путь А→Б");
        Rail(P(180, 50), P(215, 50));
        Rail(P(40, 52), P(80, 52));
        Rail(P(120, 147), P(170, 147));
        Rail(P(215, 50), P(255, 107));
        Rail(P(255, 107), P(170, 147));
        Rail(P(40, 52), P(0, 107));
        Rail(P(0, 107), P(120, 147));
        ClickButton("Ж/д станция");
        Drag(P(200, 50), P(207, 50));
        Drag(P(62, 52), P(69, 52));
        Drag(P(150, 147), P(163, 147));
        ClickButton("Ж/д депо");
        Drag(P(183, 50), P(186, 50));
        Drag(P(73, 52), P(76, 52));
        ClickButton("Маршрутный сигнал");
        ClickCell(P(122, 147));
        ClickCell(P(168, 147));
        ClickButton("Автостанция");
        Drag(P(149, 158), P(149, 161));
        ClickButton("Дорога");
        Drag(P(148, 160), P(80, 160));
        Drag(P(80, 160), P(54, 186));
        Drag(P(54, 186), P(53, 186));
        ClickButton("Городской пункт");
        Drag(P(50, 186), P(52, 186));
        ClickButton("Автогараж");
        Drag(P(147, 161), P(147, 160));

        // Стартовая пара docs/11b. Два паровоза Л по 12 полувагонов — каждый из своего депо: уголь и руда на завод.
        ClickButton("Осмотр");
        foreach (var (depot, mine) in new[] { (P(184, 50), P(203, 50)), (P(74, 52), P(65, 52)) })
        {
            ClickCell(depot);
            ClickButton("Купить: Паровоз Л");
            for (var i = 0; i < 12; i++)
                ClickButton("+ Полувагон");
            ClickButton("Выбрать А");
            // Шахта при виде всей карты может оказаться под панелью депо — сдвигаем карту, как сделал бы игрок.
            Pan(-450);
            ClickCell(mine);
            Pan(450);
            ClickButton("Выбрать Б");
            ClickCell(P(156, 147));
            ClickButton("Отправить по маршруту");
        }
        // Три ГАЗ-51 с бортовым кузовом: сталь с завода в город.
        ClickCell(P(147, 161));
        for (var i = 0; i < 3; i++)
        {
            ClickButton("Купить: ГАЗ-51 бортовой");
            ClickButton("Выбрать А");
            ClickCell(P(149, 159));
            ClickButton("Выбрать Б");
            ClickCell(P(51, 186));
            ClickButton("Отправить по маршруту");
        }
        // Двадцать суток, затем панель депо — посмотреть на состояние поездов.
        _actions.Enqueue(() => _skipHours(24 * 20));
        _actions.Enqueue(() =>
        {
            foreach (var line in Report?.Invoke() ?? [])
                GD.Print(line);
        });
        ClickCell(P(184, 50));
        // Крупный план первого поезда на линии (или завода, если поезда нет), чтобы были видны вагоны.
        _actions.Enqueue(() =>
        {
            var camera = GetViewport().GetCamera2D();
            var target = _firstTrain() ?? P(160, 150);
            camera.Zoom = new Vector2(16f, 16f);
            camera.Position = _projection.CellCenter(target, _map.Terrain.Height[target]) + new Vector2(30, 0);
        });
        Wait(5);
    }

    /// <summary>Рельсы «А→Б»: протянуть и подтвердить щелчком соединение по умолчанию.</summary>
    private void Rail(GridPos from, GridPos to)
    {
        Drag(from, to);
        ClickCell(to);
    }

    /// <summary>Колесо мыши над картой: шаг вверх (+1) или вниз (−1).</summary>
    private void Scroll(int step)
    {
        _actions.Enqueue(() =>
        {
            var center = GetViewport().GetVisibleRect().GetCenter();
            var button = step > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            Push(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = center, GlobalPosition = center });
            Push(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = center, GlobalPosition = center });
        });
        Wait(2);
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
