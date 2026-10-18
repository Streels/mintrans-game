namespace Mintrans.Simulation.Commands;

/// <summary>
/// Действие игрока: построить путь, поставить станцию, запустить маршрут.
/// Команды не меняют мир сразу: они копятся в очереди и применяются в начале следующего шага
/// (docs/03: «изменённая игроком связь начинает действовать со следующего шага»).
/// Так результат не зависит от того, в какой момент между шагами игрок нажал кнопку.
/// </summary>
public interface IGameCommand
{
    /// <summary>Короткое описание для журнала.</summary>
    string Description { get; }

    void Apply(SimulationState state);
}

/// <summary>Команда из лямбды — для простых действий и тестов.</summary>
public sealed class DelegateCommand(string description, Action<SimulationState> apply) : IGameCommand
{
    public string Description { get; } = description;

    public void Apply(SimulationState state) => apply(state);
}
