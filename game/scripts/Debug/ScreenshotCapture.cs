using Godot;

namespace Mintrans.Game.Debug;

/// <summary>Сохраняет кадр в файл и закрывает игру. Нужен для автоматической проверки отображения.</summary>
public partial class ScreenshotCapture : Node
{
    private int _frames;

    public string Path { get; set; } = "screenshot.png";
    public int WaitFrames { get; set; } = 10;

    public override void _Process(double delta)
    {
        if (++_frames < WaitFrames)
            return;
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(Path);
        GD.Print(error == Error.Ok ? $"Скриншот сохранён: {Path}" : $"Не удалось сохранить скриншот {Path}: {error}");
        GetTree().Quit();
    }
}
