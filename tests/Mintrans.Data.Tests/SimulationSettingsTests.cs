using Mintrans.Data.Definitions;
using Mintrans.Data.Json;

namespace Mintrans.Data.Tests;

public class SimulationSettingsTests
{
    [Fact]
    public void Content_has_speeds_from_design()
    {
        var settings = ContentLocator.Load().Simulation;
        Assert.Equal([1, 2, 4, 8], settings.Speeds);
        Assert.True(settings.SecondsPerHourAt1x > 0);
    }

    [Fact]
    public void Invalid_settings_are_reported_with_location()
    {
        var error = Assert.Throws<ContentException>(() => SimulationSettingsLoader.Load(JsonNode.Parse(
            """{ "secondsPerHourAt1x": 0, "speeds": [1], "maxStepsPerFrame": 4 }""", "simulation.json")));
        Assert.Contains("simulation.json", error.Message);
    }
}
