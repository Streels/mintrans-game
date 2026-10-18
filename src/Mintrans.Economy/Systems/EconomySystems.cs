using Mintrans.Economy.Definitions;
using Mintrans.Economy.Runtime;
using Mintrans.Simulation;
using Mintrans.Simulation.Systems;

namespace Mintrans.Economy.Systems;

/// <summary>
/// Суточный расчёт процессов одной фазы на всех объектах (docs/03): шахты добывают, заводы перерабатывают,
/// город потребляет. Объекты обходятся в постоянном порядке, поэтому результат воспроизводим.
/// </summary>
public sealed class ProcessSystem(ProcessPhase processPhase) : ISimulationSystem
{
    public string Name => $"economy.{processPhase}";
    public Schedule Schedule => Schedule.Daily;

    public int Phase => processPhase switch
    {
        ProcessPhase.Production => Phases.Production,
        ProcessPhase.Consumption => Phases.Consumption,
        _ => throw new ArgumentOutOfRangeException(nameof(processPhase)),
    };

    public void Run(SimulationState state)
    {
        foreach (var site in state.Get<EconomyState>().Sites)
            site.RunProcesses(processPhase);
    }
}

/// <summary>Закрывает учёт суток после всех расчётов: «вчера» становится историей, начинается новый день.</summary>
public sealed class DailyStatisticsSystem : ISimulationSystem
{
    public string Name => "economy.statistics";
    public Schedule Schedule => Schedule.Daily;
    public int Phase => Phases.Statistics;

    public void Run(SimulationState state)
    {
        foreach (var site in state.Get<EconomyState>().Sites)
            site.CloseDay(state.Time.Day);
    }
}

/// <summary>Подключение экономики к симуляции: данные в состояние и три суточные системы.</summary>
public static class EconomyModule
{
    public static EconomyState Install(GameSimulation simulation, EconomyCatalog catalog)
    {
        var economy = new EconomyState(catalog, simulation.State.Map, simulation.Time.Day);
        simulation.State.Add(economy);
        simulation.Register(new ProcessSystem(ProcessPhase.Production));
        simulation.Register(new ProcessSystem(ProcessPhase.Consumption));
        simulation.Register(new DailyStatisticsSystem());
        return economy;
    }
}
