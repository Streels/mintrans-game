using Mintrans.Data.Json;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.Data.Maps;

/// <summary>
/// Строит <see cref="WorldMap"/> из файла карты (content/maps/*.json):
/// размер и семя → шаги "terrain" → шаги "networks" → объекты "sites".
/// Шаги выполняются строго по порядку, поэтому результат воспроизводим.
/// </summary>
public sealed class MapLoader
{
    public const int SupportedFormat = 1;

    private readonly TerrainCatalog _terrain;
    private readonly IReadOnlyList<NetworkDefinition> _networks;
    private readonly MapOperations _terrainOperations;
    private readonly MapOperations _networkOperations;
    private readonly SitePlacement _placement;

    public MapLoader(
        TerrainCatalog terrain,
        IReadOnlyList<NetworkDefinition> networks,
        MapOperations? terrainOperations = null,
        MapOperations? networkOperations = null,
        SitePlacement? placement = null)
    {
        _terrain = terrain;
        _networks = networks;
        _terrainOperations = terrainOperations ?? MapOperations.DefaultTerrain();
        _networkOperations = networkOperations ?? MapOperations.DefaultNetworks();
        _placement = placement ?? SitePlacement.Basic();
    }

    public MapDescription Load(JsonNode root)
    {
        var format = root.Int("format");
        if (format != SupportedFormat)
            throw root.Get("format").Error($"формат карты {format} не поддерживается, ожидается {SupportedFormat}");

        var (width, height) = root.Get("size").AsPair();
        if (width <= 0 || height <= 0)
            throw root.Get("size").Error($"размер карты должен быть положительным: {width}x{height}");
        var seed = root.Find("seed")?.AsULong() ?? 0;

        var map = new WorldMap(width, height, seed, _terrain, _networks);

        foreach (var step in root.Find("terrain")?.Items() ?? [])
            _terrainOperations.Apply(step, map);
        foreach (var step in root.Find("networks")?.Items() ?? [])
            _networkOperations.Apply(step, map);
        var siteData = new Dictionary<string, JsonNode>();
        foreach (var node in root.Find("sites")?.Items() ?? [])
        {
            PlaceSite(node, map);
            siteData[node.String("key")] = node;
        }

        return new MapDescription(root.String("id"), root.String("name"), map, siteData);
    }

    /// <summary>
    /// { "key", "type", "name", "rect": базовая область, "parts": [ { "kind", "rect" }, ... ] } —
    /// части присоединяются по порядку и должны примыкать к уже занятой площадке.
    /// </summary>
    private void PlaceSite(JsonNode node, WorldMap map)
    {
        var key = node.String("key");
        if (map.Sites.TryGetByKey(key, out _))
            throw node.Get("key").Error($"объект «{key}» уже объявлен");
        Try(node, () => _placement.Place(map, new NewSiteRequest(key, node.String("type"), node.String("name"), node.Rect("rect"))));
        foreach (var part in node.Find("parts")?.Items() ?? [])
            Try(part, () => _placement.Attach(map, new AttachPartRequest(key, part.String("kind"), part.Rect("rect"))));
    }

    private static void Try(JsonNode node, Action action)
    {
        try
        {
            action();
        }
        catch (SitePlacementException e)
        {
            throw new ContentException(node.Location, e.Message, e);
        }
    }
}

/// <summary>
/// Загруженная карта и её описание. <paramref name="SiteData"/> — исходные записи объектов из файла:
/// другие модули читают из них свои поля (например, экономика — начальные запасы "stock"),
/// и загрузчику карты не нужно о них знать.
/// </summary>
public sealed record MapDescription(string Id, string Name, WorldMap Map, IReadOnlyDictionary<string, JsonNode> SiteData);
