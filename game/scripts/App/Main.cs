using Godot;
using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Data;
using Mintrans.Data.Definitions;
using Mintrans.Economy.Runtime;
using Mintrans.Economy.Systems;
using Mintrans.Game.Debug;
using Mintrans.Game.Input;
using Mintrans.Game.Tools;
using Mintrans.Game.UI;
using Mintrans.Game.View;
using Mintrans.Simulation;
using Mintrans.Transport.Reports;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Systems;
using Mintrans.World;

namespace Mintrans.Game.App;

/// <summary>
/// Точка сборки: загружает данные, создаёт карту и подключает к ней независимые узлы отображения.
/// Новый слой отображения добавляется здесь одной строкой и не трогает остальные.
/// </summary>
public partial class Main : Node2D
{
    private const string StylePath = "res://styles/map_style.json";
    private const string NamesPath = "res://styles/names.ru.json";

    private WorldMap? _map;
    private IsoProjection _projection = null!;
    private MapCamera _camera = null!;
    private CellHighlight _highlight = null!;
    private Hud _hud = null!;
    private DisplayNames _names = null!;
    private EconomyState? _economy;
    private FleetPanel _fleet = null!;
    private GameSimulation? _simulation;
    private TransportState? _transport;

    public override void _Ready()
    {
        var args = AppArgs.FromCommandLine();
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _camera = new MapCamera { Name = "Camera" };
        AddChild(_camera);

        try
        {
            Build(args);
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            _hud.ShowError(e.Message);
        }

        if (args.UiDemo is { } demo && _map is not null && _simulation is { } simulation)
        {
            var scenario = new UiScenario
            {
                ScreenshotPath = args.Screenshot,
                Report = () => _transport?.Vehicles.Select(v => $"{v.Name}: {v.State}, {v.Reason}, груз {v.Cargo}") ?? [],
            };
            AddChild(scenario);
            scenario.Init(_map, _projection, demo, simulation.Run, () => _transport?.Vehicles
                .Where(v => v.Network.Trains && v.State is VehicleState.Travelling or VehicleState.Loading or VehicleState.Unloading)
                .Select(v => (Core.Grid.GridPos?)v.Position).FirstOrDefault());
        }
        else if (args.Screenshot is { } path)
            AddChild(new ScreenshotCapture { Path = path });
    }

    private void Build(AppArgs args)
    {
        var contentRoot = args.Content ?? ProjectSettings.GlobalizePath("res://../content");
        var content = GameContent.Load(contentRoot);
        var description = content.LoadMap(args.Map);
        var map = description.Map;
        var style = MapStyle.Load(StylePath);
        _names = DisplayNames.Load(NamesPath);
        _projection = new IsoProjection(tileWidth: 32, tileHeight: 16, heightStep: 4);

        // Порядок добавления = порядок отрисовки: рельеф → площадки → сети → подсветка → подписи.
        var terrain = new TerrainView { Name = "Terrain" };
        AddChild(terrain);
        terrain.Init(map, _projection, style);

        var sites = new SiteView { Name = "Sites" };
        AddChild(sites);
        sites.Init(map, _projection, style);

        var networks = new NetworkView { Name = "Networks" };
        AddChild(networks);
        networks.Init(map, _projection, style);

        var facilities = new FacilityView { Name = "Facilities" };
        AddChild(facilities);
        facilities.Init(map, _projection, style);

        // Техника рисуется поверх сетей, сигналы и переезды — поверх техники; данные для них появятся ниже, вместе с симуляцией.
        var vehicles = new VehicleView { Name = "Vehicles" };
        AddChild(vehicles);
        var signals = new SignalView { Name = "Signals" };
        AddChild(signals);

        _highlight = new CellHighlight { Name = "Highlight" };
        AddChild(_highlight);
        _highlight.Init(map, _projection, style);

        // Симуляция и её модули: экономика (склады, добыча, переработка, спрос) и транспорт (техника, маршруты).
        var simulation = _simulation = new GameSimulation(new SimulationState(map, calendar: content.Simulation.Calendar));
        // Сообщения игроку (новая техника и т. п.) — строкой вверху экрана.
        simulation.State.Log.Posted += message =>
            _hud.ShowNews($"{simulation.State.Calendar.FormatDate(message.Time)}: {message.Text}");
        _economy = EconomyModule.Install(simulation, content.Economy);
        EconomyLoader.SeedStocks(_economy, description);
        var transport = _transport = TransportModule.Install(simulation, content.Transport);

        var labels = new SiteLabels { Name = "SiteLabels" };
        AddChild(labels);
        labels.Init(map, _projection, _camera,
            site => _economy.At(site.Id) is { } economic ? EconomyText.Status(economic, _names) : null);

        MoveChild(_camera, -1);
        _hud.SetTitle(description.Name);
        _map = map;

        var driver = new SimulationDriver { Name = "Simulation" };
        AddChild(driver);
        driver.Init(simulation, content.Simulation);
        driver.Runner.Paused = args.Paused;
        vehicles.Init(map, _projection, transport, driver.Runner, style, LoadTrainSprites(style));
        signals.Init(map, _projection, style, transport);

        // Панель депо: покупка техники и маршруты.
        _fleet = new FleetPanel { Name = "Fleet" };
        _hud.AddWidget(_fleet);
        _fleet.Init(transport, _economy, () => simulation.State.Time.Day, map, _names);

        // Каждый час обновляются панель депо, панель клетки (очереди) и сигналы, раз в сутки — подписи объектов.
        simulation.StepCompleted += time =>
        {
            _fleet.Refresh();
            signals.Refresh();
            RefreshCellInfo();
            if (time.IsStartOfDay)
                labels.Refresh();
        };
        if (args.SkipHours > 0)
            simulation.Run(args.SkipHours);
        var timeControls = new TimeControls { Name = "TimeControls" };
        _hud.AddWidget(timeControls);
        timeControls.Init(driver);

        // Строительство: правила — в ядре (Builder), здесь только ввод мышью и подсветка.
        var builder = new Builder(map, content.Construction);
        builder.AddDemolishGuard(new DelegateDemolishGuard(f => TransportModule.WhyFacilityInUse(transport, f)));
        // Путь под поездом и закреплённый за ним участок не сносятся (docs/10в, раздел 7).
        map.Rails?.Rules.Add(TransportModule.RailGuard(transport));
        var build = new BuildController { Name = "Build" };
        AddChild(build);
        build.Init(map, _projection);
        // Пока выбирается радиус соединения рельсов, колесо мыши меняет радиус, а не масштаб.
        _camera.InputCaptured = () => build.CapturesWheel;
        // Щелчок без инструмента: выбор пункта маршрута или открытие депо.
        build.CellClicked += cell =>
        {
            if (_fleet.Picking is not null)
                _fleet.Pick(cell);
            else if (map.Facilities.At(cell) is { } facility && transport.Catalog.NetworkByDepot(facility.Kind) is not null)
                _fleet.Open(facility);
        };
        var toolBar = new ToolBar { Name = "ToolBar" };
        _hud.AddWidget(toolBar);
        toolBar.Init(build, BuildTools.Create(builder), _names);

        // --focus=ключ объекта или --focus=x,y клетки.
        if (args.Focus is { } key && map.Sites.TryGetByKey(key, out var site))
            _camera.Focus(_projection.CellCenter(site.Base.Center, SiteView.SiteHeight(map, site)), args.Zoom ?? 1.5f);
        else if (args.Focus?.Split(',') is [var fx, var fy] && int.TryParse(fx, out var x) && int.TryParse(fy, out var y)
                 && map.Contains(new GridPos(x, y)))
            _camera.Focus(_projection.CellCenter(new GridPos(x, y), map.Terrain.Height[new GridPos(x, y)]), args.Zoom ?? 1.5f);
        else
            _camera.Fit(MapBounds(map));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_map is not null && @event is InputEventKey { Keycode: Key.F, Pressed: true, Echo: false })
            _camera.Fit(MapBounds(_map));
    }

    public override void _Process(double delta)
    {
        if (_map is null)
            return;
        var cell = _projection.ScreenToCell(GetGlobalMousePosition(), _map.Bounds, c => _map.Terrain.Height[c]);
        if (cell == _highlight.Cell)
            return;
        _highlight.Cell = cell;
        RefreshCellInfo();
    }

    private void RefreshCellInfo()
    {
        if (_map is null || _highlight.Cell is not { } cell)
        {
            _hud.SetCellInfo(null);
            return;
        }
        var lines = CellDescriber.Describe(_map, cell, _names).ToList();
        if (_map.Sites.At(cell) is { } site && _economy?.At(site.Id) is { } economic)
        {
            lines.AddRange(EconomyText.Details(economic, _names));
            // Груз в пути к объекту (docs/12): в технике, которая везёт его сюда.
            if (_transport is not null)
            {
                foreach (var (resource, (amount, count)) in RouteReport.InTransitTo(_transport, site.Id))
                    lines.Add($"  в пути сюда: {_names[resource.Value]} {amount} ({count} ед. техники)");
            }
        }
        // Пункт погрузки: сколько техники у него и сколько ждёт очереди (docs/10, docs/12).
        foreach (var stop in _transport?.Stops.All.Where(s => s.Contains(cell)) ?? [])
        {
            var here = _transport!.AtStop(stop.Ref);
            var queued = here.Count(v => v.Queued);
            lines.Add(here.Count == 0
                ? $"Пункт ({_names[stop.Network.Value]}): техники нет"
                : $"Пункт ({_names[stop.Network.Value]}): техники {here.Count}, в очереди {queued}");
        }
        _hud.SetCellInfo(lines);
    }

    /// <summary>Набор спрайтов вагонов из стиля; если его нет или он не читается — поезда рисуются линией.</summary>
    private static TrainSprites? LoadTrainSprites(MapStyle style)
    {
        if (style.TrainSprites is not { } path)
            return null;
        try
        {
            return TrainSprites.Load(path);
        }
        catch (Exception e)
        {
            GD.PushWarning($"Спрайты поездов не загружены, рисую линией: {e.Message}");
            return null;
        }
    }

    private Rect2 MapBounds(WorldMap map)
    {
        var corners = _projection.RectCorners(map.Bounds, 0);
        var rect = new Rect2(corners[0], Vector2.Zero);
        foreach (var corner in corners)
            rect = rect.Expand(corner);
        return rect;
    }
}
