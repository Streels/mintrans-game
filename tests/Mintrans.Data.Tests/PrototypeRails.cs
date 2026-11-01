using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.World.Facilities;
using Mintrans.World.Rail;

namespace Mintrans.Data.Tests;

/// <summary>
/// Железная дорога прототипа, построенная инструментами игрока (docs/01, docs/10в). Угольная линия: прямая под шахтой
/// (y = 50) с платформой и депо в западном тупике, затем петлёй направо вдоль восточного края карты — к прямой над заводом
/// (y = 147) с платформой. Рудная линия: прямая под шахтой (y = 52) с платформой и депо в восточном тупике, петлёй налево
/// вдоль западного края — к той же прямой с запада. Поезда двух линий подходят к платформе завода с разных сторон и не запирают друг друга.
/// </summary>
internal static class PrototypeRails
{
    public static WorldPoint C(int x, int y) => SpatialScale.CellCenter(new GridPos(x, y));

    public static (Facility CoalDepot, Facility OreDepot) Build(Builder builder)
    {
        // Прямые у предприятий.
        Built(builder.BuildRail(C(180, 50), C(215, 50)));
        Built(builder.BuildRail(C(40, 52), C(80, 52)));
        Built(builder.BuildRail(C(120, 147), C(170, 147)));
        // Петли между ними — в два шага, как строит игрок: от конца прямой дугой 90° и прямой до промежуточной точки,
        // оттуда второй дугой 90° к концу прямой у завода (радиус 200 м).
        Built(builder.BuildRail(C(215, 50), C(255, 107)));
        Built(builder.BuildRail(C(255, 107), C(170, 147)));
        Built(builder.BuildRail(C(40, 52), C(0, 107)));
        Built(builder.BuildRail(C(0, 107), C(120, 147)));
        // Платформы под шахтами и над заводом, депо в тупиках шахтных прямых.
        Built(builder.BuildPlatform("rail_station", C(200, 50), C(207, 50)));
        Built(builder.BuildPlatform("rail_station", C(62, 52), C(69, 52)));
        Built(builder.BuildPlatform("rail_station", C(150, 147), C(163, 147)));
        var coal = builder.BuildRailDepot("rail_depot", C(183, 50), C(186, 50));
        Built(coal);
        var ore = builder.BuildRailDepot("rail_depot", C(73, 52), C(76, 52));
        Built(ore);
        return (coal.Built!, ore.Built!);
    }

    public static void Built(RailEditPreview? result) =>
        Assert.True(result is { Committed: true }, result is null ? "нет соединения" : string.Join("; ", result.Issues));

    public static void Built(RailPlan plan) =>
        Assert.True(plan.Result is { Committed: true }, string.Join("; ", plan.Problems.Select(p => p.Message)));

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
