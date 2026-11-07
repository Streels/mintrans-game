using Mintrans.Core.Ids;

namespace Mintrans.World.Networks;

/// <summary>Описание типа сети из content/world/networks.json.</summary>
/// <param name="Layout">
/// Как устроена сеть: "edges" — по сторонам клеток (дороги); "graph" — граф путей на метрической геометрии (рельсы, docs/10в).
/// </param>
public sealed record NetworkDefinition(TypeId Id, string Layout)
{
    /// <summary>Раскладка рельсового графа: сеть живёт не в клетках, а в <see cref="Rail.RailNetwork"/>.</summary>
    public const string GraphLayout = "graph";

    public bool IsGraph => Layout == GraphLayout;
}

/// <summary>
/// Известные раскладки сетей. Новая раскладка (например, для водных путей) регистрируется здесь,
/// остальной код с ней сразу работает через <see cref="NetworkLayer"/>.
/// </summary>
public static class NetworkLayouts
{
    private static readonly Dictionary<string, Func<TypeId, int, int, NetworkLayer>> Factories = new()
    {
        [EdgeNetworkLayer.LayoutName] = (type, w, h) => new EdgeNetworkLayer(type, w, h),
    };

    public static IReadOnlyCollection<string> Names => [.. Factories.Keys, NetworkDefinition.GraphLayout];

    public static bool Exists(string layout) => Factories.ContainsKey(layout) || layout == NetworkDefinition.GraphLayout;

    public static NetworkLayer Create(NetworkDefinition definition, int width, int height) =>
        Factories.TryGetValue(definition.Layout, out var factory)
            ? factory(definition.Id, width, height)
            : throw new ArgumentException(
                $"Неизвестная раскладка сети «{definition.Layout}». Доступны: {string.Join(", ", Factories.Keys)}");
}

/// <summary>Клеточные транспортные сети карты (дороги), по одной на каждый тип. Рельсовый граф — <see cref="WorldMap.Rails"/>.</summary>
public sealed class NetworkSet
{
    private readonly Dictionary<TypeId, NetworkLayer> _layers = new();

    public NetworkSet(IEnumerable<NetworkDefinition> definitions, int width, int height)
    {
        foreach (var definition in definitions.Where(d => !d.IsGraph))
        {
            if (!_layers.TryAdd(definition.Id, NetworkLayouts.Create(definition, width, height)))
                throw new ArgumentException($"Сеть «{definition.Id}» объявлена дважды.");
        }
    }

    public IEnumerable<NetworkLayer> Layers => _layers.Values;

    public bool TryGet(TypeId type, out NetworkLayer layer) => _layers.TryGetValue(type, out layer!);

    public NetworkLayer this[TypeId type] =>
        TryGet(type, out var layer) ? layer : throw new KeyNotFoundException($"Нет сети «{type}».");
}
