using Godot;

namespace Mintrans.Game.App;

/// <summary>
/// Параметры запуска после "--" в командной строке Godot:
/// --content=путь  --map=prototype  --focus=city  --zoom=1.5  --screenshot=файл.png
/// --paused (начать на паузе)  --hours=26 (сразу промотать столько игровых часов)
/// --ui-demo[=build|chain] (сценарий проверки интерфейса мышью; с --screenshot сохраняет кадр в конце)
/// </summary>
public sealed record AppArgs(
    string? Content, string Map, string? Focus, float? Zoom, string? Screenshot, bool Paused, long SkipHours, string? UiDemo)
{
    public static AppArgs Parse(IEnumerable<string> args)
    {
        var list = args.ToList();
        var values = list
            .Where(a => a.StartsWith("--", StringComparison.Ordinal) && a.Contains('='))
            .Select(a => a[2..].Split('=', 2))
            .ToDictionary(p => p[0], p => p[1]);

        return new AppArgs(
            values.GetValueOrDefault("content"),
            values.GetValueOrDefault("map") ?? "prototype",
            values.GetValueOrDefault("focus"),
            values.TryGetValue("zoom", out var zoom) && float.TryParse(zoom, System.Globalization.CultureInfo.InvariantCulture, out var z) ? z : null,
            values.GetValueOrDefault("screenshot"),
            list.Contains("--paused"),
            values.TryGetValue("hours", out var hours) && long.TryParse(hours, out var h) ? h : 0,
            values.GetValueOrDefault("ui-demo") ?? (list.Contains("--ui-demo") ? "build" : null));
    }

    public static AppArgs FromCommandLine() => Parse(OS.GetCmdlineUserArgs());
}
