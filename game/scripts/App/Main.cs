using Godot;
using Mintrans.Construction;
using Mintrans.Data;
using Mintrans.Game.Debug;
using Mintrans.Game.Input;
using Mintrans.Game.Tools;
using Mintrans.Game.UI;
using Mintrans.Game.View;
using Mintrans.Simulation;
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

        if (args.UiDemo && _map is not null)
        {
            var scenario = new UiScenario { ScreenshotPath = args.Screenshot };
            AddChild(scenario);
            scenario.Init(_map, _projection);
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

        _highlight = new CellHighlight { Name = "Highlight" };
        AddChild(_highlight);
        _highlight.Init(map, _projection, style);

        var labels = new SiteLabels { Name = "SiteLabels" };
        AddChild(labels);
        labels.Init(map, _projection, _camera);

        MoveChild(_camera, -1);
        _hud.SetTitle(description.Name);
        _map = map;

        // Симуляция: пока в ней только время; системы экономики и транспорта подключатся сюда же.
        var simulation = new GameSimulation(new SimulationState(map));
        var driver = new SimulationDriver { Name = "Simulation" };
        AddChild(driver);
        driver.Init(simulation, content.Simulation);
        driver.Runner.Paused = args.Paused;
        if (args.SkipHours > 0)
            simulation.Run(args.SkipHours);
        var timeControls = new TimeControls { Name = "TimeControls" };
        _hud.AddWidget(timeControls);
        timeControls.Init(driver);

        // Строительство: правила — в ядре (Builder), здесь только ввод мышью и подсветка.
        var builder = new Builder(map, content.Construction);
        var build = new BuildController { Name = "Build" };
        AddChild(build);
        build.Init(map, _projection);
        var toolBar = new ToolBar { Name = "ToolBar" };
        _hud.AddWidget(toolBar);
        toolBar.Init(build, BuildTools.Create(builder), _names);

        if (args.Focus is { } key && map.Sites.TryGetByKey(key, out var site))
            _camera.Focus(_projection.CellCenter(site.Base.Center, SiteView.SiteHeight(map, site)), args.Zoom ?? 1.5f);
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
        _hud.SetCellInfo(cell is { } c ? CellDescriber.Describe(_map, c, _names) : null);
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
