using Mintrans.Data.Json;
using Mintrans.Simulation.Time;

namespace Mintrans.Data.Definitions;

/// <summary>Читает content/simulation.json — темп игры и набор скоростей.</summary>
public static class SimulationSettingsLoader
{
    private static DateTime StartDate(JsonNode root)
    {
        if (root.Find("startDate") is not { } node)
            return new DateTime(1950, 1, 1);
        return DateTime.TryParseExact(node.AsString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : throw node.Error("дата должна быть в виде ГГГГ-ММ-ДД");
    }

    public static SimulationSettings Load(JsonNode root)
    {
        var settings = new SimulationSettings(
            root.Double("secondsPerHourAt1x"),
            root.Get("speeds").Items().Select(s => s.AsInt()).ToList(),
            root.Int("maxStepsPerFrame"),
            StartDate(root));
        try
        {
            settings.Validate();
        }
        catch (ArgumentException e)
        {
            throw new ContentException(root.Location, e.Message, e);
        }
        return settings;
    }
}
