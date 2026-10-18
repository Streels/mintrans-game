using Mintrans.Simulation.Systems;
using Mintrans.Simulation.Time;

namespace Mintrans.Simulation.Tests;

/// <summary>Пауза, скорости и независимость результата от скорости (docs/03).</summary>
public class SimulationRunnerTests
{
    private static readonly SimulationSettings Settings = new(SecondsPerHourAt1x: 2.0, Speeds: [1, 2, 4, 8], MaxStepsPerFrame: 1000);

    [Fact]
    public void Speed_changes_only_how_many_steps_happen_per_second()
    {
        var sim = TestSimulation.Create();
        var runner = new SimulationRunner(sim, Settings);

        runner.Advance(4.0); // 1×: 4 с / 2 с = 2 часа
        Assert.Equal(2, sim.Time.Hours);

        runner.SpeedIndex = 3; // 8×
        runner.Advance(4.0);
        Assert.Equal(2 + 16, sim.Time.Hours);
    }

    [Fact]
    public void Pause_stops_time_but_manual_step_works()
    {
        var sim = TestSimulation.Create();
        var runner = new SimulationRunner(sim, Settings) { Paused = true };

        Assert.Equal(0, runner.Advance(100));
        Assert.Equal(0, sim.Time.Hours);

        runner.StepOnce();
        Assert.Equal(1, sim.Time.Hours);
    }

    [Theory]
    [InlineData(0, 1.0 / 60)]  // 1×, 60 кадров в секунду
    [InlineData(1, 1.0 / 30)]  // 2×, 30 кадров
    [InlineData(3, 0.25)]      // 8×, редкие кадры
    public void Same_number_of_hours_gives_same_result_at_any_speed(int speedIndex, double frameSeconds)
    {
        const int targetHours = 24 * 5;
        var reference = Play(speedIndex: 0, frameSeconds: 1.0 / 60, targetHours);
        var tested = Play(speedIndex, frameSeconds, targetHours);
        Assert.Equal(reference, tested);
    }

    [Fact]
    public void Slow_computer_slows_the_game_instead_of_freezing()
    {
        var sim = TestSimulation.Create();
        var runner = new SimulationRunner(sim, Settings with { MaxStepsPerFrame = 5 });
        var steps = runner.Advance(1000);
        Assert.Equal(5, steps);
        // Долг не копится: следующий короткий кадр не вызывает лавину шагов.
        Assert.True(runner.Advance(0.001) <= 1);
    }

    [Fact]
    public void Speed_index_is_clamped_to_available_speeds()
    {
        var runner = new SimulationRunner(TestSimulation.Create(), Settings) { SpeedIndex = 10 };
        Assert.Equal(8, runner.Speed);
        runner.SpeedIndex = -1;
        Assert.Equal(1, runner.Speed);
    }

    /// <summary>
    /// Играет с заданной скоростью и частотой кадров, пока не пройдёт <paramref name="targetHours"/>,
    /// и возвращает журнал вызовов систем за эти часы.
    /// </summary>
    private static List<string> Play(int speedIndex, double frameSeconds, int targetHours)
    {
        var log = new List<string>();
        var sim = TestSimulation.Create();
        sim.Register(new RecordingSystem("transport", Schedule.Hourly, Phases.Transport, log));
        sim.Register(new RecordingSystem("production", Schedule.Daily, Phases.Production, log));
        var runner = new SimulationRunner(sim, Settings) { SpeedIndex = speedIndex };
        while (sim.Time.Hours < targetHours)
            runner.Advance(frameSeconds);
        // Последний кадр мог захватить лишние часы — сравниваем ровно targetHours.
        return log.Where(entry => long.Parse(entry.Split(':')[0]) <= targetHours).ToList();
    }
}
