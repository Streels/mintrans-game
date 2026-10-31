using Mintrans.Core.Spatial;
using Mintrans.Economy;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Rail;
using Mintrans.Transport.Runtime;
using Mintrans.World.Rail;

namespace Mintrans.Transport.Systems;

public sealed partial class TransportSystem
{
    // ---------- Движение поездов ----------

    /// <summary>
    /// Поезд едет по маршруту на рельсовом графе (docs/10а, docs/10в): голова проходит за час «скорость» метров.
    /// На границе блока — у сигнала, на конце платформы или депо — он заново выбирает путь (узел маршрута, docs/12)
    /// и проверяет сигнал: блочный — свободен ли следующий блок, маршрутный — удалось ли закрепить путь до следующего
    /// безопасного места. Без сигналов поезд едет вслепую: въехав на занятый участок, он сталкивается с другим составом.
    /// </summary>
    private static void TravelTrain(Vehicle vehicle, Context context)
    {
        var rail = context.Transport.Rail(vehicle.Network.Network);
        var rails = rail.Rails;
        var (targets, targetName) = RailTargets(vehicle, context);
        if (targets is null)
        {
            rail.ReleaseAhead(vehicle);
            vehicle.SetState(VehicleState.Stuck, targetName);
            return;
        }
        if (vehicle.Body is null && !LeaveDepot(vehicle, rail, targets, context))
        {
            vehicle.SetState(VehicleState.Stuck, context.Map.Facilities.TryGet(vehicle.Depot, out _) && DepotBerth(vehicle, context) is not null
                ? $"нет пути до «{targetName}»"
                : "депо приписки не стоит на путях");
            return;
        }

        var revision = RailRevision(rails, context);
        if (vehicle.RailRoute is null || vehicle.RailRevision != revision)
        {
            // Сеть изменилась посреди пути: закреплённый путь переносится на новый маршрут, если он свободен.
            var heldAhead = rail.Reservations.Of(vehicle.Id).Count > 0;
            if (!Replan(vehicle, rail, targets, context))
            {
                vehicle.SetState(VehicleState.Stuck, $"нет пути до «{targetName}»");
                return;
            }
            if (heldAhead)
                rail.TryReserve(vehicle, vehicle.RailRoute!, 0, vehicle.Body!.HeadM);
        }
        if (vehicle.State == VehicleState.Stuck)
            vehicle.SetState(VehicleState.Travelling);
        vehicle.Reason = $"едет: {targetName}";

        vehicle.MoveBudget += vehicle.SpeedMph;
        var atNode = false;
        (TrackEdgeId Edge, double At)? cleared = null;
        var guard = 0;
        while (vehicle.MoveBudget > 1e-6 && guard++ < 10_000)
        {
            var route = vehicle.RailRoute!;
            var body = vehicle.Body!;
            var index = vehicle.RailRouteIndex;
            var edge = route.Edges[index];
            var last = index == route.Edges.Count - 1;
            var end = last ? route.TargetM : rails.LengthOf(edge);

            if (body.HeadM >= end - 1e-6)
            {
                if (last)
                {
                    Arrive(vehicle, context);
                    return;
                }
                if (route.ReversesAfter(index))
                {
                    // Разворот в тупике: головой становится последний вагон, путь ищется заново от него.
                    if (!body.FullyOut)
                    {
                        vehicle.MoveBudget = 0;
                        vehicle.Reason = "ждёт: состав не вышел из депо, развернуться нельзя";
                        return;
                    }
                    vehicle.Body = body.Flipped(rails);
                    rail.Moved(vehicle);
                    if (!Replan(vehicle, rail, targets, context))
                    {
                        vehicle.SetState(VehicleState.Stuck, $"нет пути до «{targetName}»");
                        return;
                    }
                    cleared = null;
                    continue;
                }
                body.Enter(rails, route.Edges[index + 1]);
                vehicle.RailRouteIndex = index + 1;
                rail.Moved(vehicle);
                continue;
            }

            var stepEnd = Math.Min(end, body.HeadM + vehicle.MoveBudget);
            // Граница, которую голова уже прошла проверку, не держит: ищем следующую.
            var searchFrom = cleared is { } passed && passed.Edge == edge.Edge && Math.Abs(passed.At - body.HeadM) < 1e-6
                ? body.HeadM
                : body.HeadM - 1e-6;
            if (rail.BoundaryAhead(edge, searchFrom, stepEnd) is { } found)
            {
                if (found.AtM > body.HeadM + 1e-6)
                    stepEnd = found.AtM;
                else
                {
                    // Голова у границы блока.
                    var at = new TrackPosition(edge, found.AtM);
                    if (!rail.HoldsAhead(vehicle, at))
                    {
                        // Узел маршрута (docs/12): перед сигналом поезд заново выбирает путь по текущей занятости блоков.
                        if (!atNode)
                        {
                            atNode = true;
                            if (!Replan(vehicle, rail, targets, context))
                            {
                                vehicle.SetState(VehicleState.Stuck, $"нет пути до «{targetName}»");
                                return;
                            }
                            continue;
                        }
                        var blocker = found.Boundary.Logic == SignalLogic.Block
                            ? rail.BlockBlocker(at with { DistanceM = at.DistanceM + RailTraffic.BeyondM }, vehicle.Id)
                            : rail.TryReserve(vehicle, route, index, found.AtM);
                        if (blocker is { } other)
                        {
                            WaitAtSignal(vehicle, found.Boundary, other, rail, targets, context);
                            return;
                        }
                    }
                    cleared = (edge.Edge, found.AtM);
                    continue;
                }
            }

            var step = TrackSpan.Along(rails, edge, body.HeadM, stepEnd);
            // Переезд (docs/10а): поезд ждёт машину, уже стоящую на переезде впереди.
            foreach (var crossing in context.Map.Crossings.On(edge.Edge))
            {
                if (!step.Overlaps(new TrackSpan(crossing.Edge, crossing.FromM, crossing.ToM), 0) ||
                    TruckOn(crossing.Cell, context) is not { } truck)
                    continue;
                vehicle.MoveBudget = 0;
                vehicle.BlockedHours++;
                vehicle.Reason = $"ждёт: на переезде «{truck.Name}»";
                return;
            }
            if (rail.Occupancy.ConflictWith(rails, step, vehicle.Id) is { } hit)
            {
                Crash(vehicle, context.Transport[hit], rail, context);
                return;
            }

            var moved = stepEnd - body.HeadM;
            if (moved <= 1e-9)
                break;
            body.Advance(rails, moved);
            rail.Moved(vehicle);
            vehicle.SetCell(SpatialScale.ToCell(rails.Locate(body.Head).At));
            vehicle.Wear += context.Maintenance.WearPerCell * moved / SpatialScale.CellM;
            vehicle.MoveBudget -= moved;
            vehicle.BlockedHours = 0;
            vehicle.CanTurn = false;
            atNode = false;
        }
        vehicle.RailRemainingM = Remaining(vehicle, rails);
    }

    /// <summary>Сколько метров маршрута осталось до цели.</summary>
    private static double? Remaining(Vehicle vehicle, RailNetwork rails)
    {
        if (vehicle.RailRoute is not { } route || vehicle.Body is not { } body)
            return null;
        var total = 0.0;
        for (var i = vehicle.RailRouteIndex; i < route.Edges.Count; i++)
            total += i == route.Edges.Count - 1 ? route.TargetM : rails.LengthOf(route.Edges[i]);
        return Math.Max(0, total - body.HeadM);
    }

    /// <summary>
    /// Выезд из депо (docs/10): состав встаёт на стоянку депо головой к выезду — в ту сторону пути, откуда цель ближе,
    /// и выходит на путь постепенно, по мере движения.
    /// </summary>
    private static bool LeaveDepot(Vehicle vehicle, RailTraffic rail, IReadOnlyCollection<TrackPosition> targets, Context context)
    {
        if (DepotBerth(vehicle, context) is not { } berth)
            return false;
        var rails = rail.Rails;
        var options = RouteOptions(vehicle, rail, context);
        var forward = new DirectedEdge(berth.Edge, true);
        var backward = new DirectedEdge(berth.Edge, false);
        var starts = new[]
        {
            new TrackPosition(forward, berth.ToM),
            new TrackPosition(backward, rails.LengthOf(backward) - berth.FromM),
        };
        var best = starts
            .Select(start => (Start: start, Route: TrainRouter.Find(rails, start, targets, options)))
            .Where(x => x.Route is not null)
            .MinBy(x => x.Route!.Hours);
        if (best.Route is null)
            return false;
        vehicle.Body = new TrainBody(best.Start.Edge, best.Start.DistanceM, vehicle.LengthM, 0);
        vehicle.RailRoute = best.Route;
        vehicle.RailRevision = RailRevision(rails, context);
        vehicle.CanTurn = false;
        vehicle.SetCell(SpatialScale.ToCell(rails.Locate(best.Start).At));
        rail.Moved(vehicle);
        return true;
    }

    private static RailBerth? DepotBerth(Vehicle vehicle, Context context) =>
        context.Map.Rails?.Berths.FirstOrDefault(b => b.Kind == BerthKind.Depot && b.Facility == vehicle.Depot);

    private static TrainRouteOptions RouteOptions(Vehicle vehicle, RailTraffic rail, Context context) => new()
    {
        SpeedMph = Math.Max(0.01, vehicle.SpeedMph),
        ReverseHours = context.RailRules.ReverseDelayHours,
        EnterDelay = next => RailPassable(vehicle, rail, next, context) ? rail.EnterDelay(vehicle, next) : null,
    };

    /// <summary>Сквозь чужие депо поезда не ездят: в депо заезжает только своя техника на обслуживание.</summary>
    private static bool RailPassable(Vehicle vehicle, RailTraffic rail, DirectedEdge next, Context context)
    {
        foreach (var berth in rail.Rails.BerthsOn(next.Edge).Where(b => b.Kind == BerthKind.Depot))
            if (!(vehicle.Leg == Leg.ToDepot && berth.Facility == vehicle.Depot))
                return false;
        return true;
    }

    /// <summary>
    /// Найти маршрут поезда от головы. Только что вставший поезд (станция, депо) может уехать и назад —
    /// тогда состав разворачивается, и головой становится последний вагон.
    /// </summary>
    private static bool Replan(Vehicle vehicle, RailTraffic rail, IReadOnlyCollection<TrackPosition> targets, Context context)
    {
        rail.ReleaseAhead(vehicle);
        var rails = rail.Rails;
        var body = vehicle.Body!;
        var options = RouteOptions(vehicle, rail, context);
        var forward = TrainRouter.Find(rails, body.Head, targets, options);
        TrainRoute? backward = null;
        TrainBody? flipped = null;
        if (vehicle.CanTurn && body.FullyOut)
        {
            flipped = body.Flipped(rails);
            backward = TrainRouter.Find(rails, flipped.Head, targets, options);
        }

        var revision = RailRevision(rails, context);
        if (backward is not null && (forward is null || backward.Hours + options.ReverseHours < forward.Hours))
        {
            vehicle.Body = flipped;
            rail.Moved(vehicle);
            vehicle.RailRoute = backward;
        }
        else if (forward is not null)
            vehicle.RailRoute = forward;
        else
            return false;
        vehicle.RailRevision = revision;
        vehicle.RailRemainingM = Remaining(vehicle, rails);
        return true;
    }

    /// <summary>
    /// Красный сигнал (docs/10а): поезд стоит. С настройкой «разворачиваться» он, подождав, разворачивается
    /// (кроме одностороннего маршрутного сигнала). Долгая стоянка — предупреждение игроку; сама блокировка не устраняется.
    /// </summary>
    private static void WaitAtSignal(Vehicle vehicle, RailBoundary boundary, VehicleId blocker, RailTraffic rail,
        IReadOnlyCollection<TrackPosition> targets, Context context)
    {
        vehicle.MoveBudget = 0;
        vehicle.BlockedHours++;
        var other = context.Transport.Vehicles.FirstOrDefault(v => v.Id == blocker)?.Name ?? "другой поезд";
        vehicle.Reason = boundary.Signal switch
        {
            { Logic: SignalLogic.Block } => $"ждёт у блочного сигнала: впереди «{other}»",
            { Logic: SignalLogic.Path } => $"ждёт у маршрутного сигнала: путь занят («{other}»)",
            _ => $"ждёт у {boundary.Where}: путь занят («{other}»)",
        };
        if (vehicle.BlockedHours == context.RailRules.StuckWarningHours)
            context.Post($"«{vehicle.Name}» стоит у {boundary.Where} уже {vehicle.BlockedHours} ч — возможна взаимная блокировка. " +
                         "Проверьте сигналы и разъезды.");

        if (vehicle.RedSignal == RedSignalBehavior.Reverse && vehicle.BlockedHours >= context.RailRules.ReverseAfterHours
            && !boundary.OneWayPath && vehicle.Body is { FullyOut: true } body)
        {
            vehicle.Body = body.Flipped(rail.Rails);
            rail.Moved(vehicle);
            vehicle.BlockedHours = 0;
            if (Replan(vehicle, rail, targets, context))
                vehicle.Reason = "развернулся у красного сигнала";
            else
                vehicle.SetState(VehicleState.Stuck, "развернулся, но пути нет");
        }
    }

    /// <summary>
    /// Крушение (docs/10а, модель OpenTTD): оба состава разбиты, груз в них потерян, обломки занимают путь
    /// <see cref="RailTrafficRules.WreckHours"/> часов. Игра не исправляет схему сигналов за игрока.
    /// </summary>
    private static void Crash(Vehicle mover, Vehicle other, RailTraffic rail, Context context)
    {
        var wasWreck = other.State == VehicleState.Crashed;
        var cell = mover.Position;
        foreach (var vehicle in new[] { mover, other }.Where(v => v.State != VehicleState.Crashed))
        {
            foreach (var unit in vehicle.Units.Where(u => u.CargoType is not null))
            {
                context.Economy.WriteOff(unit.CargoType!.Value, unit.Cargo);
                unit.Cargo = Mass.Zero;
                unit.CargoType = null;
            }
            rail.ReleaseAhead(vehicle);
            vehicle.ClearPath();
            vehicle.MoveBudget = 0;
            vehicle.SetState(VehicleState.Crashed, "разбит в крушении");
        }
        context.Post(wasWreck
            ? $"Крушение у клетки {cell.X}, {cell.Y}: «{mover.Name}» врезался в обломки «{other.Name}»."
            : $"Крушение у клетки {cell.X}, {cell.Y}: столкнулись «{mover.Name}» и «{other.Name}». Груз потерян.");
    }

    private static int RailRevision(RailNetwork rails, Context context) => rails.Revision * 31 + context.Map.Facilities.Revision;

    /// <summary>
    /// Куда едет поезд: дальний край каждой платформы станции (поезд встаёт на платформу целиком) или стоянки депо —
    /// в обе стороны пути.
    /// </summary>
    private static (IReadOnlyCollection<TrackPosition>? Targets, string Name) RailTargets(Vehicle vehicle, Context context)
    {
        var rails = context.Map.RequireRails();
        IEnumerable<RailBerth> berths;
        string name;
        if (vehicle.Leg == Leg.ToDepot)
        {
            if (!context.Map.Facilities.TryGet(vehicle.Depot, out var depot))
                return (null, "депо приписки снесено");
            berths = rails.Berths.Where(b => b.Kind == BerthKind.Depot && b.Facility == vehicle.Depot).ToList();
            name = depot.Name;
        }
        else
        {
            var reference = vehicle.Leg == Leg.ToA ? vehicle.A : vehicle.B;
            if (reference is not { } r || context.Transport.Stops.Find(r) is not { } stop)
                return (null, "пункт маршрута снесён");
            berths = stop.Berths.Where(rails.ContainsBerth).Select(rails.Berth).ToList();
            name = stop.Name;
        }
        var targets = new List<TrackPosition>();
        foreach (var berth in berths)
        {
            var forward = new DirectedEdge(berth.Edge, true);
            var backward = forward.Reverse();
            targets.Add(new TrackPosition(forward, berth.ToM));
            targets.Add(new TrackPosition(backward, rails.LengthOf(backward) - berth.FromM));
        }
        return targets.Count == 0 ? (null, $"у «{name}» нет путей") : (targets, name);
    }

    /// <summary>Стоянка под головой поезда, приехавшего к цели.</summary>
    private static RailBerthId? BerthAtHead(Vehicle vehicle, Context context)
    {
        if (vehicle.Body is not { } body || context.Map.Rails is not { } rails)
            return null;
        var s = rails.EdgeDistance(body.Head);
        return rails.BerthsOn(body.HeadEdge.Edge).FirstOrDefault(b => b.Covers(s, 0.01))?.Id;
    }
}
