namespace Mintrans.Data.Json;

/// <summary>Ошибка в файле данных: где именно (файл и путь внутри JSON) и что не так.</summary>
public sealed class ContentException(string location, string message, Exception? inner = null)
    : Exception($"{location}: {message}", inner)
{
    public string Location { get; } = location;
}
