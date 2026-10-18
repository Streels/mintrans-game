using Godot;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>Подписи объектов. Сохраняют постоянный размер на экране при любом масштабе камеры.</summary>
public partial class SiteLabels : Node2D
{
    // Каждая подпись висит на своей точке привязки; масштабируется точка, а не сама подпись.
    private readonly List<(Node2D Anchor, Label Label)> _labels = new();
    private Camera2D? _camera;

    public void Init(WorldMap map, IsoProjection projection, Camera2D camera)
    {
        _camera = camera;
        foreach (var site in map.Sites.All)
        {
            var anchor = new Node2D
            {
                // Верхний угол площадки — самая высокая её точка на экране.
                Position = projection.GridPoint(site.Base.X, site.Base.Y, SiteView.SiteHeight(map, site)),
            };
            var label = new Label { Text = site.Name };
            label.AddThemeFontSizeOverride("font_size", 18);
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            label.AddThemeConstantOverride("outline_size", 6);
            anchor.AddChild(label);
            AddChild(anchor);
            _labels.Add((anchor, label));
        }
    }

    public override void _Process(double delta)
    {
        if (_camera is null)
            return;
        var scale = Vector2.One / _camera.Zoom;
        foreach (var (anchor, label) in _labels)
        {
            anchor.Scale = scale;
            label.Position = new Vector2(-label.Size.X / 2f, -label.Size.Y - 4f);
        }
    }
}
