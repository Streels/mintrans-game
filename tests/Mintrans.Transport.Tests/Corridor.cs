using Mintrans.Construction;
using Mintrans.Construction.Rail;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.Economy.Runtime;
using Mintrans.Economy.Systems;
using Mintrans.Simulation;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;
using Mintrans.Transport.Systems;
using Mintrans.World;
using Mintrans.World.Facilities;
using Mintrans.World.Rail;
using Mintrans.World.Sites;
using static Mintrans.Transport.Tests.TestLine;

namespace Mintrans.Transport.Tests;

/// <summary>
/// Однопутный коридор 160×40 клеток (800 × 200 м) для сценариев сигнализации (docs/10а), построенный инструментами игрока.
/// Шахта (4..9, 12..17) с платформой (5..8, 20) и завод (148..153, 12..17) с платформой (149..152, 20) на главном пути y = 20.
/// Главный путь проходит мостом (76..84, 20) над поперечной линией x = 80. Западное депо (54..57, 13) — на прямом
/// ответвлении y = 13, которое отходит от главного пути у x = 16 на восток; восточное (128..131, 27) — на ответвлении y = 27
/// от x = 90. Из обоих депо поезда выезжают на запад. Дорога x = 140 от автогаража (140, 4) пересекает главный путь
/// переездом и ведёт в город (134..145, 30..35) с двумя пунктами. Сигналы и разъезды каждый сценарий ставит сам.
/// </summary>
internal sealed class Corridor
{
    public GameSimulation Simulation { get; }
    public WorldMap Map => Simulation.State.Map;
    public RailNetwork Rails => Map.RequireRails();
    public EconomyState Economy { get; }
    public TransportState Transport { get; }
    public Builder Builder { get; }
    public Facility WestDepot { get; }
    public Facility EastDepot { get; }
    public Facility Garage { get; }

    /// <summary>Клетка переезда дороги через главный путь.</summary>
    public static readonly GridPos Crossing = new(140, 20);

    public Corridor()
    {
        var map = NewMap(160, 40);
        var placement = SitePlacement.Basic();
        placement.Place(map, new NewSiteRequest("coal_mine", "coal_mine", "Шахта", new GridRect(4, 12, 6, 6)));
        placement.Place(map, new NewSiteRequest("steel_plant", "steel_plant", "Завод", new GridRect(148, 12, 6, 6)));
        placement.Place(map, new NewSiteRequest("city", "city", "Город", new GridRect(134, 30, 12, 6)));

        Simulation = new GameSimulation(new SimulationState(map));
        Economy = EconomyModule.Install(Simulation, EconomyCatalog());
        Transport = TransportModule.Install(Simulation, TransportCatalog());
        Builder = new Builder(map, ConstructionCatalog());
        Builder.AddDemolishGuard(new DelegateDemolishGuard(f => TransportModule.WhyFacilityInUse(Transport, f)));
        map.RequireRails().Rules.Add(TransportModule.RailGuard(Transport));

        Built(Builder.BuildRail(C(2, 20), C(76, 20)));
        Built(Builder.BuildRailBridge("rail_bridge", C(76, 20), C(84, 20)));
        Built(Builder.BuildRail(C(84, 20), C(156, 20)));
        Built(Builder.BuildRail(C(80, 4), C(80, 36)));
        Built(Builder.BuildPlatform("rail_station", C(5, 20), C(8, 20)));
        Built(Builder.BuildPlatform("rail_station", C(149, 20), C(152, 20)));

        WestDepot = Siding(16, 52, 60, 13, 54);
        EastDepot = Siding(90, 124, 134, 27, 128);

        Garage = Builder.BuildFacility("truck_garage", P(140, 4), P(140, 5)).Built!;
        Built(Builder.BuildPath("road", P(140, 5), P(140, 33)));
        Built(Builder.BuildFacility("city_truck_stop", P(140, 32), P(140, 32)));
        Built(Builder.BuildFacility("city_truck_stop", P(140, 33), P(140, 33)));
    }

    /// <summary>
    /// Ответвление от главного пути у <paramref name="switchX"/> на восток к прямому участку y = <paramref name="y"/>
    /// (<paramref name="fromX"/>..<paramref name="toX"/>) с депо от <paramref name="depotX"/>: из депо поезд выезжает на запад.
    /// </summary>
    private Facility Siding(int switchX, int fromX, int toX, int y, int depotX)
    {
        Built(Builder.BuildRail(C(fromX, y), C(toX, y)));
        Built(Builder.BuildRail(C(switchX, 20), C(fromX, y)));
        var plan = Builder.BuildRailDepot("rail_depot", C(depotX, y), C(depotX + 3, y));
        Built(plan);
        return plan.Built!;
    }

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

    /// <summary>Сигнал на главном пути в клетке x: протяжка к соседней клетке — односторонний в её сторону, без неё — двусторонний.</summary>
    public void Signal(string kind, int x, int toward, int y = 20) => Built(Builder.BuildRailSignal(kind, C(x, y), C(toward, y)));

    /// <summary>
    /// Разъезд: второй путь y = 21 (46..54) S-образными кривыми выходит на главный путь стрелками у x = 30 и x = 70.
    /// Как принято в OpenTTD, у каждого пути разъезда своё направление: односторонний маршрутный сигнал на восток
    /// на главном пути в x = 50 и на запад на втором пути в x = 48 — навстречу им ехать нельзя, поэтому поезда сами
    /// расходятся по разным путям.
    /// </summary>
    public void PassingLoop()
    {
        Built(Builder.BuildRail(C(46, 21), C(54, 21)));
        Built(Builder.BuildRail(C(46, 21), C(30, 20)));
        Built(Builder.BuildRail(C(54, 21), C(70, 20)));
        Signal("path_signal", 50, 51);
        Signal("path_signal", 48, 47, y: 21);
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
