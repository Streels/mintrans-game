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
            var maxTracks = node.Int("maxTracks", 1);
            if (maxTracks <= 0)
                throw node.Get("maxTracks").Error("число путей станции должно быть положительным");
            return new StationDefinition(node.String("id"), Network(node.Get("network")), Ids(node.Get("attachTo")), maxLength, maxTracks);
        }).ToList();

        var withoutRules = stations.Select(s => s.Network).Except(builds.Select(b => b.Network)).ToList();
        if (withoutRules.Count > 0)
            throw root.Get("networks").Error($"нет правил прокладки для сети «{withoutRules[0]}», которую использует станция");

        var forbidden = root.Find("forbiddenTerrain")?.Items()
            .Select(node => new ForbiddenTerrain(node.String("category"), Ids(node.Get("kinds"))))
            .ToList() ?? [];

        var facilities = root.Find("facilities")?.Items().Select(node =>
        {
            var placement = node.String("placement") switch
            {
                "depot" => FacilityPlacement.Depot,
                "stopOnNetwork" => FacilityPlacement.StopOnNetwork,
                var other => throw node.Get("placement").Error($"неизвестное размещение «{other}»: нужно depot или stopOnNetwork"),
            };
            return new FacilityDefinition(
                node.String("id"),
                node.String("title"),
                Network(node.Get("network")),
                placement,
                node.Find("siteTypes") is { } types ? Ids(types) : [],
                node.Int("maxLength", 1));
        }).ToList() ?? [];

        var bridges = root.Find("bridges")?.Items().Select(node =>
        {
            var maxLength = node.Int("maxLength");
            if (maxLength < 3)
                throw node.Get("maxLength").Error("мост — хотя бы три клетки: две опоры и пролёт");
            return new BridgeDefinition(node.String("id"), Network(node.Get("network")), maxLength);
        }).ToList() ?? [];

        var layouts = networks.ToDictionary(n => n.Id, n => n.Layout);
        var signals = root.Find("signals")?.Items().Select(node =>
        {
            var network = Network(node.Get("network"));
            if (layouts[network] != TrackNetworkLayer.LayoutName)
                throw node.Get("network").Error($"сигналы ставятся только на рельсовую сеть (layout «{TrackNetworkLayer.LayoutName}»)");
            var logic = node.String("logic") switch
            {
                "block" => SignalLogic.Block,
                "path" => SignalLogic.Path,
                var other => throw node.Get("logic").Error($"неизвестная логика сигнала «{other}»: нужно block или path"),
            };
            return new SignalDefinition(node.String("id"), network, logic);
        }).ToList() ?? [];

        var crossings = root.Find("levelCrossings")?.Items().Select(node =>
        {
            var pair = node.Items().ToList();
            if (pair.Count != 2)
                throw node.Error("переезд задаётся парой сетей [\"road\", \"rail\"]");
            return new LevelCrossing(Network(pair[0]), Network(pair[1]));
        }).ToList() ?? [];

        return new ConstructionCatalog(builds, stations, forbidden, facilities, bridges, signals, crossings);
    }

    private static List<TypeId> Ids(JsonNode node) => node.Items().Select(i => new TypeId(i.AsString())).ToList();
}
