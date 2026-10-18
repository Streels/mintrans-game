using System.Collections.Concurrent;
using Mintrans.Simulation.Commands;
using Mintrans.Simulation.Systems;
using Mintrans.Simulation.Time;

namespace Mintrans.Simulation;

/// <summary>
/// Пошаговая симуляция: один вызов <see cref="Step"/> — один игровой час.
/// Порядок шага:
/// 1) применить накопленные команды игрока;
/// 2) выполнить системы, которым пора работать, по возрастанию <see cref="ISimulationSystem.Phase"/>
///    (при равной фазе — в порядке регистрации).
/// Результат зависит только от числа шагов и команд, но не от скорости игры или частоты кадров.
/// </summary>
public sealed class GameSimulation
{
    private readonly List<(ISimulationSystem System, int Order)> _systems = new();
    private readonly ConcurrentQueue<IGameCommand> _commands = new();

    public GameSimulation(SimulationState state)
    {
        State = state;
    }

    public SimulationState State { get; }
    public GameTime Time => State.Time;

    public IReadOnlyList<ISimulationSystem> Systems => _systems.Select(s => s.System).ToList();

    /// <summary>Вызывается после каждого шага с новым временем — для интерфейса и журнала.</summary>
    public event Action<GameTime>? StepCompleted;

    public void Register(ISimulationSystem system)
    {
        if (system.Schedule.PeriodHours <= 0)
            throw new ArgumentException($"У системы «{system.Name}» период должен быть положительным.");
        _systems.Add((system, _systems.Count));
        // Сортировка один раз при регистрации: шаг не тратит на неё время.
        _systems.Sort((a, b) => a.System.Phase != b.System.Phase
            ? a.System.Phase.CompareTo(b.System.Phase)
            : a.Order.CompareTo(b.Order));
    }

    /// <summary>Поставить действие игрока в очередь. Безопасно вызывать из потока интерфейса.</summary>
    public void Enqueue(IGameCommand command) => _commands.Enqueue(command);

    public int PendingCommands => _commands.Count;

    /// <summary>Выполнить один игровой час.</summary>
    public void Step()
    {
        while (_commands.TryDequeue(out var command))
            command.Apply(State);

        // Системы получают время, к которому только что завершился час.
        var completed = State.Time.AddHours(1);
        State.Time = completed;
        foreach (var (system, _) in _systems)
        {
            if (system.Schedule.IsDue(completed))
                system.Run(State);
        }

        StepCompleted?.Invoke(completed);
    }

    /// <summary>Выполнить несколько часов подряд — для тестов и перемотки.</summary>
    public void Run(long hours)
    {
        for (var i = 0; i < hours; i++)
            Step();
    }
}
