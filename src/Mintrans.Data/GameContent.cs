using Mintrans.Construction;
using Mintrans.Data.Definitions;
using Mintrans.Data.Json;
using Mintrans.Data.Maps;
using Mintrans.Simulation.Time;
using Mintrans.World.Networks;
using Mintrans.World.Terrain;

namespace Mintrans.Data;

/// <summary>
/// Точка входа в данные игры: папка content/ со всеми определениями и картами.
/// Ядро и отображение получают данные только отсюда, а не читают файлы сами.
/// </summary>
public sealed class GameContent
{
    private GameContent(
        string root,
        TerrainCatalog terrain,
        IReadOnlyList<NetworkDefinition> networks,
        SimulationSettings simulation,
        ConstructionCatalog construction)
    {
        Root = root;
        Terrain = terrain;
        Networks = networks;
        Simulation = simulation;
        Construction = construction;
    }

    public string Root { get; }
    public TerrainCatalog Terrain { get; }
    public IReadOnlyList<NetworkDefinition> Networks { get; }
    public SimulationSettings Simulation { get; }
    public ConstructionCatalog Construction { get; }

    public static GameContent Load(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Нет папки данных игры: {Path.GetFullPath(root)}");
        var terrain = WorldDefinitionsLoader.LoadTerrain(Read(root, "world/terrain.json"));
        var networks = WorldDefinitionsLoader.LoadNetworks(Read(root, "world/networks.json"));
        var simulation = SimulationSettingsLoader.Load(Read(root, "simulation.json"));
        var construction = ConstructionLoader.Load(Read(root, "construction.json"), networks);
        return new GameContent(root, terrain, networks, simulation, construction);
    }

    /// <summary>Загрузить карту по имени файла без расширения: "prototype" → content/maps/prototype.json.</summary>
    public MapDescription LoadMap(string mapId) =>
        new MapLoader(Terrain, Networks).Load(Read(Root, $"maps/{mapId}.json"));

    public IEnumerable<string> MapIds() =>
        Directory.EnumerateFiles(Path.Combine(Root, "maps"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order();

    private static JsonNode Read(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        if (!File.Exists(path))
            throw new ContentException(relative, $"файл не найден ({Path.GetFullPath(path)})");
        return JsonNode.ParseFile(path, relative);
    }
}
