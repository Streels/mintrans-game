using Godot;
using Mintrans.Game.App;

namespace Mintrans.Game.UI;

/// <summary>Панель времени: текущая дата, пауза и кнопки скоростей из настроек.</summary>
public partial class TimeControls : PanelContainer
{
    private SimulationDriver _driver = null!;
    private Label _clock = null!;
    private Button _pause = null!;
    private readonly List<Button> _speeds = new();

    public void Init(SimulationDriver driver)
    {
        _driver = driver;
        AddThemeStyleboxOverride("panel", Hud.PanelStyle());

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        AddChild(row);

        _clock = new Label { CustomMinimumSize = new Vector2(150, 0), VerticalAlignment = VerticalAlignment.Center };
        _clock.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(_clock);

        _pause = MakeButton("Пауза", row);
        _pause.Pressed += _driver.TogglePause;

        var speeds = _driver.Runner.Settings.Speeds;
        for (var i = 0; i < speeds.Count; i++)
        {
            var index = i;
            var button = MakeButton($"{speeds[i]}×", row);
            button.Pressed += () => _driver.SetSpeed(index);
            _speeds.Add(button);
        }

        _driver.ControlsChanged += Refresh;
        Refresh();
    }

    public override void _Process(double delta)
    {
        _clock.Text = _driver.Simulation.Time.ToString();
        // Держим панель в правом верхнем углу при любом размере окна.
        Position = new Vector2(GetViewportRect().Size.X - Size.X - 12, 12);
    }

    private void Refresh()
    {
        _pause.ButtonPressed = _driver.Runner.Paused;
        for (var i = 0; i < _speeds.Count; i++)
            _speeds[i].ButtonPressed = !_driver.Runner.Paused && _driver.Runner.SpeedIndex == i;
    }

    private static Button MakeButton(string text, Container parent)
    {
        var button = new Button { Text = text, ToggleMode = true, FocusMode = FocusModeEnum.None };
        parent.AddChild(button);
        return button;
    }
}
