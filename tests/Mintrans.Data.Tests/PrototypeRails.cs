using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.World.Facilities;
using Mintrans.World.Rail;

namespace Mintrans.Data.Tests;

/// <summary>
/// Транспорт прототипа на карте 40×40 км, построенный инструментами игрока (docs/01, docs/10в).
/// Угольная линия: прямая под шахтой (y = 3142) с платформой и депо в западной части, петлёй направо (дуги 90° радиусом
/// 200 м) — к прямой над заводом (y = 3897) с востока. Рудная линия: прямая под шахтой (y = 3342) с платформой и депо
/// в восточной части, петлёй налево — к той же прямой с запада. Маршрутные сигналы на концах заводской прямой делают
/// общий участок отдельным блоком. Сталь — грузовиками: автостанция под заводом, дорога к улице города, гараж у дороги.
/// </summary>
internal static class PrototypeRails
{
    public static GridPos P(int x, int y) => new(x, y);

    public static WorldPoint C(int x, int y) => SpatialScale.CellCenter(new GridPos(x, y));

    /// <summary>Клетки для выбора пунктов маршрута и депо.</summary>
    public static readonly GridPos CoalPlatform = new(4910, 3142), OrePlatform = new(2810, 3342), PlantPlatform = new(3925, 3897);
    public static readonly GridPos CoalDepotCell = new(4871, 3142), OreDepotCell = new(2866, 3342);
    public static readonly GridPos TruckStation = new(3951, 3980), CityStop = new(3960, 4311), GarageCell = new(3961, 4000);

    public static (Facility CoalDepot, Facility OreDepot) Build(Builder builder)
    {
        // Прямые у предприятий: шахт и завода.
        Built(builder.BuildRail(C(4860, 3142), C(4980, 3142)));
        Built(builder.BuildRail(C(2760, 3342), C(2880, 3342)));
        Built(builder.BuildRail(C(3850, 3897), C(4050, 3897)));
        // Петли — в два шага, как строит игрок: от конца прямой дугой 90° и прямой до промежуточной точки,
        // оттуда второй дугой 90° к концу заводской прямой.
        Built(builder.BuildRail(C(4980, 3142), C(5020, 3857)));
        Built(builder.BuildRail(C(5020, 3857), C(4050, 3897)));
        Built(builder.BuildRail(C(2760, 3342), C(2720, 3857)));
        Built(builder.BuildRail(C(2720, 3857), C(3850, 3897)));
        // Платформы под шахтами и над заводом, депо на шахтных прямых.
        Built(builder.BuildPlatform("rail_station", C(4905, 3142), C(4914, 3142)));
        Built(builder.BuildPlatform("rail_station", C(2805, 3342), C(2814, 3342)));
        Built(builder.BuildPlatform("rail_station", C(3920, 3897), C(3929, 3897)));
        var coal = builder.BuildRailDepot("rail_depot", C(4870, 3142), C(4873, 3142));
        Built(coal);
        var ore = builder.BuildRailDepot("rail_depot", C(2865, 3342), C(2868, 3342));
        Built(ore);
        // Маршрутные сигналы на концах заводской прямой: общий участок у завода — отдельный блок,
        // и поезд одной линии не держит его, пока едет по своей петле (docs/10а).
        Built(builder.BuildRailSignal("path_signal", C(3860, 3897), C(3860, 3897)));
        Built(builder.BuildRailSignal("path_signal", C(4040, 3897), C(4040, 3897)));
        return (coal.Built!, ore.Built!);
    }

    /// <summary>Автостанция под заводом, дорога к улице города, городской пункт и автогараж.</summary>
    public static Facility BuildRoads(Builder builder)
    {
        Built(builder.BuildStation("truck_station", P(3950, 3980), P(3953, 3980)));
        Built(builder.BuildPath("road", P(3954, 3980), P(3960, 3980)));
        Built(builder.BuildPath("road", P(3960, 3980), P(3960, 4300)));
        Built(builder.BuildFacility("city_truck_stop", P(3960, 4310), P(3960, 4312)));
        var garage = builder.BuildFacility("truck_garage", P(3961, 4000), P(3960, 4000));
        Built(garage);
        return garage.Built!;
    }

    public static void Built(RailEditPreview? result) =>
        Assert.True(result is { Committed: true }, result is null ? "нет соединения" : string.Join("; ", result.Issues));

    public static void Built(RailPlan plan) =>
        Assert.True(plan.Result is { Committed: true }, string.Join("; ", plan.Problems.Select(p => p.Message)));

    public static void Built(BuildPlan plan) =>
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => $"{p.Cell}: {p.Message}")));

    public static void Built(StationPlan plan) => Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));

    public static void Built(FacilityPlan plan) =>
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => $"{p.Cell}: {p.Message}")));

    /// <summary>Доезжает ли поезд от платформы одной станции до платформы другой.</summary>
    public static bool Connected(RailNetwork rails, RailBerth from, RailBerth to)
    {
        var targets = new[]
        {
            new TrackPosition(new DirectedEdge(to.Edge, true), to.ToM),
            new TrackPosition(new DirectedEdge(to.Edge, false), rails.Edge(to.Edge).LengthM - to.FromM),
        };
        return new[] { true, false }.Any(forward =>
        {
            var start = new DirectedEdge(from.Edge, forward);
            return Transport.Rail.TrainRouter.Find(rails, rails.PositionAt(start, (from.FromM + from.ToM) / 2), targets,
                new Transport.Rail.TrainRouteOptions { SpeedMph = 1, ReverseHours = 1 }) is not null;
        });
    }
}
