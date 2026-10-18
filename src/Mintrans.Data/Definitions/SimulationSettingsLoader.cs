using Mintrans.Data.Json;
using Mintrans.Simulation.Time;

namespace Mintrans.Data.Definitions;

/// <summary>Читает content/simulation.json — темп игры и набор скоростей.</summary>
public static class SimulationSettingsLoader
{
    public static SimulationSettings Load(JsonNode root)
    {
        var settings = new SimulationSettings(
            root.Double("secondsPerHourAt1x"),
            root.Get("speeds").Items().Select(s => s.AsInt()).ToList(),
            root.Int("maxStepsPerFrame"));
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
