using Mintrans.Simulation.Time;
using Mintrans.World;

namespace Mintrans.Simulation;

/// <summary>
/// Всё, что меняется по ходу игры. Сейчас это карта и время; модули экономики и транспорта
/// будут хранить свои данные в собственных классах и получать доступ к ним через <see cref="Get{T}"/>.
/// </summary>
public sealed class SimulationState
{
    private readonly Dictionary<Type, object> _modules = new();

    public SimulationState(WorldMap map, GameTime start = default, GameCalendar? calendar = null)
    {
        Map = map;
        Time = start;
        Calendar = calendar ?? GameCalendar.Default;
    }

    public WorldMap Map { get; }

    /// <summary>Календарь кампании: дата старта и перевод часов в даты (docs/14).</summary>
    public GameCalendar Calendar { get; }

    /// <summary>Сообщения игроку: новые модели техники, события республики.</summary>
    public GameLog Log { get; } = new();

    /// <summary>Текущий игровой год.</summary>
    public int Year => Calendar.Year(Time);

    /// <summary>Время к началу текущего шага; после шага увеличивается на час.</summary>
    public GameTime Time { get; internal set; }

    /// <summary>Подключить данные модуля (например, склады предприятий). Один экземпляр на тип.</summary>
    public void Add<T>(T module) where T : class
    {
        if (!_modules.TryAdd(typeof(T), module))
            throw new InvalidOperationException($"Данные модуля {typeof(T).Name} уже подключены.");
    }

    public T Get<T>() where T : class =>
        _modules.TryGetValue(typeof(T), out var module)
            ? (T)module
            : throw new KeyNotFoundException($"Данные модуля {typeof(T).Name} не подключены.");

    public bool TryGet<T>(out T module) where T : class
    {
        var found = _modules.TryGetValue(typeof(T), out var value);
        module = found ? (T)value! : null!;
        return found;
    }
}
