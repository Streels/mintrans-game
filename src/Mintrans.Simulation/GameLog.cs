using Mintrans.Simulation.Time;

namespace Mintrans.Simulation;

/// <summary>Одно сообщение игроку с моментом, когда оно появилось.</summary>
public sealed record GameMessage(GameTime Time, string Text);

/// <summary>
/// Журнал сообщений игроку. Модули пишут сюда события («Доступна новая техника: ТЭ3»),
/// интерфейс показывает их, не зная, какой модуль их прислал.
/// </summary>
public sealed class GameLog
{
    private const int Capacity = 200;
    private readonly List<GameMessage> _messages = new();

    public IReadOnlyList<GameMessage> Messages => _messages;

    /// <summary>Пришло новое сообщение.</summary>
    public event Action<GameMessage>? Posted;

    public void Post(GameTime time, string text)
    {
        var message = new GameMessage(time, text);
        _messages.Add(message);
        if (_messages.Count > Capacity)
            _messages.RemoveAt(0);
        Posted?.Invoke(message);
    }
}
