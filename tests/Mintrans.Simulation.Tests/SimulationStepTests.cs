using Mintrans.Core.Grid;
using Mintrans.Simulation.Commands;
using Mintrans.Simulation.Systems;
using Mintrans.Simulation.Time;

namespace Mintrans.Simulation.Tests;

/// <summary>Требования docs/03_simulation_time.md к шагу симуляции.</summary>
public class SimulationStepTests
{
    [Fact]
    public void One_step_is_one_game_hour()
    {
        var sim = TestSimulation.Create();
        sim.Step();
        Assert.Equal(new GameTime(1), sim.Time);
        sim.Run(GameTime.HoursPerWeek - 1);
        Assert.Equal(1, sim.Time.Week);
    }

    [Fact]
    public void Daily_systems_run_exactly_once_per_day()
    {
        var log = new List<string>();
        var sim = TestSimulation.Create();
        var hourly = new RecordingSystem("transport", Schedule.Hourly, Phases.Transport, log);
        var daily = new RecordingSystem("production", Schedule.Daily, Phases.Production, log);
        sim.Register(hourly);
        sim.Register(daily);

        sim.Run(24 * 3 + 5);

        Assert.Equal(24 * 3 + 5, hourly.Calls);
        Assert.Equal(3, daily.Calls);
    }

    [Fact]
    public void Day_boundary_order_is_arrivals_then_production_then_consumption_then_statistics()
    {
        var log = new List<string>();
        var sim = TestSimulation.Create();
        // Регистрируем вперемешку: порядок задают фазы, а не порядок регистрации.
        sim.Register(new RecordingSystem("statistics", Schedule.Daily, Phases.Statistics, log));
        sim.Register(new RecordingSystem("consumption", Schedule.Daily, Phases.Consumption, log));
        sim.Register(new RecordingSystem("production", Schedule.Daily, Phases.Production, log));
        sim.Register(new RecordingSystem("transport", Schedule.Hourly, Phases.Transport, log));

        sim.Run(24);

        Assert.Equal(["24:transport", "24:production", "24:consumption", "24:statistics"], log.TakeLast(4));
        Assert.Equal(27, log.Count); // 24 часа перевозок + 3 суточных расчёта
    }

    [Fact]
    public void Systems_with_equal_phase_keep_registration_order()
    {
        var log = new List<string>();
        var sim = TestSimulation.Create();
        sim.Register(new RecordingSystem("mines", Schedule.Daily, Phases.Production, log));
        sim.Register(new RecordingSystem("plants", Schedule.Daily, Phases.Production, log));
        sim.Run(24);
        Assert.Equal(["24:mines", "24:plants"], log);
    }

    [Fact]
    public void Player_command_takes_effect_from_next_step()
    {
        var sim = TestSimulation.Create();
        var log = new List<string>();
        sim.Register(new ProbeRoadSystem(log));

        sim.Step();
        sim.Enqueue(new DelegateCommand("построить дорогу",
            state => state.Map.Networks["road"].LayPath([new GridPos(1, 1), new GridPos(2, 1)])));
        // До следующего шага карта не изменилась.
        Assert.False(sim.State.Map.Networks["road"].HasAny(new GridPos(1, 1)));
        Assert.Equal(1, sim.PendingCommands);

        sim.Step();
        Assert.Equal(["1:нет дороги", "2:дорога есть"], log);
        Assert.Equal(0, sim.PendingCommands);
    }

    [Fact]
    public void Step_reports_completed_time()
    {
        var sim = TestSimulation.Create();
        var reported = new List<GameTime>();
        sim.StepCompleted += reported.Add;
        sim.Run(3);
        Assert.Equal([new GameTime(1), new GameTime(2), new GameTime(3)], reported);
    }

    [Fact]
    public void Module_data_is_attached_to_state_by_type()
    {
        var sim = TestSimulation.Create();
        sim.State.Add(new List<int> { 42 });
        Assert.Equal(42, sim.State.Get<List<int>>()[0]);
        Assert.Throws<InvalidOperationException>(() => sim.State.Add(new List<int>()));
        Assert.False(sim.State.TryGet<Dictionary<int, int>>(out _));
    }

    private sealed class ProbeRoadSystem(List<string> log) : ISimulationSystem
    {
        public string Name => "probe";
        public Schedule Schedule => Schedule.Hourly;
        public int Phase => Phases.Transport;

        public void Run(SimulationState state) =>
            log.Add($"{state.Time.Hours}:{(state.Map.Networks["road"].HasAny(new GridPos(1, 1)) ? "дорога есть" : "нет дороги")}");
    }
}
