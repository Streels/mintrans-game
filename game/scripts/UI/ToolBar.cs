using Godot;
using Mintrans.Game.Tools;

namespace Mintrans.Game.UI;

/// <summary>Панель инструментов внизу экрана и строка состояния над ней.</summary>
public partial class ToolBar : VBoxContainer
{
    private BuildController _controller = null!;
    private Label _status = null!;
    private readonly Dictionary<IBuildTool, Button> _buttons = new();
    private Button _inspect = null!;

    public void Init(BuildController controller, IReadOnlyList<IBuildTool> tools, DisplayNames names)
    {
        _controller = controller;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _status.AddThemeFontSizeOverride("font_size", 15);
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _status.AddThemeConstantOverride("outline_size", 5);
        AddChild(_status);

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Hud.PanelStyle());
        AddChild(panel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        panel.AddChild(row);

        var group = new ButtonGroup();
        _inspect = MakeButton(names["tool.inspect"], group, row);
        _inspect.Pressed += () => _controller.SetTool(null);
        foreach (var tool in tools)
        {
            var button = MakeButton(names[tool.Id], group, row);
            button.Pressed += () => _controller.SetTool(tool);
            _buttons[tool] = button;
        }

        _controller.StatusChanged += text => _status.Text = text;
        _controller.ToolChanged += OnToolChanged;
        OnToolChanged(null);
    }

    public override void _Process(double delta)
    {
        var viewport = GetViewportRect().Size;
        Position = new Vector2((viewport.X - Size.X) / 2f, viewport.Y - Size.Y - 12);
    }

    private void OnToolChanged(IBuildTool? tool)
    {
        (tool is not null && _buttons.TryGetValue(tool, out var button) ? button : _inspect).ButtonPressed = true;
        _status.Text = tool is null
            ? "Выберите инструмент. Левая кнопка — протянуть, отпустить — построить, Esc — отмена."
            : "Протяните мышью с зажатой левой кнопкой.";
    }

    private static Button MakeButton(string text, ButtonGroup group, Container parent)
    {
        var button = new Button { Text = text, ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None };
        parent.AddChild(button);
        return button;
    }
}
