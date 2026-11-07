using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Economy.Runtime;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Stops;

namespace Mintrans.Transport.Reports;

/// <summary>
/// Сводка по направлению А–Б (docs/11б, docs/12): сколько техники на нём, какую перевозку она может дать за сутки
/// и сколько фактически доставила.
/// </summary>
/// <param name="Vehicles">Вся техника с этим маршрутом.</param>
/// <param name="Working">Из неё сейчас работает (не стоит в депо, не остановлена, не разбита).</param>
/// <param name="Capacity">Суммарная вместимость работающей техники.</param>
/// <param name="DistanceCells">Длина пути А→Б в клетках или null, если пути нет.</param>
/// <param name="PerDay">Оценка перевозки за сутки: вместимость × сутки ÷ время цикла А–Б–А каждой единицы.</param>
/// <param name="Delivered">Фактически выгружено в Б за последние <paramref name="Days"/> суток.</param>
public sealed record DirectionSummary(
    StopRef A, StopRef B, int Vehicles, int Working, Mass Capacity, int? DistanceCells, Mass PerDay, Mass Delivered, int Days);

/// <summary>Отчёты о перевозках для игрока. Только читают состояние, ничего не меняют.</summary>
public static class RouteReport
{
    /// <summary>За сколько суток показывать фактическую доставку.</summary>
    public const int RecentDays = 7;

    /// <summary>
    /// Сводка по направлению. Время цикла единицы — путь туда и обратно с её скоростью плюс погрузка и выгрузка
    /// полной вместимости со скоростью пунктов (с учётом длины платформы); ожидание у сигналов, очереди и ТО не учтены,
    /// поэтому это верхняя оценка.
    /// </summary>
    public static DirectionSummary Direction(TransportState transport, EconomyState economy, StopRef a, StopRef b, long today)
    {
        var all = transport.Vehicles.Where(v => v.A == a && v.B == b).ToList();
        var working = all.Where(v => v.State is not (VehicleState.InDepot or VehicleState.Stopped or VehicleState.Crashed)).ToList();
        var stopA = transport.Stops.Find(a);
        var stopB = transport.Stops.Find(b);
        var distance = stopA is not null && stopB is not null && all.Count > 0 ? Distance(transport, stopA, stopB, all[0]) : null;

        var perDay = Mass.Zero;
        if (distance is { } cells && stopA is not null && stopB is not null)
        {
            var sender = economy.At(stopA.Owner);
            var receiver = economy.At(stopB.Owner);
            foreach (var vehicle in working.Where(v => v.Speed > 0))
            {
                var capacity = vehicle.Capacity.Tonnes;
                var hours = 2.0 * cells / vehicle.Speed
                            + Handling(capacity, sender, vehicle.PlatformFit(stopA.Length))
                            + Handling(capacity, receiver, vehicle.PlatformFit(stopB.Length));
                if (hours > 0)
                    perDay += Mass.FromTonnes(capacity * 24 / hours);
            }
        }

        var capacityTotal = working.Aggregate(Mass.Zero, (sum, v) => sum + v.Capacity);
        var delivered = transport.DeliveredSince(a, b, today - RecentDays + 1);
        return new DirectionSummary(a, b, all.Count, working.Count, capacityTotal, distance, perDay, delivered, RecentDays);
    }

    /// <summary>Груз, который едет к объекту: в технике на этапе «к Б», чей пункт Б принадлежит этому объекту.</summary>
    public static IReadOnlyDictionary<TypeId, (Mass Amount, int Vehicles)> InTransitTo(TransportState transport, SiteId site)
    {
        var result = new SortedDictionary<string, (TypeId Resource, Mass Amount, int Vehicles)>();
        foreach (var vehicle in transport.Vehicles.Where(v => v.Leg == Leg.ToB && v.State != VehicleState.Crashed && v.Cargo > Mass.Zero))
        {
            if (vehicle.B is not { } b || transport.Stops.Find(b)?.Owner != site)
                continue;
            foreach (var group in vehicle.Units.Where(u => u.CargoType is not null).GroupBy(u => u.CargoType!.Value))
            {
                var amount = group.Aggregate(Mass.Zero, (sum, u) => sum + u.Cargo);
                var known = result.TryGetValue(group.Key.Value, out var entry) ? entry : (Resource: group.Key, Amount: Mass.Zero, Vehicles: 0);
                result[group.Key.Value] = (group.Key, known.Amount + amount, known.Vehicles + 1);
            }
        }
        return result.Values.ToDictionary(r => r.Resource, r => (r.Amount, r.Vehicles));
    }

    private static double Handling(double tonnes, EconomicSite? site, double fit)
    {
        var rate = site?.Definition.HandlingPerHour.Tonnes ?? 0;
        return rate > 0 ? tonnes / (rate * fit) : 0;
    }

    /// <summary>Длина пути от пункта А до Б по сети техники, без учёта занятости.</summary>
    private static int? Distance(TransportState transport, Stop a, Stop b, Vehicle sample)
    {
        if (sample.Network.Trains)
            return RailDistance(transport.Rail(sample.Network.Network).Rails, a, b);
        var network = transport.NetworkLayer(sample.Network.Network);
        if (a.Area.Cells().Where(network.HasAny).Cast<GridPos?>().FirstOrDefault() is not { } start)
            return null;
        var route = Pathfinder.Search(network, new RoutePoint(start, null, null), b.Area.Cells().ToHashSet(),
            new RouteOptions { RunThrough = sample.Network.Trains });
        return route is null ? null : route.Points.Count - 1;
    }

    /// <summary>Длина пути по рельсам от платформ А до дальнего края платформ Б, в клетках по 5 м.</summary>
    private static int? RailDistance(World.Rail.RailNetwork rails, Stop a, Stop b)
    {
        static IEnumerable<World.Rail.TrackPosition> Ends(World.Rail.RailNetwork rails, Stop stop, bool far)
        {
            foreach (var berth in stop.Berths.Where(rails.ContainsBerth).Select(rails.Berth))
            {
                var forward = new World.Rail.DirectedEdge(berth.Edge, true);
                var backward = forward.Reverse();
                yield return new World.Rail.TrackPosition(forward, far ? berth.ToM : berth.FromM);
                yield return new World.Rail.TrackPosition(backward, rails.LengthOf(backward) - (far ? berth.FromM : berth.ToM));
            }
        }
        var targets = Ends(rails, b, far: true).ToList();
        var best = Ends(rails, a, far: true)
            .Select(start => Rail.TrainRouter.Find(rails, start, targets, new Rail.TrainRouteOptions { SpeedMph = 1 })?.Hours)
            .Where(h => h is not null)
            .DefaultIfEmpty(null)
            .Min();
        return best is { } metres ? (int)Math.Round(metres / Core.Spatial.SpatialScale.CellM) : null;
    }
}
