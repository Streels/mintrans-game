using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Economy.Definitions;
using Mintrans.Economy.Runtime;

namespace Mintrans.Game.UI;

/// <summary>Тексты об экономике объекта для интерфейса: короткий статус и подробности.</summary>
public static class EconomyText
{
    /// <summary>Одна строка под названием: работает, чего не хватает, процент спроса.</summary>
    public static string Status(EconomicSite site, DisplayNames names)
    {
        if (site.Yesterday is not { } day || day.Processes.Count == 0)
            return "ожидает первых суток";
        return string.Join(" · ", day.Processes.Select(result => Describe(site, result, names)));
    }

    /// <summary>Подробности для панели клетки: склады и итоги вчерашних суток.</summary>
    public static IEnumerable<string> Details(EconomicSite site, DisplayNames names)
    {
        yield return "Склад:";
        foreach (var resource in site.Stock.Resources)
            yield return $"  {Capital(names[resource.Value])}: {site.Stock.Amount(resource)} из {site.Stock.Capacity(resource)}";

        if (site.Yesterday is not { } day)
            yield break;
        yield return $"Вчера (день {day.Day + 1}): {Status(site, names)}";
        foreach (var line in Totals("выпущено", day.Produced, names)
                     .Concat(Totals("израсходовано", day.Consumed, names))
                     .Concat(Totals("принято", day.Received, names))
                     .Concat(Totals("отгружено", day.Shipped, names)))
            yield return line;
    }

    private static string Describe(EconomicSite site, ProcessResult result, DisplayNames names)
    {
        var process = site.Definition.Processes.First(p => p.Id == result.ProcessId);
        if (process.Phase == ProcessPhase.Consumption)
            return $"спрос выполнен на {result.Share:P0}";
        var resource = result.Resource is { } r ? names[r.Value] : "";
        return result.Limit switch
        {
            ProcessLimit.None => "работает в полную силу",
            ProcessLimit.MissingInput => $"нет: {resource}",
            ProcessLimit.OutputFull => $"склад полон: {resource}",
            _ => result.Limit.ToString(),
        } + (result.Limit != ProcessLimit.None && result.Cycles > 0 ? $" ({result.Share:P0})" : "");
    }

    private static IEnumerable<string> Totals(string verb, IReadOnlyDictionary<TypeId, Mass> totals, DisplayNames names) =>
        totals.Select(t => $"  {verb}: {names[t.Key.Value]} {t.Value}");

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text[1..];
}
