using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;

namespace Mintrans.Game.Tools;

/// <summary>Что показать при протягивании: клетки, проблемные клетки, можно ли строить и пояснение.</summary>
public sealed record ToolPreview(IReadOnlyList<GridPos> Cells, IReadOnlySet<GridPos> BadCells, bool CanBuild, string Message);

/// <summary>
/// Инструмент строительства: план по протяжке мышью от клетки до клетки и постройка этого плана.
/// Новый инструмент — новый класс и одна строка в <see cref="BuildTools.Create"/>.
/// </summary>
public interface IBuildTool
{
    /// <summary>Идентификатор для названия на кнопке (из names.ru.json).</summary>
    string Id { get; }

    ToolPreview Preview(GridPos from, GridPos to);

    ToolPreview Apply(GridPos from, GridPos to);
}

/// <summary>Прокладка сети: рельсы или дорога.</summary>
public sealed class PathTool(Builder builder, TypeId network) : IBuildTool
{
    public string Id => $"tool.{network}";

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanPath(network, from, to), from);

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildPath(network, from, to), from);

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

/// <summary>Грузовая станция предприятия.</summary>
public sealed class StationTool(Builder builder, TypeId station) : IBuildTool
{
    public string Id => station.Value;

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanStation(station, from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildStation(station, from, to));

    private static ToolPreview Describe(StationPlan plan)
    {
        var tracks = plan.Station.MaxTracks > 1 ? $", путей: {plan.Tracks}" : "";
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message
            : $"Станция у «{plan.Target!.Name}», длина {plan.Length}{tracks}";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Депо, автогараж или городской пункт.</summary>
public sealed class FacilityTool(Builder builder, TypeId facility) : IBuildTool
{
    public string Id => facility.Value;

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanFacility(facility, from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildFacility(facility, from, to));

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

/// <summary>Мост (docs/10б): протянуть от опоры до опоры по прямой.</summary>
public sealed class BridgeTool(Builder builder, TypeId bridge) : IBuildTool
{
    public string Id => bridge.Value;

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanBridge(bridge, from, to), from == to);

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildBridge(bridge, from, to), false);

    private static ToolPreview Describe(BridgePlan plan, bool idle)
    {
        var message = idle ? "Протяните от опоры до опоры по прямой: пролёт пройдёт над путями, дорогами и водой"
            : plan.Problems.Count > 0 ? plan.Problems[0].Message
            : $"Мост: {plan.Cells.Count} кл.";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Сигнал (docs/10а): щелчок — двусторонний, протяжка вдоль пути — односторонний в сторону протяжки.</summary>
public sealed class SignalTool(Builder builder, TypeId signal) : IBuildTool
{
    public string Id => signal.Value;

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanSignal(signal, from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildSignal(signal, from, to));

    private static ToolPreview Describe(SignalPlan plan)
    {
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message
            : plan.Signal!.TwoWay ? "Двусторонний: протяните вдоль пути, чтобы сделать односторонним"
            : "Односторонний — в сторону протяжки";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Снять сигналы прямоугольником; пути остаются.</summary>
public sealed class SignalRemoveTool(Builder builder) : IBuildTool
{
    public string Id => "tool.remove_signals";

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanRemoveSignals(from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.RemoveSignals(from, to));

    private static ToolPreview Describe(BuildPlan plan) =>
        new(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild,
            plan.Problems.Count > 0 ? plan.Problems[0].Message : $"Снять сигналы: клеток {plan.Cells.Count}");
}

/// <summary>Снос прямоугольником: сети и станции.</summary>
public sealed class DemolishTool(Builder builder) : IBuildTool
{
    public string Id => "tool.demolish";

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanDemolish(from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.Demolish(from, to));

    private static ToolPreview Describe(BuildPlan plan) =>
        new(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild,
            plan.Problems.Count > 0 ? plan.Problems[0].Message : $"Снести клеток: {plan.Cells.Count}");
}

public static class BuildTools
{
    /// <summary>Инструменты по правилам строительства: по одному на каждую сеть, мост, станцию, сигнал, плюс снос.</summary>
    public static IReadOnlyList<IBuildTool> Create(Builder builder) =>
    [
        .. builder.Catalog.Networks.Select(n => (IBuildTool)new PathTool(builder, n.Network)),
        .. builder.Catalog.Bridges.Select(b => (IBuildTool)new BridgeTool(builder, b.Id)),
        .. builder.Catalog.Stations.Select(s => (IBuildTool)new StationTool(builder, s.Id)),
        .. builder.Catalog.Facilities.Select(f => (IBuildTool)new FacilityTool(builder, f.Id)),
        .. builder.Catalog.Signals.Select(s => (IBuildTool)new SignalTool(builder, s.Id)),
        .. builder.Catalog.Signals.Count > 0 ? new IBuildTool[] { new SignalRemoveTool(builder) } : [],
        new DemolishTool(builder),
    ];
}
