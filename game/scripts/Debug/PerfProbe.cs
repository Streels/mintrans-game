using Godot;
using Mintrans.Game.Input;

namespace Mintrans.Game.Debug;

/// <summary>
/// Замер отрисовки (параметр --perf): по очереди ставит камеру на заданные виды, на каждом пропускает несколько кадров
/// и меряет следующие — среднее и худшее время кадра, сколько из него заняла подготовка отрисовки
/// на процессоре, вызовы отрисовки. Вертикальная синхронизация на время замера
/// выключена, чтобы время кадра не упиралось в частоту экрана. С --screenshot сохраняет кадр каждого вида. В конце закрывает игру.
/// </summary>
public partial class PerfProbe : Node
{
    private const int WarmupFrames = 20;
    private const int MeasureFrames = 120;

    private readonly List<(string Name, Action Apply)> _views = [];
    private readonly List<double> _frames = [];
    private readonly List<double> _render = [];
    private MapCamera _camera = null!;
    private int _view = -1;
    private int _frame;
    private ulong _last;

    public string? ScreenshotPath { get; set; }

    public void Init(MapCamera camera, double loadSeconds)
    {
        _camera = camera;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        GD.Print($"[perf] загрузка карты и сборка отображения: {loadSeconds:0.00} с, " +
                 $"память {OS.GetStaticMemoryUsage() / 1024.0 / 1024.0:0} МБ (без управляемой), управляемая {GC.GetTotalMemory(false) / 1024.0 / 1024.0:0} МБ");
    }

    /// <summary>Вид для замера: имя для журнала и что сделать с камерой.</summary>
    public void AddView(string name, Action<MapCamera> apply) => _views.Add((name, () => apply(_camera)));

    public override void _Process(double delta)
    {
        var now = Time.GetTicksUsec();
        if (_view >= 0 && _frame > WarmupFrames)
        {
            _frames.Add((now - _last) / 1000.0);
            _render.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) +
                        RenderingServer.GetFrameSetupTimeCpu());
        }
        _last = now;

        if (_view >= 0 && ++_frame <= WarmupFrames + MeasureFrames)
            return;
        if (_view >= 0)
            Finish(_views[_view].Name);
        if (++_view >= _views.Count)
        {
            GetTree().Quit();
            return;
        }
        _frame = 0;
        _frames.Clear();
        _render.Clear();
        _views[_view].Apply();
    }

    private void Finish(string name)
    {
        var sorted = _frames.Order().ToList();
        var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        GD.Print($"[perf] {name}: кадр в среднем {_frames.Average():0.00} мс ({1000 / _frames.Average():0} к/с), " +
                 $"медиана {sorted[sorted.Count / 2]:0.00} мс, худший {sorted[^1]:0.00} мс; " +
                 $"из них подготовка отрисовки на процессоре {_render.Average():0.00} мс; " +
                 $"вызовов отрисовки {draws:0}, примитивов {primitives:0}");
        if (ScreenshotPath is { } path)
        {
            var file = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;
            GetViewport().GetTexture().GetImage().SavePng($"{file}_{_view}.png");
        }
    }
}
