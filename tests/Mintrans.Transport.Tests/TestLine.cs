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
using Mintrans.World.Rail;
using Mintrans.Construction.Rail;
using Mintrans.Core.Spatial;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.Transport.Tests;

/// <summary>
/// Карта 140×40 клеток (700 × 200 м) с готовой инфраструктурой, построенной теми же инструментами, что у игрока.
/// Главный путь по y = 20 от x = 2 до x = 132. Шахта (26..31, 12..17) со станцией на два пути: платформа на главном пути
/// (27..30, 20) и на втором пути y = 21 (27..30); второй путь S-образными кривыми выходит на главный стрелками у x = 10
/// и x = 50. Депо (3..6, 20) — в тупике главного пути за западной стрелкой: выехав на восток, поезд попадает на любой
/// путь станции. Завод (120..125, 12..17) с платформой (120..123, 20). Блочные сигналы на запад в x = 74, 64, 54.
/// Автостанция завода (126, 12..15), дорога к городу (118..131, 30..37) с городским пунктом на улице и автогараж (128, 24).
/// </summary>
internal sealed class TestLine
{
    public static GridPos P(int x, int y) => new(x, y);

    /// <summary>Центр клетки в метрах.</summary>
    public static WorldPoint C(int x, int y) => SpatialScale.CellCenter(new GridPos(x, y));

    public static Mass T(double tonnes) => Mass.FromTonnes(tonnes);
    private static ResourceAmount A(string resource, double tonnes) => new(resource, T(tonnes));

    public GameSimulation Simulation { get; }
    public WorldMap Map => Simulation.State.Map;
    public RailNetwork Rails => Map.RequireRails();
    public EconomyState Economy { get; }
    public TransportState Transport { get; }
    public Builder Builder { get; }
    public Facility Depot { get; }
    public Facility Garage { get; }

    /// <param name="mineHandlingTonnes">Скорость погрузки на угольной шахте, т/ч.</param>
    public TestLine(double wearPerCell = 0.0001, double serviceAt = 0.9, int serviceHours = 10, bool signals = true,
        double mineHandlingTonnes = 100)
    {
        var map = NewMap(140, 40);
        var placement = SitePlacement.Basic();
        placement.Place(map, new NewSiteRequest("coal_mine", "coal_mine", "Шахта", new GridRect(26, 12, 6, 6)));
        placement.Place(map, new NewSiteRequest("iron_mine", "iron_mine", "Рудник", new GridRect(6, 30, 6, 6)));
        placement.Place(map, new NewSiteRequest("steel_plant", "steel_plant", "Завод", new GridRect(120, 12, 6, 6)));
        placement.Place(map, new NewSiteRequest("city", "city", "Город", new GridRect(118, 30, 14, 8)));
        map.Networks["road"].LayPath(NetworkPaths.Polyline([P(120, 30), P(127, 30)]));
        map.Networks["road"].LayPath(NetworkPaths.Polyline([P(120, 30), P(120, 36)]));

        Simulation = new GameSimulation(new SimulationState(map));
        Economy = EconomyModule.Install(Simulation, EconomyCatalog(mineHandlingTonnes));
        Transport = TransportModule.Install(Simulation, TransportCatalog(wearPerCell, serviceAt, serviceHours));
        Builder = new Builder(map, ConstructionCatalog());
        Builder.AddDemolishGuard(new DelegateDemolishGuard(f => TransportModule.WhyFacilityInUse(Transport, f)));
        map.RequireRails().Rules.Add(TransportModule.RailGuard(Transport));

        Built(Builder.BuildRail(C(2, 20), C(132, 20)));
        // Второй путь станции шахты: с обеих сторон S-образные кривые выводят его на главный путь стрелками.
        Built(Builder.BuildRail(C(26, 21), C(34, 21)));
        Built(Builder.BuildRail(C(26, 21), C(10, 20)));
        Built(Builder.BuildRail(C(34, 21), C(50, 20)));
        Built(Builder.BuildPlatform("rail_station", C(27, 20), C(32, 20)));
        Built(Builder.BuildPlatform("rail_station", C(27, 21), C(32, 21)));
        Built(Builder.BuildPlatform("rail_station", C(120, 20), C(123, 20)));
        var depot = Builder.BuildRailDepot("rail_depot", C(3, 20), C(6, 20));
        Built(depot);
        Depot = depot.Built!;
        if (signals)
        {
            foreach (var x in new[] { 74, 64, 54 })
                Built(Builder.BuildRailSignal("block_signal", C(x, 20), C(x - 1, 20)));
        }

        Built(Builder.BuildStation("truck_station", P(126, 12), P(126, 15)));
        Built(Builder.BuildPath("road", P(126, 16), P(126, 29)));
        Built(Builder.BuildPath("road", P(126, 29), P(126, 30)));
        Built(Builder.BuildFacility("city_truck_stop", P(122, 30), P(124, 30)));
        Garage = Builder.BuildFacility("truck_garage", P(128, 24), P(127, 24)).Built!;
        Built(Builder.BuildPath("road", P(127, 24), P(126, 24)));
    }

    /// <summary>Пустая карта с травой, дорогами по клеткам и рельсовым графом.</summary>
    public static WorldMap NewMap(int width, int height) =>
        new(width, height, 0, new TerrainCatalog([new TerrainCategory("surface", [new TypeId("grass")], "grass")]),
        [
            new NetworkDefinition("road", EdgeNetworkLayer.LayoutName),
            new NetworkDefinition("rail", NetworkDefinition.GraphLayout),
        ], RailEditorSettings.Default.NetworkSettings("rail"));

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
    public static void Built(BridgePlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
    public static void Built(RailPlan plan) => Assert.True(plan.Result is { Committed: true }, string.Join("; ", plan.Problems.Select(p => p.Message)));
    public static void Built(RailEditPreview? result) =>
        Assert.True(result is { Committed: true }, result is null ? "нет соединения" : string.Join("; ", result.Issues));

    public static EconomyCatalog EconomyCatalog(double mineHandlingTonnes = 100) => new(
        [new ResourceDefinition("coal"), new ResourceDefinition("iron_ore"), new ResourceDefinition("steel")],
        [
            new EconomicSiteDefinition("coal_mine", new Dictionary<TypeId, Mass> { ["coal"] = T(1000) }, T(mineHandlingTonnes),
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
            new NetworkBuildDefinition("rail", [], []),
            new NetworkBuildDefinition("road", [new TypeId("city")], [new TypeId("truck_station")]),
        ],
        [
            new StationDefinition("rail_station", "rail", [new TypeId("coal_mine"), new TypeId("iron_mine"), new TypeId("steel_plant")], 0, 2, 20),
            new StationDefinition("truck_station", "road", [new TypeId("coal_mine"), new TypeId("iron_mine"), new TypeId("steel_plant")], 4),
        ],
        [],
        [
            new FacilityDefinition("rail_depot", "Депо", "rail", FacilityPlacement.RailDepot, [], 4),
            new FacilityDefinition("truck_garage", "Гараж", "road", FacilityPlacement.Depot, [], 1),
            new FacilityDefinition("city_truck_stop", "Пункт", "road", FacilityPlacement.StopOnNetwork, [new TypeId("city")], 3),
        ],
        [new BridgeDefinition("rail_bridge", "rail", 12), new BridgeDefinition("road_bridge", "road", 12)],
        [new SignalDefinition("block_signal", "rail", SignalLogic.Block), new SignalDefinition("path_signal", "rail", SignalLogic.Path)],
        [new LevelCrossing("road", "rail")]);
}
