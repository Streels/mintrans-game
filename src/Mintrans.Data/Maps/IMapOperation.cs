using Mintrans.Data.Json;
using Mintrans.World;

namespace Mintrans.Data.Maps;

/// <summary>
/// Один шаг построения карты из файла: { "op": "hill", ... }.
/// Чтобы добавить новый вид шага, достаточно написать класс и зарегистрировать его в <see cref="MapOperations"/>.
/// </summary>
public interface IMapOperation
{
    /// <summary>Значение поля "op" в JSON.</summary>
    string Name { get; }

    void Apply(JsonNode args, WorldMap map);
}

/// <summary>Набор известных операций для одного раздела файла карты.</summary>
public sealed class MapOperations
{
    private readonly Dictionary<string, IMapOperation> _byName = new();

    public MapOperations(IEnumerable<IMapOperation> operations)
    {
        foreach (var operation in operations)
            Register(operation);
    }

    public IReadOnlyCollection<string> Names => _byName.Keys;

    public void Register(IMapOperation operation)
    {
        if (!_byName.TryAdd(operation.Name, operation))
            throw new ArgumentException($"Операция «{operation.Name}» уже зарегистрирована.");
    }

    public void Apply(JsonNode step, WorldMap map)
    {
        var name = step.String("op");
        if (!_byName.TryGetValue(name, out var operation))
            throw step.Error($"неизвестная операция «{name}». Доступны: {string.Join(", ", _byName.Keys.Order())}");
        try
        {
            operation.Apply(step, map);
        }
        catch (ContentException)
        {
            throw;
        }
        catch (Exception e) when (e is ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ContentException(step.Location, e.Message, e);
        }
    }

    /// <summary>Операции раздела "terrain".</summary>
    public static MapOperations DefaultTerrain() => new(
    [
        new Terrain.FillOperation(),
        new Terrain.RectOperation(),
        new Terrain.EllipseOperation(),
        new Terrain.PathOperation(),
        new Terrain.HillOperation(),
        new Terrain.LevelOperation(),
    ]);

    /// <summary>Операции раздела "networks".</summary>
    public static MapOperations DefaultNetworks() => new(
    [
        new Networks.LineOperation(),
        new Networks.GridOperation(),
        new Networks.BridgeOperation(),
        new Networks.SignalOperation(),
    ]);
}
