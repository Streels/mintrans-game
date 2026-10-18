using Mintrans.Core.Ids;
using Mintrans.Simulation.Systems;
using Mintrans.World;
using Mintrans.World.Networks;
using Mintrans.World.Terrain;

namespace Mintrans.Simulation.Tests;

/// <summary>Пустой мир и система-заглушка, которая записывает свои вызовы в общий журнал.</summary>
internal static class TestSimulation
{
    public static GameSimulation Create()
    {
        var terrain = new TerrainCatalog([new TerrainCategory("surface", [new TypeId("grass")], "grass")]);
        var map = new WorldMap(8, 8, 0, terrain, [new NetworkDefinition("road", EdgeNetworkLayer.LayoutName)]);
        return new GameSimulation(new SimulationState(map));
    }
}

internal sealed class RecordingSystem(string name, Schedule schedule, int phase, List<string> log) : ISimulationSystem
{
    public string Name { get; } = name;
    public Schedule Schedule { get; } = schedule;
    public int Phase { get; } = phase;
    public int Calls { get; private set; }

    public void Run(SimulationState state)
    {
        Calls++;
        log.Add($"{state.Time.Hours}:{Name}");
    }
}
