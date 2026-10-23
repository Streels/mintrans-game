using Godot;

namespace Mintrans.Game.Input;

/// <summary>
/// Камера карты: WASD/стрелки — сдвиг, колесо — масштаб к курсору,
/// перетаскивание средней или правой кнопкой мыши — сдвиг.
/// </summary>
public partial class MapCamera : Camera2D
{
    private const float PanSpeed = 900f;
    private const float ZoomStep = 1.15f;
    private const float MinZoom = 0.05f;

    /// <summary>Самое сильное приближение: вагоны набора спрайтов остаются чёткими примерно до 37× (кадр 1:1).</summary>
    private const float MaxZoom = 32f;

    private bool _dragging;

    public override void _Process(double delta)
    {
        var move = Vector2.Zero;
        if (Godot.Input.IsKeyPressed(Key.W) || Godot.Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Godot.Input.IsKeyPressed(Key.S) || Godot.Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Godot.Input.IsKeyPressed(Key.A) || Godot.Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Godot.Input.IsKeyPressed(Key.D) || Godot.Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move != Vector2.Zero)
            Position += move.Normalized() * PanSpeed * (float)delta / Zoom.X;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheel:
                ZoomAt(wheel.Position, ZoomStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheel:
                ZoomAt(wheel.Position, 1f / ZoomStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle or MouseButton.Right } button:
                _dragging = button.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                Position -= motion.Relative / Zoom;
                break;
        }
    }

    /// <summary>Показать прямоугольник мира целиком в окне.</summary>
    public void Fit(Rect2 world)
    {
        var viewport = GetViewportRect().Size;
        var zoom = Mathf.Clamp(Mathf.Min(viewport.X / world.Size.X, viewport.Y / world.Size.Y) * 0.95f, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
        Position = world.GetCenter();
    }

    public void Focus(Vector2 world, float zoom)
    {
        zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
        Position = world;
    }

    private void ZoomAt(Vector2 screenPoint, float factor)
    {
        // Точка мира под курсором должна остаться под курсором.
        var fromCenter = screenPoint - GetViewportRect().Size / 2f;
        var anchor = Position + fromCenter / Zoom;
        var zoom = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
        Position = anchor - fromCenter / Zoom;
    }
}
