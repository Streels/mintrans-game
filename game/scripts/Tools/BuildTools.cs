using Mintrans.Construction;
using Mintrans.Construction.Rail;
using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;

namespace Mintrans.Game.Tools;

/// <summary>Точка под курсором: клетка и точное место в мире (метры).</summary>
public readonly record struct BuildPoint(GridPos Cell, WorldPoint At);

/// <summary>
/// Что показать при протягивании: клетки, проблемные клетки, оси будущих путей (в метрах), можно ли строить и пояснение.
/// </summary>
public sealed record ToolPreview(IReadOnlyList<GridPos> Cells, IReadOnlySet<GridPos> BadCells, bool CanBuild, string Message)
{
    public IReadOnlyList<PathGeometry> Shapes { get; init; } = [];
}

/// <summary>
/// Инструмент строительства: план по протяжке мышью от точки до точки и постройка этого плана.
/// Новый инструмент — новый класс и одна строка в <see cref="BuildTools.Create"/>.
/// </summary>
public interface IBuildTool
{
    /// <summary>Идентификатор для названия на кнопке (из names.ru.json).</summary>
    string Id { get; }

    ToolPreview Preview(BuildPoint from, BuildPoint to);

    ToolPreview Apply(BuildPoint from, BuildPoint to);
}

/// <summary>
/// Инструмент с отложенным подтверждением (docs/10в, раздел 2): после протяжки план висит в предпросмотре,
/// колесо мыши и Tab меняют его, щелчок подтверждает, Esc или правая кнопка — отменяют. Мир до подтверждения не меняется.
/// </summary>
public interface IPendingTool : IBuildTool
{
    /// <summary>Есть ли план, ждущий подтверждения.</summary>
    bool HasPending { get; }

    ToolPreview? PendingPreview { get; }

    void Scroll(int step);

    void NextVariant();

    ToolPreview Confirm();

    void Cancel();
}

/// <summary>Прокладка клеточной сети: дорога.</summary>
public sealed class PathTool(Builder builder, TypeId network) : IBuildTool
{
    public string Id => $"tool.{network}";

    public ToolPreview Preview(BuildPoint from, BuildPoint to) => Describe(builder.PlanPath(network, from.Cell, to.Cell), from.Cell);

    public ToolPreview Apply(BuildPoint from, BuildPoint to) => Describe(builder.BuildPath(network, from.Cell, to.Cell), from.Cell);

    private static ToolPreview Describe(BuildPlan plan, GridPos from)
    {
        var cells = plan.Cells.Count > 0 ? plan.Cells : [from];
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message
            : plan.Cells.Count > 0 ? $"Клеток: {plan.Cells.Count}"
            : "Протяните мышью от клетки к клетке";
        return new ToolPreview(cells, BadCells(plan.Problems), plan.CanBuild, message);
    }

    internal static HashSet<GridPos> BadCells(IEnumerable<BuildProblem> problems) =>
        problems.Select(p => p.Cell).OfType<GridPos>().ToHashSet();
}

/// <summary>
/// Рельсы «Соединить А→Б» (docs/10в, раздел 2): протянуть от конца пути (или точки на прямом пути — будет стрелка,
/// или свободного места) к другой точке. После протяжки соединение ждёт: колесо — радиус, Tab — вариант,
/// щелчок — построить, Esc — отмена.
/// </summary>
public sealed class RailConnectTool(Builder builder, TypeId network) : IPendingTool
{
    private RailConnection? _pending;

    public string Id => $"tool.{network}";

    public bool HasPending => _pending is not null;

    public ToolPreview? PendingPreview => _pending is { } connection ? Describe(connection, pending: true) : null;

    public ToolPreview Preview(BuildPoint from, BuildPoint to) =>
        from.At.DistanceTo(to.At) < SpatialScale.CellM
            ? new ToolPreview([from.Cell], new HashSet<GridPos>(), false,
                "Протяните от конца пути или от прямого пути (будет стрелка) к другой точке")
            : Describe(builder.PlanRail(from.At, to.At), pending: false);

    /// <summary>Отпустили кнопку: соединение не строится сразу, а ждёт подтверждения.</summary>
    public ToolPreview Apply(BuildPoint from, BuildPoint to)
    {
        if (from.At.DistanceTo(to.At) < SpatialScale.CellM)
            return Preview(from, to);
        _pending = builder.PlanRail(from.At, to.At);
        return Describe(_pending, pending: true);
    }

    public void Scroll(int step) => _pending?.ScrollRadius(step);

    public void NextVariant() => _pending?.NextVariant();

    public ToolPreview Confirm()
    {
        if (_pending is not { } connection)
            return new ToolPreview([], new HashSet<GridPos>(), false, "Нечего строить");
        _pending = null;
        if (!connection.CanConfirm)
            return Describe(connection, pending: false);
        var result = connection.Confirm();
        return new ToolPreview([], new HashSet<GridPos>(), result.Committed,
            result.Committed ? $"Путь {connection.Current!.LengthM:0} м" : Builder.Sentence(result.Issues[0]));
    }

    public void Cancel() => _pending = null;

    private static ToolPreview Describe(RailConnection connection, bool pending)
    {
        if (connection.Current is not { } option)
        {
            var reason = connection.Rejections.FirstOrDefault(r => !r.Contains("круче")) ?? connection.Rejections.FirstOrDefault()
                ?? "соединить нельзя";
            return new ToolPreview([], new HashSet<GridPos>(), false, "Нельзя: " + Builder.Sentence(StripFamily(reason)));
        }
        var radius = double.IsPositiveInfinity(option.RadiusM) ? "прямая" : $"R {option.RadiusM:0} м";
        var variants = connection.Radii[connection.RadiusIndex].Variants.Count;
        var hint = pending
            ? $"колесо — радиус ({connection.RadiusIndex + 1} из {connection.Radii.Count})" +
              (variants > 1 ? $", Tab — вариант ({connection.VariantIndex + 1} из {variants})" : "") + ", щелчок — построить, Esc — отмена"
            : "отпустите — выбрать радиус";
        var cells = option.Check.Spatial?.Cells.Values.SelectMany(c => c).Distinct().ToList() ?? [];
        return new ToolPreview(cells, new HashSet<GridPos>(), true, $"{radius}, {option.LengthM:0} м: {hint}")
        {
            Shapes = [option.Candidate.Geometry],
        };
    }

    /// <summary>«RSR, R 300 м: причина» → «причина».</summary>
    private static string StripFamily(string reason) =>
        reason.IndexOf("м: ", StringComparison.Ordinal) is var i and >= 0 ? reason[(i + 3)..] : reason;
}

/// <summary>Предпросмотр и постройка рельсового плана (мост, платформа, депо, сигнал).</summary>
public abstract class RailPlanTool(string id) : IBuildTool
{
    public string Id => id;

    protected abstract RailPlan Plan(BuildPoint from, BuildPoint to);

    protected abstract RailPlan Build(BuildPoint from, BuildPoint to);

    public ToolPreview Preview(BuildPoint from, BuildPoint to) => Describe(Plan(from, to));

    public ToolPreview Apply(BuildPoint from, BuildPoint to) => Describe(Build(from, to));

    private static ToolPreview Describe(RailPlan plan) =>
        new(plan.Cells.Count > 0 ? plan.Cells : [], PathTool.BadCells(plan.Problems), plan.CanBuild,
            plan.Problems.Count > 0 ? plan.Problems[0].Message : plan.Summary)
        {
            Shapes = plan.Shapes,
        };
}

/// <summary>Ж/д мост (docs/10б): протянуть от опоры до опоры по прямой — от конца пути или со свободного места.</summary>
public sealed class RailBridgeTool(Builder builder, TypeId bridge) : RailPlanTool(bridge.Value)
{
    protected override RailPlan Plan(BuildPoint from, BuildPoint to) => builder.PlanRailBridge(bridge, from.At, to.At);
    protected override RailPlan Build(BuildPoint from, BuildPoint to) => builder.BuildRailBridge(bridge, from.At, to.At);
}

/// <summary>Платформа ж/д станции (docs/10в, раздел 4): протянуть вдоль прямого пути рядом с шахтой или заводом.</summary>
public sealed class PlatformTool(Builder builder, TypeId station) : RailPlanTool(station.Value)
{
    protected override RailPlan Plan(BuildPoint from, BuildPoint to) => builder.PlanPlatform(station, from.At, to.At);
    protected override RailPlan Build(BuildPoint from, BuildPoint to) => builder.BuildPlatform(station, from.At, to.At);
}

/// <summary>Ж/д депо: щёлкнуть по прямому пути и протянуть в сторону, куда встанет депо.</summary>
public sealed class RailDepotTool(Builder builder, TypeId facility) : RailPlanTool(facility.Value)
{
    protected override RailPlan Plan(BuildPoint from, BuildPoint to) => builder.PlanRailDepot(facility, from.At, to.At);
    protected override RailPlan Build(BuildPoint from, BuildPoint to) => builder.BuildRailDepot(facility, from.At, to.At);
}

/// <summary>Сигнал (docs/10а, docs/10в, раздел 5): щелчок по пути — двусторонний, протяжка вдоль пути — односторонний.</summary>
public sealed class RailSignalTool(Builder builder, TypeId signal) : RailPlanTool(signal.Value)
{
    protected override RailPlan Plan(BuildPoint from, BuildPoint to) => builder.PlanRailSignal(signal, from.At, to.At);
    protected override RailPlan Build(BuildPoint from, BuildPoint to) => builder.BuildRailSignal(signal, from.At, to.At);
}

/// <summary>Автостанция предприятия.</summary>
public sealed class StationTool(Builder builder, TypeId station) : IBuildTool
{
    public string Id => station.Value;

    public ToolPreview Preview(BuildPoint from, BuildPoint to) => Describe(builder.PlanStation(station, from.Cell, to.Cell));

    public ToolPreview Apply(BuildPoint from, BuildPoint to) => Describe(builder.BuildStation(station, from.Cell, to.Cell));

    private static ToolPreview Describe(StationPlan plan)
    {
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message : $"Станция у «{plan.Target!.Name}», длина {plan.Length}";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Автогараж или городской пункт.</summary>
public sealed class FacilityTool(Builder builder, TypeId facility) : IBuildTool
{
    public string Id => facility.Value;

    public ToolPreview Preview(BuildPoint from, BuildPoint to) => Describe(builder.PlanFacility(facility, from.Cell, to.Cell));

    public ToolPreview Apply(BuildPoint from, BuildPoint to) => Describe(builder.BuildFacility(facility, from.Cell, to.Cell));

    private static ToolPreview Describe(FacilityPlan plan)
    {
        var hint = plan.Definition.Placement == FacilityPlacement.Depot
            ? "протяните мышью в сторону въезда"
            : "по улице города";
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message
            : plan.Built is { } built ? built.Name
            : $"{plan.Definition.Title}: {hint}";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Мост дороги (docs/10б): протянуть от опоры до опоры по прямой.</summary>
public sealed class BridgeTool(Builder builder, TypeId bridge) : IBuildTool
{
    public string Id => bridge.Value;

    public ToolPreview Preview(BuildPoint from, BuildPoint to) => Describe(builder.PlanBridge(bridge, from.Cell, to.Cell), from.Cell == to.Cell);

    public ToolPreview Apply(BuildPoint from, BuildPoint to) => Describe(builder.BuildBridge(bridge, from.Cell, to.Cell), false);

    private static ToolPreview Describe(BridgePlan plan, bool idle)
    {
        var message = idle ? "Протяните от опоры до опоры по прямой: пролёт пройдёт над путями, дорогами и водой"
            : plan.Problems.Count > 0 ? plan.Problems[0].Message
            : $"Мост: {plan.Cells.Count} кл.";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Снять сигналы прямоугольником; пути остаются.</summary>
public sealed class SignalRemoveTool(Builder builder) : IBuildTool
{
    public string Id => "tool.remove_signals";

    public ToolPreview Preview(BuildPoint from, BuildPoint to) => Describe(builder.PlanRemoveSignals(from.Cell, to.Cell));

    public ToolPreview Apply(BuildPoint from, BuildPoint to) => Describe(builder.RemoveSignals(from.Cell, to.Cell));

    private static ToolPreview Describe(BuildPlan plan) =>
        new(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild,
            plan.Problems.Count > 0 ? plan.Problems[0].Message : $"Снять сигналы: клеток {plan.Cells.Count}");
}

/// <summary>Снос прямоугольником: дороги, участки путей, станции и сооружения.</summary>
public sealed class DemolishTool(Builder builder) : IBuildTool
{
    public string Id => "tool.demolish";

    public ToolPreview Preview(BuildPoint from, BuildPoint to) => Describe(builder.PlanDemolish(from.Cell, to.Cell));

    public ToolPreview Apply(BuildPoint from, BuildPoint to) => Describe(builder.Demolish(from.Cell, to.Cell));

    private static ToolPreview Describe(BuildPlan plan) =>
        new(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild,
            plan.Problems.Count > 0 ? plan.Problems[0].Message : $"Снести клеток: {plan.Cells.Count}");
}

public static class BuildTools
{
    /// <summary>
    /// Инструменты по правилам строительства: сети (рельсы — соединением А→Б), мосты, станции, сооружения, сигналы, снос.
    /// Что строится на рельсовом графе, а что по клеткам, решает сеть, к которой относится вид постройки.
    /// </summary>
    public static IReadOnlyList<IBuildTool> Create(Builder builder) =>
    [
        .. builder.Catalog.Networks.Select(n => builder.IsRail(n.Network)
            ? (IBuildTool)new RailConnectTool(builder, n.Network)
            : new PathTool(builder, n.Network)),
        .. builder.Catalog.Bridges.Select(b => builder.IsRail(b.Network)
            ? (IBuildTool)new RailBridgeTool(builder, b.Id)
            : new BridgeTool(builder, b.Id)),
        .. builder.Catalog.Stations.Select(s => builder.IsRail(s.Network)
            ? (IBuildTool)new PlatformTool(builder, s.Id)
            : new StationTool(builder, s.Id)),
        .. builder.Catalog.Facilities.Select(f => f.Placement == FacilityPlacement.RailDepot
            ? (IBuildTool)new RailDepotTool(builder, f.Id)
            : new FacilityTool(builder, f.Id)),
        .. builder.Catalog.Signals.Select(s => (IBuildTool)new RailSignalTool(builder, s.Id)),
        .. builder.Catalog.Signals.Count > 0 ? new IBuildTool[] { new SignalRemoveTool(builder) } : [],
        new DemolishTool(builder),
    ];
}
