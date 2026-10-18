using Godot;
using Mintrans.Data.Json;

namespace Mintrans.Game.UI;

/// <summary>
/// Человеческие названия для идентификаторов из данных ("iron_ore" → «железная руда»).
/// Файл game/styles/names.ru.json; для другого языка достаточно другого файла.
/// Если названия нет, показывается сам идентификатор.
/// </summary>
public sealed class DisplayNames
{
    private readonly Dictionary<string, string> _names;

    private DisplayNames(Dictionary<string, string> names) => _names = names;

    public string this[string id] => _names.TryGetValue(id, out var name) ? name : id;

    public static DisplayNames Load(string path)
    {
        var json = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(json))
        {
            GD.PushWarning($"Нет файла названий {path}, показываю идентификаторы.");
            return new DisplayNames(new Dictionary<string, string>());
        }
        var root = JsonNode.Parse(json, path);
        return new DisplayNames(root.Element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name));
    }
}
