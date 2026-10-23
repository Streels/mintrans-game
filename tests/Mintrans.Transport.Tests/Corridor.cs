using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Economy.Runtime;
using Mintrans.Economy.Systems;
using Mintrans.Simulation;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.Transport.Systems;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Networks;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;
using static Mintrans.Transport.Tests.TestLine;

namespace Mintrans.Transport.Tests;

/// <summary>
/// Однопутный коридор 64×16 для сценариев сигнализации (docs/10а), построенный инструментами игрока:
/// шахта (2,2) со станцией (2..5, 6), завод (58,2) со станцией (58..61, 6), линия по y = 6 с мостом (35..37, 6)
/// над поперечной линией x = 36. Западное депо (20,9) выпускает на запад, к шахте; восточное (44,9) — тоже на запад.
/// Дорога x = 48 от автогаража (48,2) пересекает линию переездом и ведёт в город (44,10) с двумя пунктами.
/// Сигналы и разъезды каждый сценарий ставит сам.
/// </summary>
internal sealed class Corridor
{
    public GameSimulation Simulation { get; }
    public WorldMap Map => Simulation.State.Map;
    public EconomyState Economy { get; }
    public TransportState Transport { get; }
    public Builder Builder { get; }
    public Facility WestDepot { get; }
    public Facility EastDepot { get; }
    public Facility Garage { get; }

    public Corridor()
    {
        var terrain = new TerrainCatalog([new TerrainCategory("surface", [new Core.Ids.TypeId("grass")], "grass")]);
        var map = new WorldMap(64, 16, 0, terrain,
        [
            new NetworkDefinition("road", EdgeNetworkLayer.LayoutName),
            new NetworkDefinition("rail", TrackNetworkLayer.LayoutName),
        ]);
        var placement = SitePlacement.Basic();
        placement.Place(map, new NewSiteRequest("coal_mine", "coal_mine", "Шахта", new GridRect(2, 2, 4, 4)));
        placement.Place(map, new NewSiteRequest("steel_plant", "steel_plant", "Завод", new GridRect(58, 2, 4, 4)));
        placement.Place(map, new NewSiteRequest("city", "city", "Город", new GridRect(44, 10, 8, 4)));

        Simulation = new GameSimulation(new SimulationState(map));
        Economy = EconomyModule.Install(Simulation, EconomyCatalog());
        Transport = TransportModule.Install(Simulation, TransportCatalog());
        Builder = new Builder(map, ConstructionCatalog());
        Builder.AddDemolishGuard(new DelegateDemolishGuard(f => TransportModule.WhyFacilityInUse(Transport, f)));

        Built(Builder.BuildStation("rail_station", P(2, 6), P(5, 6)));
        Built(Builder.BuildStation("rail_station", P(58, 6), P(61, 6)));
        // Мост над поперечной линией: опоры ставятся на свободные клетки, путь подводится к ним сзади.
        Built(Builder.BuildBridge("rail_bridge", P(35, 6), P(37, 6)));
        Built(Builder.BuildPath("rail", P(6, 6), P(34, 6)));
        Built(Builder.BuildPath("rail", P(38, 6), P(57, 6)));
        Built(Builder.BuildPath("rail", P(36, 1), P(36, 12)));

        WestDepot = Builder.BuildFacility("rail_depot", P(20, 9), P(20, 8)).Built!;
        Built(Builder.BuildPath("rail", P(18, 6), P(20, 8)));
        EastDepot = Builder.BuildFacility("rail_depot", P(44, 9), P(44, 8)).Built!;
        Built(Builder.BuildPath("rail", P(42, 6), P(44, 8)));

        Garage = Builder.BuildFacility("truck_garage", P(48, 2), P(48, 3)).Built!;
        Built(Builder.BuildPath("road", P(48, 3), P(48, 12)));
        Built(Builder.BuildFacility("city_truck_stop", P(48, 11), P(48, 11)));
        Built(Builder.BuildFacility("city_truck_stop", P(48, 12), P(48, 12)));
    }

    public static GridPos P(int x, int y) => new(x, y);

    public StopRef Stop(string key) =>
        Transport.Stops.All.First(s => s.Network == "rail" && s.Owner == Map.Sites.GetByKey(key).Id).Ref;

    public IReadOnlyList<StopRef> CityStops => Transport.Stops.All.Where(s => s.Network == "road").Select(s => s.Ref).ToList();

    /// <summary>Локомотив с полувагонами из депо, сразу с маршрутом.</summary>
    public Vehicle Train(Facility depot, string a, string b, int gondolas = 3)
    {
        var train = Transport.Buy(depot.Id, "loco");
        for (var i = 0; i < gondolas; i++)
            Transport.AddWagon(train.Id, "gondola");
        Transport.AssignRoute(train.Id, Stop(a), Stop(b));
        return train;
    }

    public void Signal(string kind, GridPos at, GridPos toward) => Built(Builder.BuildSignal(kind, at, toward));

    /// <summary>
    /// Разъезд: второй путь y = 7 между стрелками (26,6) и (32,6). Как принято в OpenTTD, у каждого пути разъезда
    /// своё направление: односторонний маршрутный сигнал на восток в (31,6) и на запад в (27,7) —
    /// навстречу им ехать нельзя, поэтому поезда сами расходятся по разным путям.
    /// </summary>
    public void PassingLoop()
    {
        Built(Builder.BuildPath("rail", P(27, 7), P(31, 7)));
        Built(Builder.BuildPath("rail", P(24, 6), P(26, 7)));
        Built(Builder.BuildPath("rail", P(34, 6), P(32, 7)));
        Signal("path_signal", P(31, 6), P(32, 6));
        Signal("path_signal", P(27, 7), P(26, 7));
    }

    /// <summary>Крутить часы, пока условие не выполнится; не дольше <paramref name="limit"/> часов. Вызывает <paramref name="each"/> каждый час.</summary>
    public bool RunUntil(Func<bool> done, int limit = 24 * 14, Action? each = null)
    {
        for (var hour = 0; hour < limit && !done(); hour++)
        {
            Simulation.Run(1);
            each?.Invoke();
        }
        return done();
    }
}
