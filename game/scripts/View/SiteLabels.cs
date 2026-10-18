using Godot;
using Mintrans.World;
using Mintrans.World.Sites;

namespace Mintrans.Game.View;

/// <summary>
/// Подписи объектов: название и строка состояния под ним (её текст даёт внешний источник,
/// например экономика). Сохраняют постоянный размер на экране при любом масштабе камеры.
/// </summary>
public partial class SiteLabels : Node2D
{
    // Каждая подпись висит на своей точке привязки; масштабируется точка, а не сама подпись.
    private readonly List<(Site Site, Node2D Anchor, Label Name, Label Status)> _labels = new();
    private Camera2D? _camera;
    private Func<Site, string?> _status = _ => null;

    public void Init(WorldMap map, IsoProjection projection, Camera2D camera, Func<Site, string?>? status = null)
    {
        _camera = camera;
        _status = status ?? _status;
        foreach (var site in map.Sites.All)
        {
            var anchor = new Node2D
            {
                // Верхний угол площадки — самая высокая её точка на экране.
                Position = projection.GridPoint(site.Base.X, site.Base.Y, SiteView.SiteHeight(map, site)),
            };
            var name = MakeLabel(site.Name, 18, Colors.White);
            var line = MakeLabel("", 14, new Color(1f, 0.92f, 0.7f));
            anchor.AddChild(name);
            anchor.AddChild(line);
            AddChild(anchor);
            _labels.Add((site, anchor, name, line));
        }
        Refresh();
    }

    /// <summary>Перечитать строки состояния (например, после суточного расчёта).</summary>
    public void Refresh()
    {
        foreach (var (site, _, _, status) in _labels)
        {
            status.Text = _status(site) ?? "";
            status.Visible = status.Text.Length > 0;
        }
    }

    public override void _Process(double delta)
    {
        if (_camera is null)
            return;
        var scale = Vector2.One / _camera.Zoom;
        foreach (var (_, anchor, name, status) in _labels)
        {
            anchor.Scale = scale;
            var statusHeight = status.Visible ? status.Size.Y : 0;
            name.Position = new Vector2(-name.Size.X / 2f, -name.Size.Y - statusHeight - 4f);
            status.Position = new Vector2(-status.Size.X / 2f, -statusHeight - 4f);
        }
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }
}
