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
    /// Ж/д путь и платформа над заводом, автостанция и дорога под ним; в конце — соединение от конца пути, ждущее
    /// подтверждения (виден радиус и подсказка про колесо мыши), и сетка стройки.
    /// </summary>
    private void Build()
    {
        ClickButton("Ж/д путь А→Б");
        Rail(P(3850, 3897), P(4050, 3897));
        ClickButton("Ж/д станция");
        Drag(P(3920, 3897), P(3929, 3897));
        ClickButton("Автостанция");
        Drag(P(3950, 3980), P(3953, 3980));
        ClickButton("Дорога");
        Drag(P(3954, 3980), P(3960, 3980));
        Drag(P(3960, 3980), P(3960, 4060));
        ClickButton("Ж/д путь А→Б");
        Drag(P(4050, 3897), P(4110, 3830));
        Scroll(-1);
        Wait(5);
    }

    /// <summary>Вся цепочка docs/01 на карте 40×40 км: пути, платформы, депо, сигналы, дорога, гараж, техника и маршруты.</summary>
    private void Chain()
    {
        Key(Godot.Key.F); // вся карта в окне
        // Железная дорога как в тестах прототипа: прямые у предприятий и петли в два шага (docs/10в).
        ClickButton("Ж/д путь А→Б");
        Rail(P(4860, 3142), P(4980, 3142));
        Rail(P(2760, 3342), P(2880, 3342));
        Rail(P(3850, 3897), P(4050, 3897));
        Rail(P(4980, 3142), P(5020, 3857));
        Rail(P(5020, 3857), P(4050, 3897));
        Rail(P(2760, 3342), P(2720, 3857));
        Rail(P(2720, 3857), P(3850, 3897));
        ClickButton("Ж/д станция");
        Drag(P(4905, 3142), P(4914, 3142));
        Drag(P(2805, 3342), P(2814, 3342));
        Drag(P(3920, 3897), P(3929, 3897));
        ClickButton("Ж/д депо");
        Drag(P(4870, 3142), P(4873, 3142));
        Drag(P(2865, 3342), P(2868, 3342));
        ClickButton("Маршрутный сигнал");
        ClickCell(P(3860, 3897));
        ClickCell(P(4040, 3897));
        ClickButton("Автостанция");
        Drag(P(3950, 3980), P(3953, 3980));
        ClickButton("Дорога");
        Drag(P(3954, 3980), P(3960, 3980));
        Drag(P(3960, 3980), P(3960, 4300));
        ClickButton("Городской пункт");
        Drag(P(3960, 4310), P(3960, 4312));
        ClickButton("Автогараж");
        Drag(P(3961, 4000), P(3960, 4000));

        // Стартовая пара docs/11b. Два паровоза Л по 12 полувагонов — каждый из своего депо: уголь и руда на завод.
        ClickButton("Осмотр");
        foreach (var (depot, mine) in new[] { (P(4871, 3142), P(4910, 3142)), (P(2866, 3342), P(2810, 3342)) })
        {
            ClickCell(depot);
            ClickButton("Купить: Паровоз Л");
            for (var i = 0; i < 12; i++)
                ClickButton("+ Полувагон");
            ClickButton("Выбрать А");
            ClickCell(mine);
            ClickButton("Выбрать Б");
            ClickCell(P(3925, 3897));
            ClickButton("Отправить по маршруту");
        }
        // Три ГАЗ-51 с бортовым кузовом: сталь с завода в город.
        ClickCell(P(3961, 4000));
        for (var i = 0; i < 3; i++)
        {
            ClickButton("Купить: ГАЗ-51 бортовой");
            ClickButton("Выбрать А");
            ClickCell(P(3951, 3980));
            ClickButton("Выбрать Б");
            ClickCell(P(3960, 4311));
            ClickButton("Отправить по маршруту");
        }
        // Двадцать суток, затем отчёт о технике в журнал.
        _actions.Enqueue(() => _skipHours(24 * 20));
        _actions.Enqueue(() =>
        {
            foreach (var line in Report?.Invoke() ?? [])
                GD.Print(line);
        });
        // Крупный план первого поезда на линии (или завода, если поезда нет), чтобы были видны вагоны.
        _actions.Enqueue(() =>
        {
            var camera = GetViewport().GetCamera2D();
            var target = _firstTrain() ?? P(3925, 3897);
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
