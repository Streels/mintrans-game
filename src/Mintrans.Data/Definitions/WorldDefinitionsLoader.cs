using Mintrans.Core.Ids;
using Mintrans.Data.Json;
using Mintrans.World.Networks;
using Mintrans.World.Terrain;

namespace Mintrans.Data.Definitions;

/// <summary>Читает определения мира: каталог природных категорий и типы транспортных сетей.</summary>
public static class WorldDefinitionsLoader
{
    /// <summary>Формат content/world/terrain.json.</summary>
    public static TerrainCatalog LoadTerrain(JsonNode root)
    {
        var categories = root.Get("categories").Items().Select(node =>
        {
            var id = new TypeId(node.String("id"));
            var kinds = node.Get("kinds").Items().Select(kind => new TypeId(kind.String("id"))).ToList();
            var defaultKind = new TypeId(node.String("default"));
            if (!kinds.Contains(defaultKind))
                throw node.Error($"вид по умолчанию «{defaultKind}» не объявлен в kinds");
            try
            {
                return new TerrainCategory(id, kinds, defaultKind);
            }
            catch (ArgumentException e)
            {
                throw new ContentException(node.Location, e.Message, e);
            }
        }).ToList();

        try
        {
            return new TerrainCatalog(categories);
        }
        catch (ArgumentException e)
        {
            throw new ContentException(root.Location, e.Message, e);
        }
    }

    /// <summary>Формат content/world/networks.json.</summary>
    public static IReadOnlyList<NetworkDefinition> LoadNetworks(JsonNode root)
    {
        var definitions = root.Get("networks").Items().Select(node =>
        {
            var layout = node.String("layout");
            if (!NetworkLayouts.Exists(layout))
                throw node.Get("layout").Error(
                    $"неизвестная раскладка «{layout}». Доступны: {string.Join(", ", NetworkLayouts.Names)}");
            return new NetworkDefinition(node.String("id"), layout);
        }).ToList();
        var duplicate = definitions.GroupBy(d => d.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw root.Error($"сеть «{duplicate.Key}» объявлена дважды");
        return definitions;
    }
}
