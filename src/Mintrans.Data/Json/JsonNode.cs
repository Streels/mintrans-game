using System.Text.Json;
using Mintrans.Core.Grid;

namespace Mintrans.Data.Json;

/// <summary>
/// Обёртка над элементом JSON, которая помнит своё место в файле.
/// Любая ошибка формата сообщает, например: "maps/prototype.json → terrain[3].radius: ожидалось [x, y]".
/// </summary>
public readonly struct JsonNode
{
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public JsonNode(JsonElement element, string location)
    {
        Element = element;
        Location = location;
    }

    public JsonElement Element { get; }
    public string Location { get; }

    public static JsonNode Parse(string json, string location)
    {
        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            return new JsonNode(document.RootElement.Clone(), location);
        }
        catch (JsonException e)
        {
            throw new ContentException(location, $"некорректный JSON: {e.Message}", e);
        }
    }

    public static JsonNode ParseFile(string path, string location) => Parse(File.ReadAllText(path), location);

    public ContentException Error(string message) => new(Location, message);

    public bool Has(string name) => Element.ValueKind == JsonValueKind.Object && Element.TryGetProperty(name, out _);

    public JsonNode Get(string name)
    {
        if (Element.ValueKind != JsonValueKind.Object)
            throw Error("ожидался объект { ... }");
        if (!Element.TryGetProperty(name, out var value))
            throw Error($"нет обязательного поля «{name}»");
        return new JsonNode(value, $"{Location}.{name}");
    }

    public JsonNode? Find(string name) =>
        Element.ValueKind == JsonValueKind.Object && Element.TryGetProperty(name, out var value)
            ? new JsonNode(value, $"{Location}.{name}")
            : null;

    public IEnumerable<JsonNode> Items()
    {
        if (Element.ValueKind != JsonValueKind.Array)
            throw Error("ожидался список [ ... ]");
        var i = 0;
        foreach (var item in Element.EnumerateArray())
            yield return new JsonNode(item, $"{Location}[{i++}]");
    }

    public string AsString() =>
        Element.ValueKind == JsonValueKind.String ? Element.GetString()! : throw Error("ожидалась строка");

    public int AsInt() =>
        Element.ValueKind == JsonValueKind.Number && Element.TryGetInt32(out var value)
            ? value
            : throw Error("ожидалось целое число");

    public double AsDouble() =>
        Element.ValueKind == JsonValueKind.Number ? Element.GetDouble() : throw Error("ожидалось число");

    public ulong AsULong() =>
        Element.ValueKind == JsonValueKind.Number && Element.TryGetUInt64(out var value)
            ? value
            : throw Error("ожидалось неотрицательное целое число");

    /// <summary>Пара чисел [x, y].</summary>
    public (int A, int B) AsPair()
    {
        var items = Items().ToList();
        if (items.Count != 2)
            throw Error("ожидалась пара [x, y]");
        return (items[0].AsInt(), items[1].AsInt());
    }

    public GridPos AsPos()
    {
        var (x, y) = AsPair();
        return new GridPos(x, y);
    }

    /// <summary>Прямоугольник [x, y, ширина, высота].</summary>
    public GridRect AsRect()
    {
        var items = Items().ToList();
        if (items.Count != 4)
            throw Error("ожидался прямоугольник [x, y, ширина, высота]");
        var (w, h) = (items[2].AsInt(), items[3].AsInt());
        if (w <= 0 || h <= 0)
            throw Error($"размер прямоугольника должен быть положительным: {w}x{h}");
        return new GridRect(items[0].AsInt(), items[1].AsInt(), w, h);
    }

    public string String(string name) => Get(name).AsString();
    public int Int(string name) => Get(name).AsInt();
    public int Int(string name, int fallback) => Find(name)?.AsInt() ?? fallback;
    public double Double(string name) => Get(name).AsDouble();
    public GridPos Pos(string name) => Get(name).AsPos();
    public GridRect Rect(string name) => Get(name).AsRect();
}
