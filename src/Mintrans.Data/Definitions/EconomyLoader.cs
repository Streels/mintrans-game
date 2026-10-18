using System.Text.Json;
using Mintrans.Core.Ids;
using Mintrans.Data.Json;
using Mintrans.Data.Maps;
using Mintrans.Economy;
using Mintrans.Economy.Definitions;
using Mintrans.Economy.Runtime;

namespace Mintrans.Data.Definitions;

/// <summary>Читает content/economy/*.json и задаёт начальные запасы сценария.</summary>
public static class EconomyLoader
{
    public static EconomyCatalog Load(JsonNode resourcesRoot, JsonNode sitesRoot)
    {
        var resources = resourcesRoot.Get("resources").Items()
            .Select(node => new ResourceDefinition(node.String("id")))
            .ToList();

        var sites = sitesRoot.Get("sites").Items().Select(node => new EconomicSiteDefinition(
            node.String("type"),
            Amounts(node.Get("storage")).ToDictionary(a => a.Resource, a => a.Amount),
            Mass.FromTonnes(node.Double("handlingPerHour")),
            node.Get("processes").Items().Select(Process).ToList())).ToList();

        try
        {
            return new EconomyCatalog(resources, sites);
        }
        catch (ArgumentException e)
        {
            throw new ContentException(sitesRoot.Location, e.Message, e);
        }
    }

    /// <summary>Положить на склады начальные запасы из файла карты ("stock" у объектов).</summary>
    public static void SeedStocks(EconomyState economy, MapDescription map)
    {
        foreach (var (key, node) in map.SiteData)
        {
            if (node.Find("stock") is not { } stock)
                continue;
            var site = map.Map.Sites.GetByKey(key);
            foreach (var amount in Amounts(stock))
            {
                try
                {
                    economy.Seed(site.Id, amount.Resource, amount.Amount);
                }
                catch (ArgumentException e)
                {
                    throw new ContentException(stock.Location, $"«{key}»: {e.Message}", e);
                }
            }
        }
    }

    private static ProcessDefinition Process(JsonNode node)
    {
        var phase = node.String("phase") switch
        {
            "production" => ProcessPhase.Production,
            "consumption" => ProcessPhase.Consumption,
            var other => throw node.Get("phase").Error($"неизвестная фаза «{other}»: нужна production или consumption"),
        };
        var cycles = node.Int("cyclesPerDay");
        if (cycles < 0)
            throw node.Get("cyclesPerDay").Error("число циклов не может быть отрицательным");
        return new ProcessDefinition(node.String("id"), phase, Amounts(node.Get("inputs")), Amounts(node.Get("outputs")), cycles);
    }

    /// <summary>Объект { "ресурс": тонны, ... } → список количеств.</summary>
    internal static List<ResourceAmount> Amounts(JsonNode node)
    {
        if (node.Element.ValueKind != JsonValueKind.Object)
            throw node.Error("ожидался объект { \"ресурс\": тонны }");
        return node.Element.EnumerateObject().Select(p =>
        {
            var value = new JsonNode(p.Value, $"{node.Location}.{p.Name}");
            var tonnes = value.AsDouble();
            if (tonnes < 0)
                throw value.Error("количество не может быть отрицательным");
            return new ResourceAmount(p.Name, Mass.FromTonnes(tonnes));
        }).ToList();
    }
}
