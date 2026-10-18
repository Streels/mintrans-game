using Godot;
using Mintrans.Simulation;
using Mintrans.Simulation.Time;

namespace Mintrans.Game.App;

/// <summary>
/// Связывает симуляцию с кадрами Godot: передаёт время кадра регулятору и обрабатывает
/// клавиши времени. Сама логика шага живёт в ядре и от Godot не зависит.
/// Клавиши: пробел — пауза, 1–4 — скорость, точка — один час вручную.
/// </summary>
public partial class SimulationDriver : Node
{
    public GameSimulation Simulation { get; private set; } = null!;
    public SimulationRunner Runner { get; private set; } = null!;

    /// <summary>Изменились пауза или скорость — интерфейсу пора обновиться.</summary>
    public event Action? ControlsChanged;

    public void Init(GameSimulation simulation, SimulationSettings settings)
    {
        Simulation = simulation;
        Runner = new SimulationRunner(simulation, settings);
    }

    public override void _Process(double delta) => Runner.Advance(delta);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.Space:
                TogglePause();
                break;
            case >= Key.Key1 and <= Key.Key9 when key.Keycode - Key.Key1 < Runner.Settings.Speeds.Count:
                SetSpeed((int)(key.Keycode - Key.Key1));
                break;
            case Key.Period:
                Runner.StepOnce();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public void TogglePause()
    {
        Runner.Paused = !Runner.Paused;
        ControlsChanged?.Invoke();
    }

    public void SetSpeed(int index)
    {
        Runner.SpeedIndex = index;
        Runner.Paused = false;
        ControlsChanged?.Invoke();
    }
}
