using Mintrans.Construction;
using Mintrans.Core.Ids;
using Mintrans.Data.Json;
using Mintrans.World.Networks;

namespace Mintrans.Data.Definitions;

/// <summary>Читает content/construction.json и сверяет его с объявленными сетями.</summary>
public static class ConstructionLoader
{
    public static ConstructionCatalog Load(JsonNode root, IReadOnlyList<NetworkDefinition> networks)
    {
        var known = networks.Select(n => n.Id).ToHashSet();
        TypeId Network(JsonNode node)
        {
            var id = new TypeId(node.AsString());
            return known.Contains(id) ? id : throw node.Error($"нет сети «{id}» в world/networks.json");
        }

        var builds = root.Get("networks").Items().Select(node => new NetworkBuildDefinition(
            Network(node.Get("network")),
            Ids(node.Get("siteTypes")),
            Ids(node.Get("parts")))).ToList();

        var stations = root.Get("stations").Items().Select(node =>
        {
            var maxLength = node.Int("maxLength");
            if (maxLength <= 0)
                throw node.Get("maxLength").Error("длина станции должна быть положительной");
            return new StationDefinition(node.String("id"), Network(node.Get("network")), Ids(node.Get("attachTo")), maxLength);
        }).ToList();

        var withoutRules = stations.Select(s => s.Network).Except(builds.Select(b => b.Network)).ToList();
        if (withoutRules.Count > 0)
            throw root.Get("networks").Error($"нет правил прокладки для сети «{withoutRules[0]}», которую использует станция");

        var forbidden = root.Find("forbiddenTerrain")?.Items()
            .Select(node => new ForbiddenTerrain(node.String("category"), Ids(node.Get("kinds"))))
            .ToList() ?? [];

        return new ConstructionCatalog(builds, stations, forbidden);
    }

    private static List<TypeId> Ids(JsonNode node) => node.Items().Select(i => new TypeId(i.AsString())).ToList();
}
