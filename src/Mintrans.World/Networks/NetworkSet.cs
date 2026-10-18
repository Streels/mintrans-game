using Mintrans.Core.Ids;

namespace Mintrans.World.Networks;

/// <summary>Описание типа сети из content/world/networks.json.</summary>
/// <param name="Layout">Как сеть устроена внутри клетки: "edges" (дороги) или "tracks" (рельсы).</param>
public sealed record NetworkDefinition(TypeId Id, string Layout);

/// <summary>
/// Известные раскладки сетей. Новая раскладка (например, для водных путей) регистрируется здесь,
/// остальной код с ней сразу работает через <see cref="NetworkLayer"/>.
/// </summary>
public static class NetworkLayouts
{
    private static readonly Dictionary<string, Func<TypeId, int, int, NetworkLayer>> Factories = new()
    {
        [EdgeNetworkLayer.LayoutName] = (type, w, h) => new EdgeNetworkLayer(type, w, h),
        [TrackNetworkLayer.LayoutName] = (type, w, h) => new TrackNetworkLayer(type, w, h),
    };

    public static IReadOnlyCollection<string> Names => Factories.Keys;

    public static bool Exists(string layout) => Factories.ContainsKey(layout);

    public static NetworkLayer Create(NetworkDefinition definition, int width, int height) =>
        Factories.TryGetValue(definition.Layout, out var factory)
            ? factory(definition.Id, width, height)
            : throw new ArgumentException(
                $"Неизвестная раскладка сети «{definition.Layout}». Доступны: {string.Join(", ", Factories.Keys)}");
}

/// <summary>Все транспортные сети карты, по одной на каждый тип.</summary>
public sealed class NetworkSet
{
    private readonly Dictionary<TypeId, NetworkLayer> _layers = new();

    public NetworkSet(IEnumerable<NetworkDefinition> definitions, int width, int height)
    {
        foreach (var definition in definitions)
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
