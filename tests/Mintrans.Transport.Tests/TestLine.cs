using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Economy.Definitions;
using Mintrans.Economy.Runtime;
using Mintrans.Economy.Systems;
using Mintrans.Simulation;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.Transport.Systems;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Networks;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.Transport.Tests;

/// <summary>
/// Маленькая карта 64×24 с готовой инфраструктурой, построенной теми же инструментами, что у игрока:
/// шахта (2,2) с двухпутной ж/д станцией (2..5, 6..7) и завод (50,2) с однопутной (50..53, 6), линия между ними по y = 6,
/// блочные сигналы на запад в (16,6), (12,6), (9,6), депо (20,9) со съездом на линию в сторону шахты;
/// автостанция завода (54,2..5), дорога к городу (50,14) с городским пунктом на улице и автогараж (57,10).
/// </summary>
internal sealed class TestLine
{
    public static GridPos P(int x, int y) => new(x, y);
    public static Mass T(double tonnes) => Mass.FromTonnes(tonnes);
    private static ResourceAmount A(string resource, double tonnes) => new(resource, T(tonnes));

    public GameSimulation Simulation { get; }
    public WorldMap Map => Simulation.State.Map;
    public EconomyState Economy { get; }
    public TransportState Transport { get; }
    public Builder Builder { get; }
    public Facility Depot { get; }
    public Facility Garage { get; }

    public TestLine(double wearPerCell = 0.0001, double serviceAt = 0.9, int serviceHours = 10, bool signals = true)
    {
        var terrain = new TerrainCatalog([new TerrainCategory("surface", [new TypeId("grass")], "grass")]);
        var map = new WorldMap(64, 24, 0, terrain,
        [
            new NetworkDefinition("road", EdgeNetworkLayer.LayoutName),
            new NetworkDefinition("rail", TrackNetworkLayer.LayoutName),
        ]);
        var placement = SitePlacement.Basic();
        placement.Place(map, new NewSiteRequest("coal_mine", "coal_mine", "Шахта", new GridRect(2, 2, 4, 4)));
        placement.Place(map, new NewSiteRequest("iron_mine", "iron_mine", "Рудник", new GridRect(2, 14, 4, 4)));
        placement.Place(map, new NewSiteRequest("steel_plant", "steel_plant", "Завод", new GridRect(50, 2, 4, 4)));
        placement.Place(map, new NewSiteRequest("city", "city", "Город", new GridRect(50, 14, 6, 6)));
        map.Networks["road"].LayPath(NetworkPaths.Polyline([P(50, 14), P(55, 14)]));
        map.Networks["road"].LayPath(NetworkPaths.Polyline([P(50, 14), P(50, 19)]));

        Simulation = new GameSimulation(new SimulationState(map));
        Economy = EconomyModule.Install(Simulation, EconomyCatalog());
        Transport = TransportModule.Install(Simulation, TransportCatalog(wearPerCell, serviceAt, serviceHours));
        Builder = new Builder(map, ConstructionCatalog());
        Builder.AddDemolishGuard(new DelegateDemolishGuard(f => TransportModule.WhyFacilityInUse(Transport, f)));

        Built(Builder.BuildStation("rail_station", P(2, 6), P(5, 7)));
        Built(Builder.BuildStation("rail_station", P(50, 6), P(53, 6)));
        Built(Builder.BuildPath("rail", P(6, 6), P(49, 6)));
        // Второй путь станции шахты стрелкой выходит на линию.
        Built(Builder.BuildPath("rail", P(6, 7), P(6, 6)));
        Depot = Builder.BuildFacility("rail_depot", P(20, 9), P(20, 8)).Built!;
        // Протяжка от линии к депо: стрелка в (19,6) выводит из депо на запад, к шахте.
        Built(Builder.BuildPath("rail", P(18, 6), P(20, 8)));
        if (signals)
        {
            foreach (var x in new[] { 16, 12, 9 })
                Built(Builder.BuildSignal("block_signal", P(x, 6), P(x - 1, 6)));
        }

        Built(Builder.BuildStation("truck_station", P(54, 2), P(54, 5)));
        Built(Builder.BuildPath("road", P(54, 6), P(54, 13)));
        Built(Builder.BuildFacility("city_truck_stop", P(51, 14), P(53, 14)));
        Garage = Builder.BuildFacility("truck_garage", P(57, 10), P(56, 10)).Built!;
        Built(Builder.BuildPath("road", P(56, 10), P(54, 10)));
    }

    public EconomicSite Site(string key) => Economy.At(Map.Sites.GetByKey(key).Id)!;

    public StopRef Stop(string key, string network) =>
        Transport.Stops.All.First(s => s.Network == network && s.Owner == Map.Sites.GetByKey(key).Id).Ref;

    /// <summary>Поезд из локомотива и нескольких полувагонов, ещё в депо.</summary>
    public Vehicle Train(int gondolas = 3, string wagon = "gondola")
    {
        var train = Transport.Buy(Depot.Id, "loco");
        for (var i = 0; i < gondolas; i++)
            Transport.AddWagon(train.Id, wagon);
        return train;
    }

    public void RunHours(int hours) => Simulation.Run(hours);

    /// <summary>Сохранение груза с учётом груза в пути (docs/12: груз всегда ровно в одном месте).</summary>
    public void AssertConserved()
    {
        foreach (var resource in Economy.Catalog.Resources)
            Assert.Equal(Economy.Totals.Expected(resource.Id), Economy.InStock(resource.Id) + Transport.InTransit(resource.Id));
    }

    public static void Built(BuildPlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
    public static void Built(StationPlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
    public static void Built(FacilityPlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
    public static void Built(SignalPlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
    public static void Built(BridgePlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));

    public static EconomyCatalog EconomyCatalog() => new(
        [new ResourceDefinition("coal"), new ResourceDefinition("iron_ore"), new ResourceDefinition("steel")],
        [
            new EconomicSiteDefinition("coal_mine", new Dictionary<TypeId, Mass> { ["coal"] = T(1000) }, T(100),
                [new ProcessDefinition("mining", ProcessPhase.Production, [], [A("coal", 1)], 300)]),
            new EconomicSiteDefinition("iron_mine", new Dictionary<TypeId, Mass> { ["iron_ore"] = T(1000) }, T(100),
                [new ProcessDefinition("mining", ProcessPhase.Production, [], [A("iron_ore", 1)], 400)]),
            new EconomicSiteDefinition("steel_plant",
                new Dictionary<TypeId, Mass> { ["coal"] = T(1000), ["iron_ore"] = T(1500), ["steel"] = T(500) }, T(100),
                [new ProcessDefinition("smelting", ProcessPhase.Production, [A("coal", 1), A("iron_ore", 1.5)], [A("steel", 1)], 200)]),
            new EconomicSiteDefinition("city", new Dictionary<TypeId, Mass> { ["steel"] = T(1000) }, T(50),
                [new ProcessDefinition("demand", ProcessPhase.Consumption, [A("steel", 1)], [], 150)]),
        ]);

    public static TransportCatalog TransportCatalog(double wearPerCell = 0.0001, double serviceAt = 0.9, int serviceHours = 10) => new(
        [
            new VehicleModel("loco", "Локомотив", UnitRole.Locomotive, "rail", 4, Mass.Zero, [], 1),
            new VehicleModel("gondola", "Полувагон", UnitRole.Wagon, "rail", 0, T(60), [new TypeId("coal"), new TypeId("iron_ore")], 1),
            new VehicleModel("flatcar", "Платформа", UnitRole.Wagon, "rail", 0, T(50), [new TypeId("steel")], 1),
            new VehicleModel("truck", "Грузовик", UnitRole.Truck, "road", 4, T(20), [new TypeId("coal"), new TypeId("iron_ore"), new TypeId("steel")], 1),
            .. EraTests.ExtraModels(),
        ],
        [
            new NetworkTransport("rail", "rail_depot", [new TypeId("rail_station")], [], 2, true, WagonsPerStopCell: 2),
            new NetworkTransport("road", "truck_garage", [new TypeId("truck_station")], [new TypeId("city_truck_stop")], 1, false,
                new Dictionary<TypeId, int> { ["truck_station"] = 2, ["city_truck_stop"] = 2 }),
        ],
        new MaintenanceRules(wearPerCell, serviceAt, serviceHours),
        new LoadingRules(24),
        EraTests.Bodies());

    public static ConstructionCatalog ConstructionCatalog() => new(
        [
            new NetworkBuildDefinition("rail", [], [new TypeId("rail_station")]),
            new NetworkBuildDefinition("road", [new TypeId("city")], [new TypeId("truck_station")]),
        ],
        [
            new StationDefinition("rail_station", "rail", [new TypeId("coal_mine"), new TypeId("iron_mine"), new TypeId("steel_plant")], 8, 2),
            new StationDefinition("truck_station", "road", [new TypeId("coal_mine"), new TypeId("iron_mine"), new TypeId("steel_plant")], 4),
        ],
        [],
        [
            new FacilityDefinition("rail_depot", "Депо", "rail", FacilityPlacement.Depot, [], 1),
            new FacilityDefinition("truck_garage", "Гараж", "road", FacilityPlacement.Depot, [], 1),
            new FacilityDefinition("city_truck_stop", "Пункт", "road", FacilityPlacement.StopOnNetwork, [new TypeId("city")], 3),
        ],
        [new BridgeDefinition("rail_bridge", "rail", 12), new BridgeDefinition("road_bridge", "road", 12)],
        [new SignalDefinition("block_signal", "rail", SignalLogic.Block), new SignalDefinition("path_signal", "rail", SignalLogic.Path)],
        [new LevelCrossing("road", "rail")]);
}
