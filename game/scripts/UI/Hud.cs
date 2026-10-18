using Godot;

namespace Mintrans.Game.UI;

/// <summary>Интерфейс поверх карты: заголовок с подсказкой управления и панель сведений о клетке.</summary>
public partial class Hud : CanvasLayer
{
    private Label _title = null!;
    private Label _cellInfo = null!;
    private PanelContainer _cellPanel = null!;

    public override void _Ready()
    {
        _title = MakeLabel(16);
        var titlePanel = Panel(_title);
        titlePanel.Position = new Vector2(12, 12);
        AddChild(titlePanel);

        _cellInfo = MakeLabel(15);
        _cellPanel = Panel(_cellInfo);
        _cellPanel.Visible = false;
        AddChild(_cellPanel);
    }

    public void SetTitle(string mapName) =>
        _title.Text = $"{mapName}\nWASD / стрелки, правая кнопка — сдвиг · колесо — масштаб · F — вся карта\n"
                      + "Пробел — пауза · 1–4 — скорость · . — один час";

    private Label? _news;
    private double _newsLeft;

    /// <summary>Показать сообщение вверху экрана на некоторое время.</summary>
    public void ShowNews(string text)
    {
        if (_news is null)
        {
            _news = MakeLabel(16);
            _news.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.6f));
            _news.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            _news.AddThemeConstantOverride("outline_size", 6);
            AddChild(_news);
        }
        _news.Text = text;
        _news.Visible = true;
        _newsLeft = 15;
    }

    public override void _Process(double delta)
    {
        if (_news is null || !_news.Visible)
            return;
        _news.ResetSize();
        _news.Position = new Vector2((GetViewport().GetVisibleRect().Size.X - _news.Size.X) / 2f, 108);
        _newsLeft -= delta;
        if (_newsLeft <= 0)
            _news.Visible = false;
    }

    /// <summary>Добавить на экран готовый элемент интерфейса (например, панель времени).</summary>
    public void AddWidget(Control widget) => AddChild(widget);

    public void SetCellInfo(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            _cellPanel.Visible = false;
            return;
        }
        _cellInfo.Text = string.Join("\n", lines);
        _cellPanel.Visible = true;
        _cellPanel.ResetSize();
        var viewport = GetViewport().GetVisibleRect().Size;
        _cellPanel.Position = new Vector2(12, viewport.Y - _cellPanel.Size.Y - 12);
    }

    public void ShowError(string message)
    {
        var label = MakeLabel(16);
        label.Text = "Ошибка загрузки данных:\n" + message;
        label.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.5f));
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(900, 0);
        var panel = Panel(label);
        panel.Position = new Vector2(12, 90);
        AddChild(panel);
    }

    private static Label MakeLabel(int size)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    /// <summary>Общий вид панелей интерфейса.</summary>
    public static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = new Color(0.08f, 0.09f, 0.1f, 0.82f),
        ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
    };

    private static PanelContainer Panel(Control content)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        panel.AddChild(content);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        return panel;
    }
}
