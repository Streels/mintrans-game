using Godot;
using Mintrans.Core.Grid;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.World;
using Mintrans.World.Facilities;

namespace Mintrans.Game.UI;

/// <summary>
/// Панель депо или автогаража (docs/10, docs/11): купить технику, собрать состав,
/// выбрать пункты А и Б щелчком по карте и отправить по маршруту. Показывает состояние каждой машины.
/// </summary>
public partial class FleetPanel : PanelContainer
{
    private TransportState _transport = null!;
    private WorldMap _map = null!;
    private DisplayNames _names = null!;
    private VBoxContainer _body = null!;
    private Label _message = null!;
    private Facility? _depot;
    private VehicleId? _selected;
    private int _resizeFrames;
    private readonly Dictionary<VehicleId, (StopRef? A, StopRef? B)> _drafts = new();

    /// <summary>Какой пункт сейчас выбирается щелчком по карте: 'A', 'B' или null.</summary>
    public char? Picking { get; private set; }

    public void Init(TransportState transport, WorldMap map, DisplayNames names)
    {
        _transport = transport;
        _map = map;
        _names = names;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Hud.PanelStyle());
        CustomMinimumSize = new Vector2(440, 0);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 4);
        root.AddChild(_body);
        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) };
        _message.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.55f));
        root.AddChild(_message);
        Visible = false;
    }

    public bool IsOpen => Visible && _depot is not null;

    public void Open(Facility depot)
    {
        _depot = depot;
        _selected = _transport.InDepot(depot.Id).Select(v => (VehicleId?)v.Id).LastOrDefault();
        Picking = null;
        _message.Text = "";
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        Visible = false;
        _depot = null;
        Picking = null;
    }

    /// <summary>Щелчок по карте во время выбора пункта маршрута.</summary>
    public void Pick(GridPos cell)
    {
        if (Picking is not { } which || Selected is not { } vehicle)
            return;
        var stop = _transport.Stops.At(cell, vehicle.Network.Network);
        if (stop is null)
        {
            _message.Text = vehicle.Network.Trains
                ? "Щёлкните по ж/д станции предприятия."
                : "Щёлкните по автостанции предприятия или городскому пункту.";
            return;
        }
        var draft = Draft(vehicle);
        _drafts[vehicle.Id] = which == 'A' ? (stop.Ref, draft.B) : (draft.A, stop.Ref);
        Picking = null;
        _message.Text = $"Пункт {(which == 'A' ? "А" : "Б")}: {stop.Name}";
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_resizeFrames > 0 && --_resizeFrames == 0)
            ResetSize();
        var viewport = GetViewportRect().Size;
        Position = new Vector2(viewport.X - Size.X - 12, 64);
    }

    private Vehicle? Selected =>
        _selected is { } id && _transport.Vehicles.FirstOrDefault(v => v.Id == id) is { } vehicle ? vehicle : null;

    private (StopRef? A, StopRef? B) Draft(Vehicle vehicle) =>
        _drafts.TryGetValue(vehicle.Id, out var draft) ? draft : (vehicle.A, vehicle.B);

    /// <summary>Перестроить содержимое — после действий игрока и каждого игрового часа.</summary>
    public void Refresh()
    {
        if (!IsOpen)
            return;
        foreach (var child in _body.GetChildren())
            child.QueueFree();
        var depot = _depot!;
        var network = _transport.Catalog.NetworkByDepot(depot.Kind)!;

        var header = new HBoxContainer();
        header.AddChild(Text(depot.Name, 17, expand: true));
        header.AddChild(MakeButton("×", Close));
        _body.AddChild(header);

        var buy = new HFlowContainer();
        // Только модели, которые продаются в текущем году (docs/14); у грузовиков — по кнопке на каждый кузов.
        foreach (var model in _transport.ForSale(network.Network).Where(m => m.Role != UnitRole.Wagon))
        {
            if (model.Bodies is { Count: > 0 } bodies)
            {
                foreach (var body in bodies.Select(_transport.Catalog.Body))
                    buy.AddChild(MakeButton($"Купить: {model.Title} {body.Title}",
                        () => Act(() => _selected = _transport.Buy(depot.Id, model.Id, body.Id).Id)));
            }
            else
                buy.AddChild(MakeButton($"Купить: {model.Title}", () => Act(() => _selected = _transport.Buy(depot.Id, model.Id).Id)));
        }
        _body.AddChild(buy);

        var vehicles = _transport.InDepot(depot.Id).ToList();
        if (vehicles.Count == 0)
            _body.AddChild(Text("Техники пока нет.", 14));
        foreach (var vehicle in vehicles)
        {
            var row = MakeButton($"{vehicle.Name} — {VehicleText.Status(vehicle, _transport)}", () =>
            {
                _selected = vehicle.Id;
                Picking = null;
                Refresh();
            });
            row.ToggleMode = true;
            row.ButtonPressed = vehicle.Id == _selected;
            row.Alignment = HorizontalAlignment.Left;
            row.ClipText = true;
            _body.AddChild(row);
        }

        if (Selected is { } selected)
            AddDetails(selected);
        // Старые строки удаляются в конце кадра — подгоняем размер панели уже в следующем.
        _resizeFrames = 2;
    }

    private void AddDetails(Vehicle vehicle)
    {
        _body.AddChild(new HSeparator());
        _body.AddChild(Text(vehicle.Name, 16));
        _body.AddChild(Text(VehicleText.Composition(vehicle), 14));
        _body.AddChild(Text(VehicleText.Details(vehicle, _names), 14));

        if (vehicle.Network.Trains && vehicle.State == VehicleState.InDepot)
        {
            var wagons = new HFlowContainer();
            foreach (var model in _transport.ForSale(vehicle.Network.Network).Where(m => m.Role == UnitRole.Wagon))
                wagons.AddChild(MakeButton($"+ {model.Title}", () => Act(() => _transport.AddWagon(vehicle.Id, model.Id))));
            wagons.AddChild(MakeButton("− вагон", () => Act(() => _transport.RemoveWagon(vehicle.Id))));
            _body.AddChild(wagons);
        }

        var (a, b) = Draft(vehicle);
        _body.AddChild(RouteRow('A', a, vehicle));
        _body.AddChild(RouteRow('B', b, vehicle));
        var send = MakeButton(vehicle.A is null ? "Отправить по маршруту" : "Изменить маршрут", () =>
        {
            if (a is not { } stopA || b is not { } stopB)
            {
                _message.Text = "Выберите пункты А и Б.";
                return;
            }
            var problems = _transport.CheckRoute(vehicle.Id, stopA, stopB);
            if (problems.Count > 0)
            {
                _message.Text = string.Join(" ", problems);
                return;
            }
            Act(() => _transport.AssignRoute(vehicle.Id, stopA, stopB));
            _message.Text = $"{vehicle.Name} отправлен по маршруту.";
        });
        _body.AddChild(send);
    }

    private HBoxContainer RouteRow(char which, StopRef? stop, Vehicle vehicle)
    {
        var row = new HBoxContainer();
        var name = stop is { } s ? _transport.Stops.Find(s)?.Name ?? "снесён" : "не выбран";
        row.AddChild(Text($"{(which == 'A' ? "А" : "Б")}: {name}", 14, expand: true));
        var pick = MakeButton(Picking == which ? "щёлкните по карте…" : which == 'A' ? "Выбрать А" : "Выбрать Б", () =>
        {
            Picking = which;
            _message.Text = vehicle.Network.Trains
                ? $"Щёлкните по ж/д станции — пункт {(which == 'A' ? "А (погрузка)" : "Б (выгрузка)")}."
                : $"Щёлкните по автостанции или городскому пункту — пункт {(which == 'A' ? "А (погрузка)" : "Б (выгрузка)")}.";
            Refresh();
        });
        row.AddChild(pick);
        return row;
    }

    /// <summary>Выполнить действие игрока; ошибку показать текстом, а не падать.</summary>
    private void Act(Action action)
    {
        try
        {
            action();
            _message.Text = "";
        }
        catch (FleetException e)
        {
            _message.Text = e.Message;
        }
        Refresh();
    }

    private static Label Text(string text, int size, bool expand = false)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        if (expand)
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    private static Button MakeButton(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }
}
