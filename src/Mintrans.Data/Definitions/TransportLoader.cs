using Mintrans.Core.Ids;
using Mintrans.Data.Json;
using Mintrans.Economy;
using Mintrans.Economy.Definitions;
using Mintrans.Transport.Definitions;

namespace Mintrans.Data.Definitions;

/// <summary>Читает content/transport/vehicles.json и сверяет грузы с каталогом ресурсов.</summary>
public static class TransportLoader
{
    public static TransportCatalog Load(JsonNode root, EconomyCatalog economy)
    {
        var resources = economy.Resources.Select(r => r.Id).ToHashSet();

        var networks = root.Get("networks").Items().Select(node => new NetworkTransport(
            node.String("network"),
            node.String("depot"),
            Ids(node.Get("stopParts")),
            Ids(node.Get("stopFacilities")),
            node.Int("unitsPerCell"),
            node.Get("trains").Element.GetBoolean(),
            node.Find("stopSlots") is { } slots
                ? slots.Element.EnumerateObject().ToDictionary(p => new TypeId(p.Name), p => new JsonNode(p.Value, $"{slots.Location}.{p.Name}").AsInt())
                : null,
            node.Int("wagonsPerStopCell", 2))).ToList();

        var bodies = root.Find("bodies")?.Items().Select(node =>
        {
            var cargo = Ids(node.Get("cargo"));
            foreach (var resource in cargo.Where(r => !resources.Contains(r)))
                throw node.Get("cargo").Error($"нет ресурса «{resource}» в economy/resources.json");
            return new TruckBody(node.String("id"), node.String("title"), cargo);
        }).ToList() ?? [];

        var models = root.Get("models").Items().Select(node =>
        {
            var role = node.String("role") switch
            {
                "locomotive" => UnitRole.Locomotive,
                "wagon" => UnitRole.Wagon,
                "truck" => UnitRole.Truck,
                var other => throw node.Get("role").Error($"неизвестная роль «{other}»: нужна locomotive, wagon или truck"),
            };
            var cargo = node.Find("cargo") is { } list ? Ids(list) : [];
            foreach (var resource in cargo.Where(r => !resources.Contains(r)))
                throw node.Get("cargo").Error($"нет ресурса «{resource}» в economy/resources.json");
            return new VehicleModel(
                node.String("id"),
                node.String("title"),
                role,
                node.String("network"),
                node.Find("speed")?.AsDouble() ?? 0,
                Mass.FromTonnes(node.Find("capacity")?.AsDouble() ?? 0),
                cargo,
                node.Int("length", 1),
                node.Int("availableFromYear", 0),
                node.Int("maxWagons", 0),
                node.Find("bodies") is { } list2 ? Ids(list2) : null);
        }).ToList();

        var maintenance = root.Get("maintenance");
        var loading = root.Get("loading");
        var defaults = new RailTrafficRules();
        var rail = root.Find("railTraffic") is { } traffic
            ? new RailTrafficRules(
                traffic.Find("occupiedDelayHours")?.AsDouble() ?? defaults.OccupiedDelayHours,
                traffic.Find("reverseDelayHours")?.AsDouble() ?? defaults.ReverseDelayHours,
                traffic.Int("reverseAfterHours", defaults.ReverseAfterHours),
                traffic.Int("stuckWarningHours", defaults.StuckWarningHours),
                traffic.Int("wreckHours", defaults.WreckHours),
                traffic.Int("crossingCloseCells", defaults.CrossingCloseCells))
            : defaults;
        try
        {
            return new TransportCatalog(
                models,
                networks,
                new MaintenanceRules(maintenance.Double("wearPerCell"), maintenance.Double("serviceAt"), maintenance.Int("serviceHours")),
                new LoadingRules(loading.Int("maxWaitHours")),
                bodies,
                rail);
        }
        catch (ArgumentException e)
        {
            throw new ContentException(root.Location, e.Message, e);
        }
    }

    private static List<TypeId> Ids(JsonNode node) => node.Items().Select(i => new TypeId(i.AsString())).ToList();
}
